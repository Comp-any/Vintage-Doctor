using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using VintageDoctor.API;

namespace VintageDoctor
{
    /// <summary>Something a mod told the doctor went wrong while playing (<see cref="IDoctorApi.ReportIncident"/>).</summary>
    public sealed class Incident
    {
        public string Mod, Title, Details;
        public DoctorLevel Level;
        public int Count;
        public DateTime First, Last;
    }

    /// <summary>A finding a mod handed over on the event bus (see docs/api.md).</summary>
    public sealed class BusResult
    {
        public string Mod, Title, Advice, FixId, FixArg, FixLabel, Link, LinkArg, LinkLabel;
        public DoctorLevel Level;
        public List<string> Details = new List<string>();
    }

    /// <summary>
    /// Vintage Doctor: works out what's wrong with the game and its mods and fixes what it can. One of these runs on
    /// each side of the game. Other mods reach it through <see cref="IDoctorApi"/>.
    /// </summary>
    public partial class VintageDoctorSystem : ModSystem, IDoctorApi
    {
        public const string Channel = "vintagedoctor";
        public const string ConfigFile = "vintagedoctor.json";
        public const string CollectEvent = "vintagedoctor:collect", FixEvent = "vintagedoctor:fix";

        ICoreAPI api;
        public DoctorConfig Config = new DoctorConfig();
        public readonly KnownProblems Known = new KnownProblems();
        public readonly TickWatch Ticks = new TickWatch();
        public readonly TickProfile Profile = new TickProfile();
        public readonly MemoryWatch MemoryTrend = new MemoryWatch();
        public string DataFolder { get; private set; }
        public bool IsDedicated { get; private set; }

        public string DoctorVersion => Mod?.Info?.Version ?? "?";
        public int ApiVersion => 3;

        // What this session started from (read once per side at start).
        public SessionRecord PreviousSession { get; internal set; }
        public bool PreviousUnclean { get; private set; }
        public string PreviousSessionTime => PreviousSession?.Time;
        public bool NewCrashThisSession { get; private set; }
        DoctorState state;
        SessionRecord thisSession;
        string statePath;

        readonly object gate = new object();
        readonly Dictionary<string, DoctorCheck> checks = new Dictionary<string, DoctorCheck>(StringComparer.Ordinal);
        readonly Dictionary<string, DoctorFix> fixes = new Dictionary<string, DoctorFix>(StringComparer.Ordinal);
        readonly Dictionary<string, DoctorTopic> topics = new Dictionary<string, DoctorTopic>(StringComparer.Ordinal);
        readonly Dictionary<string, JsonFix> jsonFixes = new Dictionary<string, JsonFix>(StringComparer.Ordinal);
        readonly List<Incident> incidents = new List<Incident>();

        public override bool ShouldLoad(EnumAppSide forSide) => true;
        // Early, so the API is there when other mods start; the registries themselves exist from construction.
        public override double ExecuteOrder() => 0.02;

        public override void StartPre(ICoreAPI api)
        {
            this.api = api;
            try { DataFolder = api.GetOrCreateDataPath(Path.Combine("ModData", DoctorApi.ModId)); }
            catch { DataFolder = Path.Combine(GamePaths.DataPath, "ModData", DoctorApi.ModId); }
            LoadConfig();
        }

        public override void Start(ICoreAPI api)
        {
            this.api = api;
            api.Network.RegisterChannel(Channel)
                .RegisterMessageType<DoctorRequestPacket>()
                .RegisterMessageType<DoctorReportPacket>()
                .RegisterMessageType<DoctorOpenPacket>();
            BuiltInFixes.Register(this, api);
            StartGuests(api);
        }

        public override void AssetsLoaded(ICoreAPI api)
        {
            try { LoadKnownProblems(api); }
            catch (Exception e) { api.Logger.Warning("[vintagedoctor] Could not read the known-problem lists: {0}", e.Message); }
        }

        void LoadConfig()
        {
            try
            {
                DoctorConfig loaded = api.LoadModConfig<DoctorConfig>(ConfigFile);
                if (loaded != null) Config = loaded;
                Config.IgnoreLogMessages ??= new List<string>();
                Config.KnownProblems ??= new List<JObject>();
                if (string.IsNullOrWhiteSpace(Config.ServerReportPrivilege)) Config.ServerReportPrivilege = Privilege.controlserver;
                // Written back so new settings show up in the file. In single player both sides share it: only one writes.
                if (api.Side == EnumAppSide.Server || !(api is ICoreClientAPI c) || !c.IsSinglePlayer) api.StoreModConfig(Config, ConfigFile);
            }
            catch (Exception e)
            {
                api.Logger.Warning("[vintagedoctor] Could not read ModConfig/{0} ({1}); using the defaults.", ConfigFile, e.Message);
                Config = new DoctorConfig();
            }
        }

