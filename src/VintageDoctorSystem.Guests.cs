using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VintageDoctor.API;

namespace VintageDoctor
{
    /// <summary>A page a mod said it would deliver later: where its lines go once they come.</summary>
    public sealed class Deferred
    {
        public string Request, Mod;
        public int Seconds = 60;
        /// <summary>Where in the page the mod's lines go.</summary>
        public int InsertAt;
    }

    /// <summary>
    /// A mod as a guest in the doctor: whole pages it builds itself (now, or a few seconds later), fixes that finish
    /// later, what its copy on the player's game has to say to the server, and opening the window at one of its pages.
    /// All of it works over the event bus, so a mod with a doctor of its own can hand that over without referencing
    /// this DLL (docs/api.md, "A whole doctor of your own").
    /// </summary>
    public partial class VintageDoctorSystem
    {
        public const string TopicsEvent = "vintagedoctor:topics", PageEvent = "vintagedoctor:page", DeliverEvent = "vintagedoctor:deliver",
            ClientFactsEvent = "vintagedoctor:clientfacts", OpenEvent = "vintagedoctor:open", AboutEvent = "vintagedoctor:about";

        const int MaxGuestLines = 3000, MaxFactBytes = 64 * 1024, MaxAllFactBytes = 256 * 1024;

        sealed class Waiting
        {
            public DoctorContext Page;            // a page waiting for a mod's lines...
            public Action<DoctorReport> PageThen;
            public Action<string> FixThen;        // ...or a fix waiting for its outcome
            public string Mod, Lang, FixId;
        }

        readonly Dictionary<string, Waiting> waiting = new Dictionary<string, Waiting>(StringComparer.Ordinal);
        readonly Dictionary<string, DoctorTopic> busTopics = new Dictionary<string, DoctorTopic>(StringComparer.Ordinal);
        readonly Dictionary<string, System.Func<string, string, byte[]>> factProviders = new Dictionary<string, System.Func<string, string, byte[]>>(StringComparer.Ordinal);
        readonly HashSet<string> badTopicsSaid = new HashSet<string>(StringComparer.Ordinal);
        long busTopicsAt = long.MinValue / 2;
        int requestCounter;

        string SideName => api.Side == EnumAppSide.Server ? "server" : "client";

        void StartGuests(ICoreAPI api)
        {
            api.Event.RegisterEventBusListener(OnDeliver, 0.5, DeliverEvent);
            api.Event.RegisterEventBusListener(OnOpenAsked, 0.5, OpenEvent);
        }

        string NextRequest() => SideName[0] + "-" + (++requestCounter).ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// A text a mod gave, in a player's language: when it is a lang key ("mymod:doctor-question", no spaces) that
        /// has a translation, the translation; otherwise the text as it is.
        /// </summary>
        public static string WordsIn(string lang, string textOrKey)
        {
            if (string.IsNullOrEmpty(textOrKey)) return "";
            if (textOrKey.IndexOf(' ') < 0 && textOrKey.IndexOf(':') > 0)
            {
                try { if (Lang.HasTranslation(textOrKey, false, false)) return Lang.GetL(string.IsNullOrEmpty(lang) ? "en" : lang, textOrKey); } catch { }
            }
            return textOrKey;
        }

        static bool ValidTopicId(string id)
        {
            return !string.IsNullOrEmpty(id) && !Pages.Topics.Contains(id) && id != "fix" && id != "selftest" && id != "hello" && Regex.IsMatch(id, "^[a-z0-9-]+$");
        }

        // ---------------------------------------------------------------- pages a mod builds itself

