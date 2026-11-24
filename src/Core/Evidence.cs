using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using VintageDoctor.API;

namespace VintageDoctor
{
    /// <summary>A config file (ModConfig/*.json, or one of the game's own) and whether it can be read.</summary>
    public sealed class ConfigFile
    {
        public string Path, Name;
        public long Bytes;
        /// <summary>Why it can't be read (the JSON error with its line), or null.</summary>
        public string Error;
        /// <summary>Nothing in it (a crash or a full disk while it was being saved leaves these).</summary>
        public bool Empty;
        /// <summary>One of the game's own files (serverconfig.json, clientsettings.json).</summary>
        public bool Game;
        /// <summary>The mod it most likely belongs to, or null.</summary>
        public string Mod;
        public bool Bad => Empty || Error != null;
    }

    public sealed class ConfigScan
    {
        public string Folder;
        public readonly List<ConfigFile> Files = new List<ConfigFile>();
        public string Error;

        public static ConfigScan Read(string modConfigFolder, string dataFolder, ModSnapshot mods, IEnumerable<KeyValuePair<string, string>> declared)
        {
            var scan = new ConfigScan { Folder = modConfigFolder };
            try
            {
                var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (declared != null) foreach (var kv in declared) owners[kv.Key] = kv.Value;
                if (Directory.Exists(modConfigFolder))
                {
                    var files = new List<string>(Directory.GetFiles(modConfigFolder, "*.json"));
                    foreach (string dir in Directory.GetDirectories(modConfigFolder).Take(60))
                    {
                        try { files.AddRange(Directory.GetFiles(dir, "*.json").Take(40)); } catch { }
                    }
                    foreach (string path in files.Take(600))
                    {
                        ConfigFile f = Check(path);
                        string rel = System.IO.Path.GetRelativePath(modConfigFolder, path).Replace('\\', '/');
                        f.Name = rel;
                        f.Mod = owners.TryGetValue(rel, out string owner) ? owner : GuessOwner(rel, mods);
                        scan.Files.Add(f);
                    }
                }
                foreach (string name in new[] { "serverconfig.json", "clientsettings.json", "servermagicnumbers.json" })
                {
                    string path = System.IO.Path.Combine(dataFolder ?? "", name);
                    if (!File.Exists(path)) continue;
                    ConfigFile f = Check(path);
                    f.Game = true;
                    scan.Files.Add(f);
                }
            }
            catch (Exception e) { scan.Error = e.Message; }
            return scan;
        }

        /// <summary>Reads one JSON file the way the game does (comments allowed) and says whether that works.</summary>
        public static ConfigFile Check(string path)
        {
            var f = new ConfigFile { Path = path, Name = System.IO.Path.GetFileName(path) };
            try
            {
                var info = new FileInfo(path);
                f.Bytes = info.Length;
                if (info.Length > 8 * 1024 * 1024) return f; // too big to be worth parsing here
                string text;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(fs))
                    text = reader.ReadToEnd();
                if (text.Trim('\0', ' ', '\t', '\r', '\n', '\uFEFF').Length == 0) { f.Empty = true; return f; }   // font-ok: a byte order mark, trimmed away
                // The whole document, as the game reads it: a file cut short fails here too (a bare reader stops quietly at the end).
                JToken.Parse(text);
            }
            catch (JsonException e) { f.Error = e.Message; }
            catch (Exception e) { f.Error = e.GetType().Name + ": " + e.Message; }
            return f;
        }