        /// <summary>Every mod's assets/&lt;modid&gt;/config/vintagedoctor.json (the doctor's own is one of them), then the owner's list in the config.</summary>
        void LoadKnownProblems(ICoreAPI api)
        {
            foreach (IAsset asset in api.Assets.GetMany("config/vintagedoctor.json", null, true))
            {
                string source = asset.Location.ToString();
                try
                {
                    JToken root = JToken.Parse(asset.ToText());
                    foreach (JsonFix fix in Known.LoadJson(asset.Location.Domain, source, root)) RegisterJsonFix(fix);
                }
                catch (Exception e) { Known.LoadErrors.Add(source + ": " + e.Message); }
            }
            if (Config.KnownProblems != null && Config.KnownProblems.Count > 0)
            {
                var root = new JObject { ["knownProblems"] = new JArray(Config.KnownProblems.Cast<object>().ToArray()) };
                foreach (JsonFix fix in Known.LoadJson("server", "ModConfig/" + ConfigFile, root)) RegisterJsonFix(fix);
            }
        }

        void RegisterJsonFix(JsonFix fix)
        {
            lock (gate) jsonFixes[fix.Id] = fix;
            if (fix.Type == "url") return; // shown as a link, nothing to run
            RegisterFix(new DoctorFix(fix.ModId, fix.Id, ctx => BuiltInFixes.RunJsonFix(this, fix, (DoctorFixContext)ctx)));
        }

        public JsonFix JsonFixFor(string fixId)
        {
            lock (gate) return fixId != null && jsonFixes.TryGetValue(fixId, out JsonFix f) ? f : null;
        }

        /// <summary>The words on a fix button when the check gave none.</summary>
        public string FixLabel(string fixId, DoctorContext x)
        {
            JsonFix jf = JsonFixFor(fixId);
            if (jf == null) return x.T("btn-fix");
            if (jf.Type == "command") return x.T("btn-run-command", jf.Command ?? "");
            if (jf.Type == "resetconfig") return x.T("btn-reset-config", jf.File ?? "");
            return x.T("btn-fix");
        }

        // ---------------------------------------------------------------- the API

        public void RegisterCheck(DoctorCheck check)
        {
            if (check == null || string.IsNullOrWhiteSpace(check.Id) || check.Run == null) throw new ArgumentException("A doctor check needs an Id and a Run action.");
            lock (gate) checks[check.Id] = check;
        }

        public void RegisterFix(DoctorFix fix)
        {
            if (fix == null || string.IsNullOrWhiteSpace(fix.Id) || fix.Apply == null) throw new ArgumentException("A doctor fix needs an Id and an Apply function.");
            lock (gate) fixes[fix.Id] = fix;
        }

        public void RegisterTopic(DoctorTopic topic)
        {
            if (topic == null || string.IsNullOrWhiteSpace(topic.Id) || topic.Run == null) throw new ArgumentException("A doctor topic needs an Id and a Run action.");
            string id = topic.Id.Trim().ToLowerInvariant();
            if (!ValidTopicId(id))
                throw new ArgumentException("The topic id '" + topic.Id + "' is taken by the doctor itself or has characters other than a-z, 0-9 and '-'.");
            topic.Id = id;
            lock (gate) topics[id] = topic;
        }

        public void RegisterKnownProblem(KnownProblem problem)
        {
            if (problem == null) return;
            Known.Add(problem);
            cached = null;
        }

        public void ReportIncident(string modId, DoctorLevel level, string title, string details = null)
        {
            if (string.IsNullOrWhiteSpace(title)) return;
            lock (gate)
            {
                Incident same = incidents.FirstOrDefault(i => i.Mod == modId && i.Title == title);
                if (same != null) { same.Count++; same.Last = DateTime.Now; same.Details = details ?? same.Details; if (level > same.Level) same.Level = level; return; }
                if (incidents.Count >= 200) incidents.RemoveAt(0);
                incidents.Add(new Incident { Mod = modId, Level = level, Title = title, Details = details, Count = 1, First = DateTime.Now, Last = DateTime.Now });
            }
        }