        /// <summary>
        /// Asks on the event bus which pages mods have ("vintagedoctor:topics": a tree per page id under "topics", with
        /// question, mod and ownHeadings). Asked again every few seconds at most, so a mod that starts late is still found.
        /// </summary>
        void RefreshBusTopics()
        {
            long now = Environment.TickCount64;
            if (now - busTopicsAt < 3000) return;
            busTopicsAt = now;
            var found = new Dictionary<string, DoctorTopic>(StringComparer.Ordinal);
            try
            {
                var tree = new TreeAttribute();
                tree.SetString("side", SideName);
                tree["topics"] = new TreeAttribute();
                api.Event.PushEvent(TopicsEvent, tree);
                if (tree["topics"] is ITreeAttribute list)
                {
                    foreach (KeyValuePair<string, IAttribute> kv in list)
                    {
                        if (!(kv.Value is ITreeAttribute t)) continue;
                        string id = (kv.Key ?? "").Trim().ToLowerInvariant();
                        if (!ValidTopicId(id))
                        {
                            if (badTopicsSaid.Add(id)) api.Logger.Warning("[vintagedoctor] A mod offered a page with the id '{0}', which is taken by the doctor itself or has characters other than a-z, 0-9 and '-'. It was left out.", id);
                            continue;
                        }
                        string mod = t.GetString("mod") ?? id;
                        found[id] = new DoctorTopic
                        {
                            Id = id, ModId = mod, Question = t.GetString("question") ?? id, OwnHeadings = t.GetBool("ownHeadings"),
                            Run = ctx => RunBusPage((DoctorContext)ctx, id, mod)
                        };
                        if (found.Count >= 50) break;
                    }
                }
            }
            catch (Exception e) { api.Logger.Warning("[vintagedoctor] Asking mods for their pages on the event bus failed: {0}", e.Message); }
            lock (gate)
            {
                busTopics.Clear();
                foreach (KeyValuePair<string, DoctorTopic> kv in found) busTopics[kv.Key] = kv.Value;
            }
        }

        /// <summary>Asks again at the next chance (for the self-test, whose pages come and go within a moment).</summary>
        public void ForgetBusTopics() => busTopicsAt = long.MinValue / 2;

        /// <summary>What every question on the event bus carries: who asks, about what, in which language, what they hold and look at, and what their own game sent along.</summary>
        void FillAsk(ITreeAttribute tree, DoctorContext x)
        {
            tree.SetString("side", x.Side == EnumAppSide.Server ? "server" : "client");
            tree.SetString("topic", x.Topic ?? "");
            tree.SetString("argument", x.Argument ?? "");
            tree.SetString("language", x.LanguageCode ?? "en");
            tree.SetString("player", x.Player?.PlayerUID ?? "");
            tree.SetBool("compact", x.Compact);
            FillExtras(tree, x.HeldItem, x.LookedAt, x.Facts);
            if (x.Subject?.Code != null) tree.SetString("subject", (x.Subject is Block ? "block:" : "item:") + x.Subject.Code);
        }

        static void FillExtras(ITreeAttribute tree, ItemStack held, BlockPos lookedAt, ITreeAttribute facts)
        {
            try { if (held?.Collectible?.Code != null) { tree.SetBytes("held", held.ToBytes()); tree.SetString("heldCode", (held.Collectible is Block ? "block:" : "item:") + held.Collectible.Code); } } catch { }
            if (lookedAt != null)
            {
                tree.SetBool("hasLookedAt", true);
                tree.SetInt("lookedAtX", lookedAt.X);
                tree.SetInt("lookedAtY", lookedAt.Y);
                tree.SetInt("lookedAtZ", lookedAt.Z);
                tree.SetInt("lookedAtDim", lookedAt.dimension);
            }
            if (facts != null) tree["clientfacts"] = facts;
        }

        /// <summary>A mod's page over the event bus: its lines now, or its word that they will come ("later").</summary>
        void RunBusPage(DoctorContext x, string topicId, string modId)
        {
            string request = NextRequest();
            var tree = new TreeAttribute();
            FillAsk(tree, x);
            tree.SetString("topic", topicId);
            tree.SetString("request", request);
            api.Event.PushEvent(PageEvent, tree);
            if (tree.GetBool("later"))
            {
                x.Pending = new Deferred { Request = request, Mod = modId, Seconds = Math.Max(5, Math.Min(300, tree.GetInt("seconds", 60))), InsertAt = x.R.Lines.Count };
                return;
            }
            if (AppendGuestLines(x, tree, x.R.Lines.Count) == 0)
                x.R.Add(DoctorReport.InfoMark + " " + x.T("guest-empty", x.ModName(modId)), DoctorLineStyle.Check);
        }

        static readonly Regex GuestFix = new Regex(@"\{\{fix\|([^|{}]*)\|", RegexOptions.Compiled);