        static string GuessOwner(string rel, ModSnapshot mods)
        {
            if (mods == null) return null;
            string stem = rel;
            int slash = stem.IndexOf('/');
            string folder = slash > 0 ? stem.Substring(0, slash) : null;
            stem = System.IO.Path.GetFileNameWithoutExtension(stem);
            string id = mods.ByLooseName(stem) ?? (folder != null ? mods.ByLooseName(folder) : null);
            if (id != null) return ModSnapshot.IsGameMod(id) ? null : id;
            // "mymod-client.json", "MyModConfig.json": the mod whose id the name starts with.
            string loose = ModSnapshot.Loose(stem);
            ModEntry best = null;
            foreach (ModEntry m in mods.UserMods)
            {
                string mid = ModSnapshot.Loose(m.Id);
                if (mid.Length >= 4 && loose.StartsWith(mid, StringComparison.Ordinal) && (best == null || mid.Length > ModSnapshot.Loose(best.Id).Length)) best = m;
            }
            return best?.Id;
        }
    }

    /// <summary>One time the game ran with the doctor in it.</summary>
    public sealed class SessionRecord
    {
        public string Time;
        public string GameVersion;
        public Dictionary<string, string> Mods = new Dictionary<string, string>();
        /// <summary>The game closed the normal way (false: it crashed, was killed, or lost power).</summary>
        public bool CleanExit;
    }

    /// <summary>What the doctor remembers between sessions on one side (ModData/vintagedoctor/state-server.json, state-client.json).</summary>
    public sealed class DoctorState
    {
        public List<SessionRecord> Sessions = new List<SessionRecord>();
        /// <summary>How big the crash log was when the doctor last looked: bigger now means a new crash.</summary>
        public long CrashLogBytes = -1;

        public static DoctorState Load(string file)
        {
            try
            {
                if (File.Exists(file))
                {
                    var s = JsonConvert.DeserializeObject<DoctorState>(File.ReadAllText(file));
                    if (s != null) { s.Sessions ??= new List<SessionRecord>(); return s; }
                }
            }
            catch { }
            return new DoctorState();
        }

        public void Save(string file)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
                string tmp = file + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(this, Formatting.Indented));
                if (File.Exists(file)) File.Delete(file);
                File.Move(tmp, file);
            }
            catch { }
        }
    }

    /// <summary>The mod's own settings (ModConfig/vintagedoctor.json).</summary>
    public class DoctorConfig
    {
        /// <summary>The privilege a player needs to see the server's report and use its fixes.</summary>
        public string ServerReportPrivilege = "controlserver";
        /// <summary>Tell admins in chat when they join if the doctor found a problem or a new crash.</summary>
        public bool TellAdminsOnJoin = true;
        /// <summary>Write one line to the log after start-up saying how many problems the check-up found.</summary>
        public bool ScanAtStartup = true;
        /// <summary>Log lines containing any of these are left out of the doctor's reports.</summary>
        public List<string> IgnoreLogMessages = new List<string>();
        /// <summary>Your own explanations for log lines, in the same form mods use (see docs/api.md).</summary>
        public List<JObject> KnownProblems = new List<JObject>();
        /// <summary>Let fix buttons that mods describe in JSON run a server command (the command is always shown on the button).</summary>
        public bool AllowCommandFixes = true;
        /// <summary>For testing: run every page once after start-up and write the results to ModData/vintagedoctor/selftest.</summary>
        public bool SelfTestOnStart = false;
    }

    /// <summary>
    /// Everything the doctor read from disk for one look: the log, the crash log, the Mods folders and the config
    /// files. Reading happens off the main thread; the checks then run on it with this in hand.
    /// </summary>
    public sealed class Evidence
    {
        public EnumAppSide Side;
        public DateTime TakenAt = DateTime.Now;
        public ModSnapshot Mods;
        public LogScan Log;
        public CrashScan Crash;
        public FolderScan Folder;
        public ConfigScan Configs;
        /// <summary>For the log's errors about formatting tags: the text the game was showing, by error, where the debug or chat log had it.</summary>
        public Dictionary<string, string> TagTexts = new Dictionary<string, string>(StringComparer.Ordinal);
        public long ReadMilliseconds;

        public static string LogFile(EnumAppSide side) => System.IO.Path.Combine(GamePaths.Logs, side == EnumAppSide.Client ? "client-main.log" : "server-main.log");
        public static string CrashFile(EnumAppSide side) => System.IO.Path.Combine(GamePaths.Logs, side == EnumAppSide.Client ? "client-crash.log" : "server-crash.log");

        /// <summary>The folders mods are loaded from: the data folder's Mods, the game's own, and wherever a loaded mod came from.</summary>
        public static List<string> ModFolders(ModSnapshot mods)
        {
            var list = new List<string> { GamePaths.DataPathMods, System.IO.Path.Combine(GamePaths.Binaries, "Mods") };
            if (mods != null)
                foreach (ModEntry m in mods.Mods)
                {
                    if (m.Core || string.IsNullOrEmpty(m.SourcePath)) continue;
                    try
                    {
                        string dir = System.IO.Path.GetDirectoryName(m.SourcePath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
                        if (!string.IsNullOrEmpty(dir) && !list.Contains(dir)) list.Add(dir);
                    }
                    catch { }
                }
            return list;
        }

        /// <summary>
        /// Reads it all. Safe to call from a worker thread; never throws. <paramref name="zipBudgetMs"/> is how long
        /// reading the mods' zips to their ends may take here (no limit when negative): on the main thread it is kept
        /// short, and what is left is read in the background.
        /// </summary>
        public static Evidence Gather(EnumAppSide side, ModSnapshot mods, KnownProblems known, DoctorConfig config, bool files, int zipBudgetMs = -1)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var ev = new Evidence { Side = side, Mods = mods };
            try { mods?.IndexTypes(); } catch { }
            ev.Log = LogScan.Read(LogFile(side), mods);
            ev.Crash = CrashScan.Read(CrashFile(side), mods);
            if (ev.Log?.TagErrors.Count > 0)
                ev.TagTexts = TextTags.ShownTexts(GamePaths.Logs, side == EnumAppSide.Client ? "client" : "server", ev.Log.TagErrors.Select(t => t.message).Distinct().ToList());
            if (files)
            {
                try { ev.Folder = FolderScan.Read(ModFolders(mods), mods); } catch (Exception e) { ev.Folder = new FolderScan(); ev.Folder.Errors.Add(e.Message); }
                try { ZipHealth.Test(ev.Folder.Files, zipBudgetMs); } catch { }
                ev.Configs = ConfigScan.Read(GamePaths.ModConfig, GamePaths.DataPath, mods, known?.ConfigOwners);
            }
            ev.Apply(known, config);
            ev.ReadMilliseconds = watch.ElapsedMilliseconds;
            return ev;
        }

        /// <summary>Drops the lines the owner asked to ignore and marks the ones the doctor (or a mod) can explain.</summary>
        public void Apply(KnownProblems known, DoctorConfig config)
        {
            if (Log == null) return;
            if (config?.IgnoreLogMessages != null && config.IgnoreLogMessages.Count > 0)
                Log.Issues.RemoveAll(i => config.IgnoreLogMessages.Any(s => !string.IsNullOrWhiteSpace(s) && i.Message.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0));
            if (known == null) return;
            foreach (LogIssue issue in Log.Issues)
            {
                KnownProblem k = known.Match(issue, Side);
                if (k == null) continue;
                issue.Known = k;
                if (issue.Mod == null && !string.IsNullOrEmpty(k.ModId) && k.ModId != DoctorApi.ModId) issue.Mod = k.ModId;
            }
        }
    }

    /// <summary>The log messages the doctor can explain: its own list, what mods ship in JSON, what mods register in code, and the owner's.</summary>
    public sealed class KnownProblems
    {
        readonly List<KnownProblem> list = new List<KnownProblem>();
        readonly Dictionary<string, Regex> regexes = new Dictionary<string, Regex>();
        /// <summary>Config files mods declared as theirs: file name (relative to ModConfig) → mod id.</summary>
        public readonly Dictionary<string, string> ConfigOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Mods that said they don't get on with another: (mod, other mod, level, why).</summary>
        public readonly List<(string mod, string other, DoctorLevel level, string why)> Conflicts = new List<(string, string, DoctorLevel, string)>();
        /// <summary>Mods that say another makes them better: (mod, other mod, why).</summary>
        public readonly List<(string mod, string other, string why)> Recommends = new List<(string, string, string)>();
        /// <summary>Files that could not be read, for the doctor's own page.</summary>
        public readonly List<string> LoadErrors = new List<string>();
        public readonly List<string> Sources = new List<string>();
        readonly object gate = new object();

        public int Count { get { lock (gate) return list.Count; } }
        public List<KnownProblem> All { get { lock (gate) return list.ToList(); } }

        public void Add(KnownProblem p)
        {
            if (p == null || string.IsNullOrWhiteSpace(p.Match) || string.IsNullOrWhiteSpace(p.Title)) return;
            lock (gate)
            {
                if (!string.IsNullOrEmpty(p.Id)) list.RemoveAll(x => x.Id == p.Id);
                list.Add(p);
                if (p.IsRegex)
                {
                    try { regexes[p.Id ?? p.Match] = new Regex(p.Match, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)); }
                    catch (Exception e) { LoadErrors.Add((p.Id ?? p.Match) + ": " + e.Message); list.Remove(p); }
                }
            }
        }

        public void RemoveMod(string modId)
        {
            lock (gate) list.RemoveAll(x => x.ModId == modId);
        }

        public KnownProblem Match(LogIssue issue, EnumAppSide side)
        {
            lock (gate)
            {
                foreach (KnownProblem p in list)
                {
                    string ps = (p.Side ?? "both").ToLowerInvariant();
                    if (ps == "server" && side != EnumAppSide.Server) continue;
                    if (ps == "client" && side != EnumAppSide.Client) continue;
                    bool hit;
                    if (p.IsRegex)
                    {
                        if (!regexes.TryGetValue(p.Id ?? p.Match, out Regex rx)) continue;
                        try { hit = rx.IsMatch(issue.Message); } catch { hit = false; }
                    }
                    else hit = issue.Message.IndexOf(p.Match, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!hit) continue;
                    if (!string.IsNullOrEmpty(p.MatchStack) && !issue.Stack.Any(s => s.IndexOf(p.MatchStack, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                    return p;
                }
            }
            return null;
        }

        public static DoctorLevel ParseLevel(string s, DoctorLevel fallback = DoctorLevel.Warning)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "ok": case "passed": return DoctorLevel.Ok;
                case "info": return DoctorLevel.Info;
                case "warn": case "warning": return DoctorLevel.Warning;
                case "problem": case "error": case "fail": case "failed": return DoctorLevel.Problem;
                default: return fallback;
            }
        }

        /// <summary>
        /// Reads one vintagedoctor.json (a mod's assets/&lt;modid&gt;/config/vintagedoctor.json, or the owner's list).
        /// <paramref name="domain"/> is the mod it came from. Returns the fixes it describes, for the caller to register.
        /// </summary>
        public List<JsonFix> LoadJson(string domain, string source, JToken root)
        {
            var fixes = new List<JsonFix>();
            try
            {
                if (!(root is JObject o)) { LoadErrors.Add(source + ": not a JSON object"); return fixes; }
                Sources.Add(source);
                int n = 0;
                foreach (JToken t in o.GetValue("knownProblems", StringComparison.OrdinalIgnoreCase) as JArray ?? new JArray())
                {
                    if (!(t is JObject jp)) continue;
                    KnownProblem p = FromJson(domain, jp, n++, fixes, source);
                    if (p != null) Add(p);
                }
                foreach (JToken t in o.GetValue("conflicts", StringComparison.OrdinalIgnoreCase) as JArray ?? new JArray())
                {
                    if (!(t is JObject jc)) continue;
                    string other = Str(jc, "with") ?? Str(jc, "mod");
                    if (!string.IsNullOrWhiteSpace(other)) Conflicts.Add((domain, other.Trim().ToLowerInvariant(), ParseLevel(Str(jc, "level")), Str(jc, "why") ?? ""));
                }
                foreach (JToken t in o.GetValue("recommends", StringComparison.OrdinalIgnoreCase) as JArray ?? new JArray())
                {
                    if (!(t is JObject jr)) continue;
                    string other = Str(jr, "mod");
                    if (!string.IsNullOrWhiteSpace(other)) Recommends.Add((domain, other.Trim().ToLowerInvariant(), Str(jr, "why") ?? ""));
                }
                foreach (JToken t in o.GetValue("configFiles", StringComparison.OrdinalIgnoreCase) as JArray ?? new JArray())
                {
                    if (t.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)t)) ConfigOwners[((string)t).Replace('\\', '/')] = domain;
                }
            }
            catch (Exception e) { LoadErrors.Add(source + ": " + e.Message); }
            return fixes;
        }

        static string Str(JObject o, string key)
        {
            JToken t = o.GetValue(key, StringComparison.OrdinalIgnoreCase);
            return t == null || t.Type == JTokenType.Null ? null : t.Type == JTokenType.String ? (string)t : t.ToString();
        }

        KnownProblem FromJson(string domain, JObject o, int index, List<JsonFix> fixes, string source)
        {
            var p = new KnownProblem
            {
                Id = domain + ":" + (Str(o, "id") ?? "json-" + index),
                ModId = Str(o, "mod") ?? domain,
                Match = Str(o, "match"),
                IsRegex = string.Equals(Str(o, "regex"), "true", StringComparison.OrdinalIgnoreCase),
                MatchStack = Str(o, "matchStack"),
                Side = Str(o, "side") ?? "both",
                Level = ParseLevel(Str(o, "level")),
                Title = Str(o, "title"),
                Explain = Str(o, "explain"),
                Advice = Str(o, "advice")
            };
            if (string.IsNullOrWhiteSpace(p.Match) || string.IsNullOrWhiteSpace(p.Title)) { LoadErrors.Add(source + ": a known problem needs \"match\" and \"title\""); return null; }
            if (o.GetValue("fix", StringComparison.OrdinalIgnoreCase) is JObject jf)
            {
                var fix = new JsonFix
                {
                    Id = p.Id + "/fix",
                    ModId = domain,
                    Type = (Str(jf, "type") ?? "").Trim().ToLowerInvariant(),
                    File = Str(jf, "file"),
                    Command = Str(jf, "command"),
                    Url = Str(jf, "url"),
                    Label = Str(jf, "label")
                };
                if (fix.Type == "resetconfig" || fix.Type == "command" || fix.Type == "url")
                {
                    fixes.Add(fix);
                    p.FixId = fix.Id;
                    p.FixLabel = fix.Label;
                    p.FixArgument = fix.Type == "url" ? fix.Url : "";
                }
                else LoadErrors.Add(source + ": unknown fix type \"" + fix.Type + "\" (resetConfig, command or url)");
            }
            return p;
        }
    }

    /// <summary>A fix a mod described in JSON: reset one of its config files, run a command, or open a web page.</summary>
    public sealed class JsonFix
    {
        public string Id, ModId, Type, File, Command, Url, Label;
    }
}