        public void Unregister(string modId)
        {
            if (string.IsNullOrEmpty(modId) || modId == DoctorApi.ModId) return;
            lock (gate)
            {
                foreach (string id in checks.Where(kv => kv.Value.ModId == modId).Select(kv => kv.Key).ToList()) checks.Remove(id);
                foreach (string id in fixes.Where(kv => kv.Value.ModId == modId).Select(kv => kv.Key).ToList()) fixes.Remove(id);
                foreach (string id in topics.Where(kv => kv.Value.ModId == modId).Select(kv => kv.Key).ToList()) topics.Remove(id);
                incidents.RemoveAll(i => i.Mod == modId);
                factProviders.Remove(modId);
            }
            Known.RemoveMod(modId);
        }

        public List<DoctorCheck> ChecksFor(string modId)
        {
            lock (gate) return checks.Values.Where(c => modId == null || c.ModId == modId).OrderBy(c => c.ModId ?? "", StringComparer.Ordinal).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
        }

        public List<Incident> IncidentsFor(string modId)
        {
            lock (gate) return incidents.Where(i => modId == null || i.Mod == modId).OrderByDescending(i => i.Last).Take(30).ToList();
        }

        /// <summary>A mod's page by its id: one registered in code, or one offered on the event bus.</summary>
        public DoctorTopic TopicFor(string id)
        {
            if (id == null) return null;
            RefreshBusTopics();
            lock (gate) return topics.TryGetValue(id, out DoctorTopic t) || busTopics.TryGetValue(id, out t) ? t : null;
        }

        public List<DoctorTopic> AllTopics()
        {
            RefreshBusTopics();
            lock (gate) return topics.Values.Concat(busTopics.Values.Where(b => !topics.ContainsKey(b.Id))).OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
        }

        public List<DoctorFix> AllFixes()
        {
            lock (gate) return fixes.Values.OrderBy(f => f.Id, StringComparer.Ordinal).ToList();
        }

        // ---------------------------------------------------------------- the event bus (for mods that don't reference this DLL)

        /// <summary>
        /// Asks on the event bus for findings: listeners of "vintagedoctor:collect" add a tree per finding under "results"
        /// (level, title, details, advice, fix, fixArgument, fixLabel, mod).
        /// </summary>
        public List<BusResult> CollectFromEventBus(DoctorContext x, string onlyMod, string eventName = CollectEvent)
        {
            var list = new List<BusResult>();
            try
            {
                var tree = new TreeAttribute();
                FillAsk(tree, x);
                if (onlyMod != null) tree.SetString("mod", onlyMod);
                tree["results"] = new TreeAttribute();
                api.Event.PushEvent(eventName, tree);
                if (!(tree["results"] is ITreeAttribute results)) return list;
                foreach (KeyValuePair<string, IAttribute> kv in results)
                {
                    if (!(kv.Value is ITreeAttribute r)) continue;
                    string title = r.GetString("title");
                    if (string.IsNullOrWhiteSpace(title)) continue;
                    int colon = kv.Key.IndexOf(':');
                    var b = new BusResult
                    {
                        Mod = r.GetString("mod") ?? (colon > 0 ? kv.Key.Substring(0, colon) : kv.Key),
                        Level = KnownProblems.ParseLevel(r.GetString("level"), DoctorLevel.Info),
                        Title = title,
                        Advice = r.GetString("advice"),
                        FixId = r.GetString("fix"),
                        FixArg = r.GetString("fixArgument"),
                        FixLabel = r.GetString("fixLabel"),
                        Link = r.GetString("link"),
                        LinkArg = r.GetString("linkArgument"),
                        LinkLabel = r.GetString("linkLabel")
                    };
                    string details = r.GetString("details");
                    if (!string.IsNullOrEmpty(details)) b.Details.AddRange(details.Replace("\r\n", "\n").Split('\n').Where(d => d.Length > 0).Take(40));
                    if (onlyMod != null && b.Mod != onlyMod) continue;
                    list.Add(b);
                    if (list.Count >= 100) break;
                }
            }
            catch (Exception e) { api.Logger.Warning("[vintagedoctor] Collecting findings on the event bus failed: {0}", e.Message); }
            return list;
        }

        /// <summary>A fix over the event bus, for callers that can't wait: a mod that needs time gets it, and the caller is told so.</summary>
        string RunBusFix(string id, string arg, IPlayer player, string lang)
        {
            ITreeAttribute tree = BusFixTree(id, arg, player, lang, null, NextRequest());
            api.Event.PushEvent(FixEvent, tree);
            if (tree.GetBool("later")) return Lang.GetL(lang ?? "en", "vintagedoctor:guest-fix-working", id);
            return BusFixOutcome(tree, id, lang);
        }

        // ---------------------------------------------------------------- fixes