        /// <summary>
        /// Puts a mod's lines into the page: "texts" (a string array), and optionally "styles" and "indents" (a byte each
        /// per line; styles as <see cref="DoctorLineStyle"/>). A fix button written without a side ("{{fix|mymod:do|arg|label}}")
        /// becomes one for this side's event bus. Returns how many lines came.
        /// </summary>
        int AppendGuestLines(DoctorContext x, ITreeAttribute tree, int at)
        {
            string[] texts = (tree["texts"] as StringArrayAttribute)?.value;
            if (texts == null || texts.Length == 0) return 0;
            byte[] styles = tree.GetBytes("styles", null), indents = tree.GetBytes("indents", null);
            char side = x.SideLetter;
            var lines = new List<DoctorLine>(Math.Min(texts.Length, MaxGuestLines));
            for (int i = 0; i < texts.Length && i < MaxGuestLines; i++)
            {
                string text = texts[i] ?? "";
                if (text.Length > 4000) text = text.Substring(0, 4000);
                if (text.IndexOf("{{fix|", StringComparison.Ordinal) >= 0)
                {
                    text = GuestFix.Replace(text, m =>
                    {
                        string target = m.Groups[1].Value;
                        bool sided = target.Length > 2 && target[1] == ':' && (target[0] == 's' || target[0] == 'c');
                        return sided ? m.Value : "{{fix|" + side + ":bus:" + target + "|";
                    });
                }
                byte style = styles != null && i < styles.Length ? styles[i] : (byte)0;
                if (style > (byte)DoctorLineStyle.Check) style = 0;
                lines.Add(new DoctorLine { Text = text, Style = (DoctorLineStyle)style, Indent = indents != null && i < indents.Length ? Math.Min((int)indents[i], 20) : 0 });
            }
            at = Math.Max(0, Math.Min(at, x.R.Lines.Count));
            x.R.Lines.InsertRange(at, lines);
            return lines.Count;
        }

        // ---------------------------------------------------------------- waiting for a mod

        void OnDeliver(string eventName, ref EnumHandling handling, IAttribute data)
        {
            if (!(data is ITreeAttribute tree)) return;
            string request = tree.GetString("request");
            if (request == null) return;
            Waiting w;
            lock (gate)
            {
                if (!waiting.TryGetValue(request, out w)) return;
                waiting.Remove(request);
            }
            tree.SetBool("accepted", true);
            Complete(w, tree);
        }

        void Expire(string request)
        {
            Waiting w;
            lock (gate)
            {
                if (!waiting.TryGetValue(request, out w)) return;
                waiting.Remove(request);
            }
            api.Logger.Warning("[vintagedoctor] {0} said it would deliver '{1}' later and didn't in time.", w.Mod ?? "A mod", w.FixId ?? w.Page?.Topic ?? "?");
            Complete(w, null);
        }

        /// <summary>For the self-test: what a mod that never answers leaves behind.</summary>
        public bool ExpireNow(string request)
        {
            bool had;
            lock (gate) had = waiting.ContainsKey(request);
            if (had) Expire(request);
            return had;
        }

        /// <summary>The request ids the doctor is waiting on (for the self-test).</summary>
        public List<string> WaitingRequests()
        {
            lock (gate) return waiting.Keys.ToList();
        }

        /// <summary>Finishes what was waiting, with what the mod delivered (null: it never did).</summary>
        void Complete(Waiting w, ITreeAttribute tree)
        {
            try
            {
                string lang = w.Lang ?? "en";
                string name = Snapshot().NameOf(w.Mod);
                if (w.FixThen != null)
                {
                    w.FixThen(tree == null ? DoctorReport.WarnMark + " " + Lang.GetL(lang, "vintagedoctor:guest-timeout", name) : BusFixOutcome(tree, w.FixId, lang));
                    return;
                }
                DoctorContext x = w.Page;
                Deferred d = x.Pending;
                x.Pending = null;
                int at = d?.InsertAt ?? x.R.Lines.Count;
                if (tree == null) x.R.Lines.Insert(Math.Min(at, x.R.Lines.Count), new DoctorLine { Text = DoctorReport.WarnMark + " " + x.T("guest-timeout", name), Style = DoctorLineStyle.Check });
                else if (AppendGuestLines(x, tree, at) == 0)
                    x.R.Lines.Insert(Math.Min(at, x.R.Lines.Count), new DoctorLine { Text = DoctorReport.InfoMark + " " + x.T("guest-empty", name), Style = DoctorLineStyle.Check });
                w.PageThen?.Invoke(x.R);
            }
            catch (Exception e) { api.Logger.Error("[vintagedoctor] Finishing a page or fix a mod delivered later failed: {0}", e); }
        }