        /// <summary>Does a fix and returns one line saying what happened (starting with ⚠ when it didn't work).</summary>
        public string RunFix(string fixId, string arg, IPlayer player, string lang, Evidence ev)
        {
            fixId = fixId ?? "";
            try
            {
                if (fixId.StartsWith("bus:", StringComparison.Ordinal)) return RunBusFix(fixId.Substring(4), arg, player, lang);
                DoctorFix fix;
                lock (gate) fixes.TryGetValue(fixId, out fix);
                if (fix == null) return DoctorReport.WarnMark + " " + Lang.GetL(lang ?? "en", "vintagedoctor:fix-unknown", fixId);
                var ctx = new DoctorFixContext { Doctor = this, Api = api, Side = api.Side, Player = player, LanguageCode = lang ?? "en", Argument = arg ?? "", Ev = ev };
                string outcome = fix.Apply(ctx);
                api.Logger.Notification("[vintagedoctor] Fix '{0}' ({1}) asked for by {2}: {3}", fixId, arg ?? "", player?.PlayerName ?? "the console", outcome ?? "");
                return string.IsNullOrEmpty(outcome) ? Lang.GetL(lang ?? "en", "vintagedoctor:fix-done") : outcome;
            }
            catch (Exception e)
            {
                api.Logger.Warning("[vintagedoctor] Fix '{0}' failed: {1}", fixId, e);
                return DoctorReport.WarnMark + " " + Lang.GetL(lang ?? "en", "vintagedoctor:fix-failed", fixId, e.Message);
            }
        }

        // ---------------------------------------------------------------- reading the disk

        ModSnapshot snapshot;
        Evidence cached;
        readonly object evidenceGate = new object();

        public ModSnapshot Snapshot()
        {
            if (snapshot == null) snapshot = ModSnapshot.Take(api);
            return snapshot;
        }

        public void ForgetEvidence() => cached = null;

        /// <summary>Reads the logs and folders off the main thread, then calls <paramref name="then"/> on it.</summary>
        public void WithEvidence(bool fresh, Action<Evidence> then)
        {
            Evidence ev = cached;
            if (!fresh && ev != null && (DateTime.Now - ev.TakenAt).TotalSeconds < 20) { then(ev); return; }
            ModSnapshot snap = Snapshot();
            EnumAppSide side = api.Side;
            Task.Run(() =>
            {
                Evidence e;
                lock (evidenceGate) e = Evidence.Gather(side, snap, Known, Config, true);
                api.Event.EnqueueMainThreadTask(() =>
                {
                    cached = e;
                    try { then(e); }
                    catch (Exception ex) { api.Logger.Error("[vintagedoctor] Building the page failed: {0}", ex); }
                }, "vintagedoctor-evidence");
            });
        }

        /// <summary>
        /// The check-up from this side alone, put in order by mod (<see cref="Pages.Arrange"/>): for the console, where
        /// there is no window to put two halves together.
        /// </summary>
        public DoctorReport CheckUp(DoctorReport half, Evidence ev, IPlayer player, string lang)
        {
            var final = new DoctorReport { Title = half.Title, Key = half.Key, File = half.File };
            var x = new DoctorContext
            {
                Doctor = this, Api = api, Side = api.Side, Player = player, LanguageCode = lang ?? "en", Topic = Pages.Overview, Argument = "", Ev = ev, R = final,
                HeldItem = player?.InventoryManager?.ActiveHotbarSlot?.Itemstack
            };
            Pages.Arrange(x, new[] { (half, (string)null) }, null, api.Side == EnumAppSide.Server, api.Side == EnumAppSide.Client);
            return final;
        }

        /// <summary>The same, here and now (for the console and the self-test, where waiting is fine).</summary>
        public Evidence EvidenceNow(bool fresh = true)
        {
            Evidence ev = cached;
            if (!fresh && ev != null && (DateTime.Now - ev.TakenAt).TotalSeconds < 20) return ev;
            lock (evidenceGate) ev = Evidence.Gather(api.Side, Snapshot(), Known, Config, true, 1500);
            cached = ev;
            return ev;
        }

        /// <summary>
        /// Builds this side's half of a page, here and now. A mod's page that will only come later (see
        /// <see cref="BuildPageThen"/>) is left out, with a line saying so.
        /// </summary>
        public DoctorReport BuildPage(Evidence ev, IPlayer player, string lang, string topic, string arg, ItemStack held = null,
            Vintagestory.API.MathTools.BlockPos lookedAt = null, bool sameProcess = false, string sideLabel = null, ITreeAttribute facts = null)
        {
            DoctorContext x = NewContext(ev, player, lang, topic, arg, held, lookedAt, sameProcess, sideLabel, facts, false);
            BuildInto(x);
            if (x.Pending != null)
            {
                x.R.Lines.Insert(Math.Min(x.Pending.InsertAt, x.R.Lines.Count), new DoctorLine { Text = x.T("guest-working", x.ModName(x.Pending.Mod)), Style = DoctorLineStyle.Note });
                x.Pending = null;
            }
            return x.R;
        }

        public string SaveText(string name, string text)
        {
            try
            {
                Directory.CreateDirectory(DataFolder);
                string file = Path.Combine(DataFolder, name);
                File.WriteAllText(file, text);
                return file;
            }
            catch (Exception e) { api.Logger.Warning("[vintagedoctor] Could not write {0}: {1}", name, e.Message); return null; }
        }

        // ---------------------------------------------------------------- sessions

        /// <summary>Notes this session in the state file and reads what the last one left: did it end properly, is there a new crash report?</summary>
        void BeginSession()
        {
            try
            {
                statePath = Path.Combine(DataFolder, "state-" + (api.Side == EnumAppSide.Server ? "server" : "client") + ".json");
                state = DoctorState.Load(statePath);
                PreviousSession = state.Sessions.LastOrDefault();
                // A mod that closed the game on purpose (to install updates, say) said so first: not a surprise.
                PreviousUnclean = PreviousSession != null && !PreviousSession.CleanExit && string.IsNullOrEmpty(PreviousSession.ClosedBy);
                long crashBytes = 0;
                try { var fi = new FileInfo(Evidence.CrashFile(api.Side)); if (fi.Exists) crashBytes = fi.Length; } catch { }
                NewCrashThisSession = state.CrashLogBytes >= 0 && crashBytes > state.CrashLogBytes;
                state.CrashLogBytes = crashBytes;
                thisSession = new SessionRecord
                {
                    Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                    GameVersion = GameVersion.ShortGameVersion
                };
                foreach (ModEntry m in Snapshot().UserMods) thisSession.Mods[m.Id] = m.Version;
                state.Sessions.Add(thisSession);
                while (state.Sessions.Count > 12) state.Sessions.RemoveAt(0);
                state.Save(statePath);
                lock (live) if (!live.Contains(this)) live.Add(this);
            }
            catch (Exception e) { api.Logger.Warning("[vintagedoctor] Could not keep the session notes: {0}", e.Message); }
        }

        /// <summary>Every doctor running in this program: the player's game and, in single player, its built-in server.</summary>
        static readonly List<VintageDoctorSystem> live = new List<VintageDoctorSystem>();

        /// <summary>
        /// A mod is about to close the game on purpose (1.6.0, "vintagedoctor:closing"): noted at once in this
        /// session's record, by every doctor in this program (the game going down takes a single player's server
        /// with it), so the next start says who closed it and why instead of "didn't close properly".
        /// </summary>
        internal void NoteClosing(string modId, string reason)
        {
            List<VintageDoctorSystem> all;
            lock (live) all = live.ToList();
            foreach (VintageDoctorSystem d in all)
            {
                try
                {
                    if (d.thisSession == null || d.state == null) continue;
                    d.thisSession.ClosedBy = string.IsNullOrWhiteSpace(modId) ? "?" : modId.Trim();
                    d.thisSession.ClosedWhy = (reason ?? "").Trim();
                    d.state.Save(d.statePath);
                }
                catch (Exception e) { d.api?.Logger.Warning("[vintagedoctor] Could not note that {0} is closing the game: {1}", modId, e.Message); }
            }
            api?.Logger.Notification("[vintagedoctor] {0} says it is closing the game{1}", modId, string.IsNullOrWhiteSpace(reason) ? "" : ": " + reason);
        }

        /// <summary>For the self-test: this session isn't being closed on purpose after all.</summary>
        internal void ForgetClosing()
        {
            List<VintageDoctorSystem> all;
            lock (live) all = live.ToList();
            foreach (VintageDoctorSystem d in all) { if (d.thisSession != null) { d.thisSession.ClosedBy = null; d.thisSession.ClosedWhy = null; try { d.state.Save(d.statePath); } catch { } } }
        }

        void EndSession()
        {
            try
            {
                if (thisSession == null || state == null) return;
                thisSession.CleanExit = true;
                state.Save(statePath);
            }
            catch { }
        }

        public override void Dispose()
        {
            try { Profile.Stop(); } catch { }
            EndSession();
            lock (live) live.Remove(this);
            base.Dispose();
        }
    }
}