        DoctorContext NewContext(Evidence ev, IPlayer player, string lang, string topic, string arg, ItemStack held, BlockPos lookedAt, bool sameProcess, string sideLabel,
            ITreeAttribute facts, bool otherSideHasTopic)
        {
            return new DoctorContext
            {
                Doctor = this, Api = api, Side = api.Side, Player = player, LanguageCode = string.IsNullOrEmpty(lang) ? "en" : lang, Topic = topic ?? "", Argument = arg ?? "",
                HeldItem = held, LookedAt = lookedAt, SameProcess = sameProcess, Ev = ev, SideLabel = sideLabel, Facts = facts, OtherSideHasTopic = otherSideHasTopic
            };
        }

        void BuildInto(DoctorContext x)
        {
            string asked = x.Topic;
            try { Pages.Build(x); }
            catch (Exception e)
            {
                api.Logger.Error("[vintagedoctor] Building the page '{0}' failed: {1}", asked, e);
                x.Owner = null;
                x.Warn(x.T("check-crashed", asked ?? "?", e.GetType().Name + ": " + e.Message));
            }
        }

        /// <summary>
        /// Builds this side's half of a page and hands it to <paramref name="then"/>: at once, or, when a mod's page
        /// said "later", once the mod delivers (or its time is up). <paramref name="working"/> is told how many
        /// seconds that may take, so whoever waits can be told too.
        /// </summary>
        public void BuildPageThen(Evidence ev, IPlayer player, string lang, string topic, string arg, ItemStack held, BlockPos lookedAt, bool sameProcess, string sideLabel,
            ITreeAttribute facts, bool otherSideHasTopic, Action<DoctorReport> then, Action<int> working = null)
        {
            DoctorContext x = NewContext(ev, player, lang, topic, arg, held, lookedAt, sameProcess, sideLabel, facts, otherSideHasTopic);
            BuildInto(x);
            Deferred d = x.Pending;
            if (d == null) { then(x.R); return; }
            lock (gate) waiting[d.Request] = new Waiting { Page = x, PageThen = then, Mod = d.Mod, Lang = x.LanguageCode };
            try { working?.Invoke(d.Seconds); } catch { }
            string request = d.Request;
            api.Event.RegisterCallback(_ => Expire(request), d.Seconds * 1000);
        }

        // ---------------------------------------------------------------- fixes over the event bus

        ITreeAttribute BusFixTree(string id, string arg, IPlayer player, string lang, ITreeAttribute facts, string request)
        {
            var tree = new TreeAttribute();
            tree.SetString("id", id);
            tree.SetString("argument", arg ?? "");
            tree.SetString("side", SideName);
            tree.SetString("language", lang ?? "en");
            tree.SetString("player", player?.PlayerUID ?? "");
            tree.SetString("request", request);
            ItemStack held = null;
            try { held = player?.InventoryManager?.ActiveHotbarSlot?.Itemstack; } catch { }
            FillExtras(tree, held, null, facts);
            return tree;
        }

        static string BusFixOutcome(ITreeAttribute tree, string id, string lang)
        {
            string result = tree.GetString("result");
            if (result == null) return DoctorReport.WarnMark + " " + Lang.GetL(lang ?? "en", "vintagedoctor:fix-nobody", id);
            return tree.GetBool("failed") ? DoctorReport.WarnMark + " " + result : result;
        }

        /// <summary>
        /// Does a fix and hands the outcome to <paramref name="then"/>: at once for the doctor's own fixes and those
        /// registered in code, later when a mod on the event bus says it needs time ("later").
        /// </summary>
        public void RunFixThen(string fixId, string arg, IPlayer player, string lang, Evidence ev, ITreeAttribute facts, Action<string> then, Action<int> working = null)
        {
            fixId = fixId ?? "";
            if (!fixId.StartsWith("bus:", StringComparison.Ordinal)) { then(RunFix(fixId, arg, player, lang, ev)); return; }
            string id = fixId.Substring(4), request = NextRequest();
            try
            {
                ITreeAttribute tree = BusFixTree(id, arg, player, lang, facts, request);
                api.Event.PushEvent(FixEvent, tree);
                if (!tree.GetBool("later"))
                {
                    string outcome = BusFixOutcome(tree, id, lang);
                    api.Logger.Notification("[vintagedoctor] Fix '{0}' ({1}) asked for by {2}: {3}", fixId, arg ?? "", player?.PlayerName ?? "the console", outcome);
                    then(outcome);
                    return;
                }
                int seconds = Math.Max(5, Math.Min(300, tree.GetInt("seconds", 60)));
                int colon = id.IndexOf(':');
                lock (gate) waiting[request] = new Waiting { FixThen = then, Mod = colon > 0 ? id.Substring(0, colon) : id, Lang = lang ?? "en", FixId = id };
                api.Logger.Notification("[vintagedoctor] Fix '{0}' ({1}) asked for by {2}: its mod is working on it.", fixId, arg ?? "", player?.PlayerName ?? "the console");
                try { working?.Invoke(seconds); } catch { }
                api.Event.RegisterCallback(_ => Expire(request), seconds * 1000);
            }
            catch (Exception e)
            {
                api.Logger.Warning("[vintagedoctor] Fix '{0}' failed: {1}", fixId, e);
                then(DoctorReport.WarnMark + " " + Lang.GetL(lang ?? "en", "vintagedoctor:fix-failed", fixId, e.Message));
            }
        }

        // ---------------------------------------------------------------- what a mod on the player's game sends along

        public void RegisterClientFacts(string modId, System.Func<string, string, byte[]> collect)
        {
            if (string.IsNullOrWhiteSpace(modId) || collect == null) throw new ArgumentException("Client facts need a mod id and a function.");
            lock (gate) factProviders[modId] = collect;
        }

        /// <summary>
        /// On the player's side, before the server is asked for a page: what each mod wants its server half to know
        /// (registered in code, or added to "facts" by a listener of "vintagedoctor:clientfacts"). Null for nothing.
        /// </summary>
        public ITreeAttribute GatherClientFacts(string topic, string arg)
        {
            var facts = new TreeAttribute();
            List<KeyValuePair<string, System.Func<string, string, byte[]>>> providers;
            lock (gate) providers = factProviders.ToList();
            foreach (var kv in providers)
            {
                try
                {
                    byte[] b = kv.Value(topic ?? "", arg ?? "");
                    if (b != null && b.Length > 0 && b.Length <= MaxFactBytes) facts.SetBytes(kv.Key, b);
                }
                catch (Exception e) { api.Logger.Warning("[{0}] What it wanted to send along for Vintage Doctor failed: {1}", kv.Key, e.Message); }
            }
            try
            {
                var tree = new TreeAttribute();
                tree.SetString("topic", topic ?? "");
                tree.SetString("argument", arg ?? "");
                tree["facts"] = facts;
                api.Event.PushEvent(ClientFactsEvent, tree);
            }
            catch (Exception e) { api.Logger.Warning("[vintagedoctor] Asking mods what to send along failed: {0}", e.Message); }
            // Only byte arrays of a sensible size travel.
            foreach (string key in facts.Select(kv => kv.Key).ToList())
            {
                byte[] b = null;
                try { b = facts.GetBytes(key, null); } catch { }
                if (b == null || b.Length == 0 || b.Length > MaxFactBytes) facts.RemoveAttribute(key);
            }
            return facts.Count == 0 ? null : facts;
        }

        public static byte[] FactsToBytes(ITreeAttribute facts)
        {
            if (!(facts is TreeAttribute tree) || tree.Count == 0) return null;
            try
            {
                byte[] bytes = tree.ToBytes();
                return bytes.Length > MaxAllFactBytes ? null : bytes;
            }
            catch { return null; }
        }

        public static ITreeAttribute FactsFromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxAllFactBytes) return null;
            try { return TreeAttribute.CreateFromBytes(bytes); }
            catch { return null; }
        }

        // ---------------------------------------------------------------- opening the window from another mod

        void OnOpenAsked(string eventName, ref EnumHandling handling, IAttribute data)
        {
            if (!(data is ITreeAttribute tree)) return;
            try
            {
                IPlayer player = null;
                string uid = tree.GetString("player");
                if (api.Side == EnumAppSide.Server && !string.IsNullOrEmpty(uid)) player = api.World.PlayerByUid(uid);
                tree.SetBool("opened", OpenPage(tree.GetString("topic") ?? "", tree.GetString("argument") ?? "", player));
            }
            catch (Exception e) { api.Logger.Warning("[vintagedoctor] A mod asked to open the window and that failed: {0}", e.Message); }
        }

        public bool OpenPage(string topic, string argument = "", IPlayer player = null)
        {
            if (api.Side == EnumAppSide.Client) return OpenHere(topic ?? "", argument ?? "");
            var sp = player as IServerPlayer;
            if (sp == null || sp.ConnectionState != EnumClientState.Playing) return false;
            lock (gate) if (!clientsWithDoctor.Contains(sp.PlayerUID)) return false;
            serverChannel.SendPacket(new DoctorOpenPacket { Topic = topic ?? "", Arg = argument ?? "" }, sp);
            return true;
        }
    }
}
