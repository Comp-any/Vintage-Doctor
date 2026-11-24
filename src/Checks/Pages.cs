using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using VintageDoctor.API;

namespace VintageDoctor
{
    /// <summary>
    /// The doctor's pages. Each side of the game builds its own half of a page (the player's game reads its own log,
    /// crash reports and settings; the server reads its own and the world), and the window shows both, one under the
    /// other. In single player, what both would see alike (the Mods folder, config files, code patches, memory) is
    /// left to the game's half.
    /// </summary>
    public static class Pages
    {
        public const string Overview = "overview", BugReportTopic = "bugreport", Api = "api";

        /// <summary>Every page the doctor has on its own (mods add theirs through the API).</summary>
        public static readonly string[] Topics =
        {
            Overview, Checks.TopicCrash, Checks.TopicStartup, Checks.TopicLog, Checks.TopicMods, Checks.TopicMod, Checks.TopicPerf, Checks.TopicFiles,
            Checks.TopicPatches, Checks.TopicClashes, Checks.TopicChanges, Checks.TopicWorld, Checks.TopicItem, Checks.TopicBlock, Checks.TopicCraft, Checks.TopicHotkeys, BugReportTopic, Api
        };

        public static string Canonical(string topic)
        {
            topic = (topic ?? "").Trim().ToLowerInvariant();
            switch (topic)
            {
                // "deep" was a second, slower round of checks up to 1.2.0: the check-up runs everything now.
                case "": case "checkup": case "check": case "home": case "deep": case "deeper": case "full": return Overview;
                case "crashes": case "crashed": return Checks.TopicCrash;
                case "start": case "starting": return Checks.TopicStartup;
                case "logs": case "errors": return Checks.TopicLog;
                case "lag": case "performance": case "slow": case "fps": case "tps": return Checks.TopicPerf;
                case "folder": case "modsfolder": case "config": case "configs": return Checks.TopicFiles;
                case "harmony": case "code": return Checks.TopicPatches;
                case "clash": case "conflict": case "conflicts": case "compat": case "compatibility": return Checks.TopicClashes;
                case "changed": case "whatchanged": return Checks.TopicChanges;
                case "hand": case "held": return Checks.TopicItem;
                case "recipe": case "crafting": return Checks.TopicCraft;
                case "keys": case "hotkey": return Checks.TopicHotkeys;
                case "report": case "bug": return BugReportTopic;
                default: return topic;
            }
        }

        /// <summary>Whether this side has something to say on a topic.</summary>
        static bool ServerOnly(string topic) => topic == Checks.TopicWorld || topic == Checks.TopicBlock;
        static bool ClientOnly(string topic) => topic == Checks.TopicHotkeys;

        /// <summary>Builds this side's half of a page into <paramref name="x"/>'s report.</summary>
        public static void Build(DoctorContext x)
        {
            string topic = Canonical(x.Topic);
            string arg = x.Argument ?? "";
            bool server = x.Side == EnumAppSide.Server;
            // A mod id or an item code given straight: "/doctor carryon", "/doctor game:ingot-copper".
            if (!Topics.Contains(topic) && x.Doctor.TopicFor(topic) == null)
            {
                // A mod's page that only the other side of the game has: nothing to add from here.
                if (x.OtherSideHasTopic)
                {
                    x.Topic = topic;
                    x.R.Key = topic + "|" + arg;
                    x.R.Title = x.T("title");
                    return;
                }
                if (x.Ev.Mods.Get(topic) != null || x.Ev.Mods.Get(x.Ev.Mods.ByLooseName(topic)) != null) { arg = topic; topic = Checks.TopicMod; }
                else if (topic.IndexOf(':') > 0) { arg = x.Topic.Trim(); topic = Checks.TopicItem; }
                else
                {
                    x.Line(x.T("unknown-topic", x.Topic), DoctorLineStyle.Warn);
                    topic = Overview;
                }
            }
            x.Topic = topic;
            x.Argument = arg;
            x.R.Key = topic + "|" + arg;
            x.R.Title = x.T("title");

            switch (topic)
            {
                case Overview:
                    // Every check, each with its result, then filed under the mods the results are about.
                    x.ListAll = true;
                    if (server) ServerOverview(x); else ClientOverview(x);
                    LogButtons(x);
                    BySection(x);
                    break;
                case Checks.TopicCrash: Checks.Guarded(x, "crash", () => Checks.Crash(x, true)); if (!(server && x.SameProcess)) Checks.Guarded(x, "shutdown", () => Checks.Shutdown(x)); break;
                case Checks.TopicStartup: Checks.Guarded(x, "startup", () => Checks.Startup(x, true)); break;
                case Checks.TopicLog: Checks.Guarded(x, "log", () => Checks.Log(x, true, arg.Length > 0 && arg != "all" ? arg : null)); break;
                case Checks.TopicMods: Checks.Guarded(x, "mods", () => Checks.ModsPage(x)); break;
                case Checks.TopicMod:
                    if (arg.Length == 0) Checks.Guarded(x, "mods", () => Checks.ModsPage(x));
                    else Checks.Guarded(x, "mod", () => Checks.ModPage(x, arg));
                    break;
                case Checks.TopicPerf:
                    if (server) Checks.Guarded(x, "performance", () => Checks.ServerPerformance(x, true));
                    else Checks.Guarded(x, "performance", () => Checks.ClientPerformance(x, true));
                    break;
                case Checks.TopicFiles:
                    if (server && x.SameProcess) break;
                    Checks.Guarded(x, "folder", () => Checks.Folder(x, true));
                    Checks.Guarded(x, "configs", () => Checks.Configs(x, true));
                    break;
                case Checks.TopicPatches:
                    if (!(server && x.SameProcess)) Checks.Guarded(x, "patches", () => Checks.CodePatches(x, true));
                    if (server) Checks.Guarded(x, "jsonpatches", () => Checks.JsonPatchOverlaps(x));
                    break;
                case Checks.TopicClashes:
                    Checks.Guarded(x, "clashes", () => Checks.Clashes(x, true));
                    if (server || !x.Doctor.ServerAnswers) Checks.Guarded(x, "conflicts", () => Checks.Conflicts(x));
                    if (!(server && x.SameProcess)) Checks.Guarded(x, "patches", () => Checks.CodePatches(x, false));
                    if (server) Checks.Guarded(x, "jsonpatches", () => Checks.JsonPatchOverlaps(x));
                    break;
                case Checks.TopicChanges: if (!(server && x.SameProcess)) Checks.Guarded(x, "changes", () => Checks.Changes(x, true)); break;
                case Checks.TopicWorld: if (server) Checks.Guarded(x, "world", () => Checks.WorldChecks(x, true)); break;
                case Checks.TopicItem: if (server || !x.Doctor.ServerAnswers) Checks.Guarded(x, "item", () => Checks.ItemPage(x, arg)); break;
                case Checks.TopicCraft: if (server || !x.Doctor.ServerAnswers) Checks.Guarded(x, "craft", () => Checks.CraftPage(x, arg)); break;
                case Checks.TopicBlock: if (server) Checks.Guarded(x, "block", () => Checks.BlockPage(x)); break;
                case Checks.TopicHotkeys: if (!server) Checks.Guarded(x, "hotkeys", () => Checks.Hotkeys(x, true)); break;
                case BugReportTopic: Checks.Guarded(x, "bugreport", () => BugReport(x, arg)); break;
                case Api: Checks.Guarded(x, "api", () => ApiPage(x)); break;
                default:
                    {
                        DoctorTopic t = x.Doctor.TopicFor(topic);
                        if (t == null) break;
                        if (!t.OwnHeadings) x.Section(x.Words(t.Question ?? t.Id));
                        x.Owner = t.ModId;
                        try { t.Run?.Invoke(x); }
                        catch (Exception e) { x.Owner = null; x.Warn(x.T("modcheck-failed", x.ModName(t.ModId), t.Id, e.GetType().Name + ": " + e.Message)); }
                        x.Owner = null;
                        break;
                    }
            }
            if (topic != Overview) LogButtons(x);
        }

        // ---------------------------------------------------------------- a button for every log that is spoken of

        // log-texts-begin
        // Which of the doctor's own texts speak of a log, and of which: 'm' this side's log (client-main.log or
        // server-main.log), 'c' its crash reports, 'f' the Logs folder as a whole, '-' the word is there but there is
        // nothing to open (a command, a button's own words, a status line). A card or line holding such a text gets a
        // button that opens the file or the folder (LogButtons). tools/check_lang.py reads this table and fails on a
        // text that says "log" or "crash report" and isn't in it, so a new one can't be forgotten. A "sec-" text is
        // a heading and only looked for in headings.
        static readonly Dictionary<string, char> LogTexts = new Dictionary<string, char>(StringComparer.Ordinal)
        {
            { "sec-log", 'm' }, { "sec-log-mod", 'm' }, { "sec-startup", 'm' }, { "sec-crash", 'c' },
            { "crash-none", 'f' }, { "crash-none-why", 'f' }, { "crash-unreadable", 'c' }, { "crash-older", 'c' },
            { "unclean-title", 'f' }, { "unclean-why", 'f' }, { "log-nofile", 'f' },
            { "start-clean-why", 'm' }, { "start-compile-why", 'm' },
            { "log-clean", 'm' }, { "log-clean-warnings", 'm' }, { "log-clean-mod", 'm' }, { "log-errors", 'm' }, { "log-errors-why", 'm' },
            { "log-errors-mod", 'm' }, { "log-errors-nomod", 'm' }, { "log-truncated", 'm' }, { "log-summary", 'm' },
            { "mods-crashes", 'c' }, { "mods-errors", 'm' }, { "mod-crashes", 'c' },
            { "world-disk-why", 'f' }, { "world-disk-advice", 'f' },
            { "item-inlog", 'm' }, { "item-notinlog", 'm' },
            { "targets-optional-why", 'm' }, { "notadded-why", 'm' }, { "guest-timeout", 'm' },
            { "gui-scanning-long", '-' }, { "questions-console", '-' }, { "btn-crash", '-' }, { "btn-crash-all", '-' }, { "btn-open-logs", '-' },
            { "api-help", '-' }, { "api-counts", '-' }, { "guest-fix-working", '-' },
        };
        // log-texts-end

        /// <summary>A log named by its file: "client-main.log", "server-debug.log", "server-main-2.log".</summary>
        static readonly Regex LogFileName = new Regex(@"\b(client|server)-[a-z]+(?:-\d+)?\.log\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        static readonly Dictionary<string, List<(Regex rx, char kind, bool heading)>> logPatterns = new Dictionary<string, List<(Regex, char, bool)>>(StringComparer.Ordinal);

        /// <summary>The texts of <see cref="LogTexts"/> in a language, each as a pattern that finds it in a line whatever values were put into it.</summary>
        static List<(Regex rx, char kind, bool heading)> LogPatterns(DoctorContext x)
        {
            string lang = x.LanguageCode ?? "en";
            lock (logPatterns)
            {
                if (logPatterns.TryGetValue(lang, out var known)) return known;
                var list = new List<(Regex, char, bool)>();
                const string hole = "\u0001";   // font-ok: stands for a value while the pattern is made, never drawn
                object[] holes = Enumerable.Repeat((object)hole, 10).ToArray();
                foreach (var kv in LogTexts)
                {
                    if (kv.Value == '-') continue;
                    // A value at either end adds nothing to a pattern that is looked for anywhere in a line.
                    string text = (x.T(kv.Key, holes) ?? "").Trim(hole[0], ' ');
                    // A text that is nearly all values would be found in every line.
                    if (text.Replace(hole, "").Trim().Length < 6) continue;
                    try { list.Add((new Regex(Regex.Escape(text).Replace(hole, ".*?"), RegexOptions.CultureInvariant | RegexOptions.Singleline), kv.Value, kv.Key.StartsWith("sec-", StringComparison.Ordinal))); }
                    catch { }
                }
                logPatterns[lang] = list;
                return list;
            }
        }

        static bool IsCard(DoctorLine l) => l.Style == DoctorLineStyle.Check && l.Indent == 0;

        /// <summary>Whether a file of the Logs folder is there. (The self-test puts its own answer in.)</summary>
        internal static Func<string, bool> LogFileExists = RealLogFile;
        static bool RealLogFile(string name)
        {
            try { return File.Exists(Path.Combine(GamePaths.Logs, name)); }
            catch { return true; }
        }

        /// <summary>The files that a line's buttons for the Logs folder open ("" for the folder itself).</summary>
        static IEnumerable<(string file, string token)> LogTokens(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf("{{folder|", StringComparison.Ordinal) < 0) yield break;
            foreach (Match m in DoctorReport.ActionToken.Matches(text))
            {
                if (m.Groups[1].Value == "folder" && m.Groups[2].Value == DoctorReport.LogsFolder) yield return (m.Groups[3].Value, m.Value);
            }
        }

        static bool OnlyButtons(string text) => !string.IsNullOrEmpty(text) && text.IndexOf("{{", StringComparison.Ordinal) >= 0 && DoctorReport.ActionToken.Replace(text, "").Trim().Length == 0;

        /// <summary>
        /// Puts buttons at the end of a card (lines <paramref name="start"/> up to <paramref name="end"/>), or under a
        /// line that stands by itself: on the card's last line when that is a line of buttons already, else on a
        /// line of their own. Returns how many lines that added.
        /// </summary>
        static int Attach(List<DoctorLine> lines, int start, int end, bool card, IEnumerable<string> tokens)
        {
            string text = string.Join(" ", tokens.Where(t => !string.IsNullOrEmpty(t)));
            if (text.Length == 0) return 0;
            DoctorLine last = lines[end - 1];
            if (card && end - 1 > start && last.Style == DoctorLineStyle.Text && OnlyButtons(last.Text)) { last.Text += " " + text; return 0; }
            lines.Insert(end, new DoctorLine { Text = text, Style = DoctorLineStyle.Text, Indent = card ? 1 : 0, Section = lines[start].Section });
            return 1;
        }

        /// <summary>
        /// Gives every card and line that speaks of a log a button that opens it: the file where one log is meant,
        /// the Logs folder where it is the logs as a whole (or a file that may not be there). A log is spoken of by
        /// one of the doctor's own texts (<see cref="LogTexts"/>) or by its file's name, whoever wrote the line. A
        /// card that has the button already is left alone, so doing this twice adds nothing. On a server that is
        /// another computer there is nothing the player could open, and nothing is added.
        /// </summary>
        public static void LogButtons(DoctorContext x)
        {
            if (x == null || x.Compact || !x.OnPlayersComputer) return;
            List<DoctorLine> lines = x.R.Lines;
            List<(Regex rx, char kind, bool heading)> patterns = LogPatterns(x);
            string side = x.Side == EnumAppSide.Client ? "client" : "server";
            // In single player both sides' logs are in the one Logs folder.
            bool bothHere = x.SameProcess || (x.Api as Vintagestory.API.Client.ICoreClientAPI)?.IsSinglePlayer == true;

            // Outside the cards one button of a kind is enough for a page (the log's own page names its file twice).
            var loose = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lines.Count;)
            {
                int end = i + 1;
                if (IsCard(lines[i])) { while (end < lines.Count && IsDetail(lines[end])) end++; }
                else foreach (var (file, _) in LogTokens(lines[i].Text)) loose.Add(file);
                i = end;
            }

            for (int i = 0; i < lines.Count;)
            {
                bool card = IsCard(lines[i]);
                int end = i + 1;
                if (card) { while (end < lines.Count && IsDetail(lines[end])) end++; }
                var want = new List<string>();
                var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int k = i; k < end; k++)
                {
                    string text = lines[k].Text ?? "";
                    if (text.IndexOf("{{", StringComparison.Ordinal) >= 0)
                    {
                        foreach (var (file, _) in LogTokens(text)) have.Add(file);
                        // A button's own words don't count: "Every error and warning" leads to a page, not to a file.
                        text = DoctorReport.ActionToken.Replace(text, " ");
                    }
                    if (text.Length < 6) continue;
                    foreach (Match m in LogFileName.Matches(text))
                    {
                        if ((bothHere || m.Groups[1].Value == side) && !want.Contains(m.Value)) want.Add(m.Value);
                    }
                    bool heading = lines[k].Style == DoctorLineStyle.Heading;
                    foreach (var (rx, kind, headingOnly) in patterns)
                    {
                        if (headingOnly != heading || !rx.IsMatch(text)) continue;
                        string file = kind == 'm' ? x.MainLogName : kind == 'c' ? x.CrashLogName : "";
                        if (!want.Contains(file)) want.Add(file);
                    }
                }
                // A file that isn't there (no crash was ever written down) has nothing to show: its folder then.
                for (int w = 0; w < want.Count; w++) { if (want[w].Length > 0 && !LogFileExists(want[w])) want[w] = ""; }
                want = want.Distinct().ToList();
                // The folder says nothing next to a file from it.
                if (want.Count > 1) want.Remove("");
                var add = new List<string>();
                foreach (string file in want)
                {
                    if (file.Length == 0 ? have.Count > 0 : have.Contains(file)) continue;
                    if (!card && !loose.Add(file)) continue;
                    add.Add(x.LogToken(file));
                }
                if (add.Count > 0) end += Attach(lines, i, end, card, add);
                i = end;
            }
        }

        // ---------------------------------------------------------------- the check-up

        static void ServerOverview(DoctorContext x)
        {
            x.R.Section(x.SameProcess ? x.T("sec-server-sp") : x.T("sec-server"));
            Checks.Guarded(x, "crash", () => Checks.Crash(x, false));
            if (!x.SameProcess) Checks.Guarded(x, "shutdown", () => Checks.Shutdown(x));
            Checks.Guarded(x, "startup", () => Checks.Startup(x, false));
            Checks.Guarded(x, "log", () => Checks.Log(x, false));
            if (!x.SameProcess)
            {
                Checks.Guarded(x, "folder", () => Checks.Folder(x, false));
                Checks.Guarded(x, "configs", () => Checks.Configs(x, false));
            }
            Checks.Guarded(x, "performance", () => Checks.ServerPerformance(x, false));
            Checks.Guarded(x, "world", () => Checks.WorldChecks(x, false));
            Checks.Guarded(x, "clashes", () => Checks.Clashes(x, false));
            Checks.Guarded(x, "conflicts", () => Checks.Conflicts(x));
            Checks.Guarded(x, "jsonpatches", () => Checks.JsonPatchOverlaps(x));
            Checks.Guarded(x, "names", () => Checks.MissingNames(x));
            if (!x.SameProcess)
            {
                Checks.Guarded(x, "patches", () => Checks.CodePatches(x, false));
                Checks.Guarded(x, "changes", () => Checks.Changes(x, false));
            }
            Checks.Guarded(x, "modchecks", () => Checks.ModChecks(x));
        }

        static void ClientOverview(DoctorContext x)
        {
            x.R.Section(x.T("sec-client"));
            Checks.Guarded(x, "crash", () => Checks.Crash(x, false));
            Checks.Guarded(x, "shutdown", () => Checks.Shutdown(x));
            Checks.Guarded(x, "startup", () => Checks.Startup(x, false));
            Checks.Guarded(x, "log", () => Checks.Log(x, false));
            Checks.Guarded(x, "folder", () => Checks.Folder(x, false));
            Checks.Guarded(x, "configs", () => Checks.Configs(x, false));
            Checks.Guarded(x, "performance", () => Checks.ClientPerformance(x, false));
            Checks.Guarded(x, "hotkeys", () => Checks.Hotkeys(x, false));
            Checks.Guarded(x, "versus", () => Checks.ModsVersusServer(x, false));
            Checks.Guarded(x, "clashes", () => Checks.Clashes(x, false));
            if (!x.Doctor.ServerAnswers)
            {
                Checks.Guarded(x, "conflicts", () => Checks.Conflicts(x));
                Checks.Guarded(x, "names", () => Checks.MissingNames(x));
            }
            Checks.Guarded(x, "patches", () => Checks.CodePatches(x, false));
            Checks.Guarded(x, "changes", () => Checks.Changes(x, false));
            Checks.Guarded(x, "modchecks", () => Checks.ModChecks(x));
        }

        // ---------------------------------------------------------------- the check-up, by mod

        /// <summary>A card about more mods than this is about the game as a whole (a list of every mod that changed, say).</summary>
        const int MostModsOnACard = 6;

        /// <summary>A line that belongs to the card above it: indented, or a note.</summary>
        static bool IsDetail(DoctorLine l) => l.Style != DoctorLineStyle.Heading && (l.Indent > 0 || l.Style == DoctorLineStyle.Note);

        /// <summary>
        /// Files one side's half of the check-up by mod: every card gets the sections it belongs in
        /// (<see cref="DoctorLine.Section"/>), which are the mods it is about, or the game's when it isn't about one.
        /// The checks' own headings go; the half's first one stays for a game with a Vintage Doctor from before 1.3.0,
        /// which shows the half as it comes.
        /// </summary>
        static void BySection(DoctorContext x)
        {
            // The mods a card's words may name: the loaded ones, and the ones in the Mods folder that the game didn't
            // load (a mod left out for a missing requirement is exactly one to have a section for).
            var mods = new List<(string id, string name, bool loaded)>();
            foreach (ModEntry m in x.Ev?.Mods?.UserMods ?? Enumerable.Empty<ModEntry>()) mods.Add((m.Id, m.Name, true));
            foreach (ModFile f in x.Ev?.Folder?.Files ?? Enumerable.Empty<ModFile>())
            {
                if (string.IsNullOrEmpty(f.ModId) || x.Ev.Mods?.Get(f.ModId) != null || ModSnapshot.IsGameMod(f.ModId)) continue;
                if (!mods.Any(known => string.Equals(known.id, f.ModId, StringComparison.OrdinalIgnoreCase))) mods.Add((f.ModId.ToLowerInvariant(), f.ModName, false));
            }
            List<DoctorLine> lines = x.R.Lines.ToList();
            var kept = new List<DoctorLine>();
            bool sideHeading = false;
            for (int i = 0; i < lines.Count;)
            {
                DoctorLine l = lines[i];
                if (l.Style == DoctorLineStyle.Heading)
                {
                    if (!sideHeading) { l.Section = ""; kept.Add(l); sideHeading = true; }
                    i++;
                    continue;
                }
                bool card = l.Style == DoctorLineStyle.Check && l.Indent == 0;
                int end = i + 1;
                if (card) { while (end < lines.Count && IsDetail(lines[end])) end++; }
                // A line on its own in a failure's or a result's colour is about the page (an unknown page was asked for); the rest is the game's.
                string section = card ? string.Join(",", SectionsOf(x, lines, i, end, mods))
                    : l.Style == DoctorLineStyle.Warn || l.Style == DoctorLineStyle.Total ? "" : DoctorReport.GameSection;
                for (int k = i; k < end; k++) { lines[k].Section = section; kept.Add(lines[k]); }
                i = end;
            }
            x.R.Lines.Clear();
            x.R.Lines.AddRange(kept);
            foreach (string key in kept.SelectMany(l => (l.Section ?? "").Split(',')).Where(k => k.Length > 0).Distinct())
                x.R.SectionTitles[key] = SectionTitle(x, key);
        }

        /// <summary>The mods a card is about: the ones it was tagged with, the ones its buttons lead to, and the ones its words name.</summary>
        static List<string> SectionsOf(DoctorContext x, List<DoctorLine> lines, int from, int to, List<(string id, string name, bool loaded)> mods)
        {
            var found = new List<string>();
            void Add(string id)
            {
                id = (id ?? "").Trim();
                if (id.Length == 0) return;
                string key = x.Ev?.Mods?.Get(id)?.Id ?? id.ToLowerInvariant();
                if (ModSnapshot.IsGameMod(key) || key.IndexOf(',') >= 0 || key.IndexOf('|') >= 0) return;
                if (!found.Contains(key)) found.Add(key);
            }
            // A card that says it is the game's is, whichever mods its words happen to name.
            string[] about = (lines[from].About ?? "").Split(',');
            if (about.Contains(DoctorReport.GameSection)) return new List<string> { DoctorReport.GameSection };
            foreach (string id in about) Add(id);
            var words = new StringBuilder();
            for (int i = from; i < to; i++)
            {
                string text = lines[i].Text ?? "";
                words.Append(text).Append('\n');
                if (text.IndexOf("{{", StringComparison.Ordinal) < 0) continue;
                foreach (Match m in DoctorReport.ActionToken.Matches(text))
                {
                    string kind = m.Groups[1].Value, target = m.Groups[2].Value, arg = m.Groups[3].Value;
                    if (kind == "open" && target == Checks.TopicMod) Add(arg);
                    else if (kind == "open" && target == Checks.TopicLog && arg.Length > 0 && arg != "all") Add(arg);
                    else if (kind == "fix" && target.EndsWith("doctor:disable-mod", StringComparison.Ordinal)) Add(arg);
                }
            }
            string all = words.ToString();
            foreach (var (id, name, loaded) in mods)
            {
                if (!found.Contains(id) && Names(all, id, name, loaded)) Add(id);
            }
            if (found.Count == 0 || found.Count > MostModsOnACard) return new List<string> { DoctorReport.GameSection };
            return found;
        }

        /// <summary>
        /// Whether a text names a mod: its name as whole words, its tag in a log line ("[modid]"), or one of its files
        /// ("modid:path"). A name of three letters or fewer is too easily a word of its own, so only the id counts then.
        /// A mod the game didn't load is spoken of by its id (the game knows nothing else of it), so the bare id
        /// counts for those.
        /// </summary>
        public static bool Names(string text, string id, string name, bool loaded)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (!string.IsNullOrEmpty(name) && name.Length >= 4 && WholeWord(text, name, 0, false)) return true;
            if (string.IsNullOrEmpty(id)) return false;
            if (text.IndexOf("[" + id + "]", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (WholeWord(text, id + ":", 0, true)) return true;
            return !loaded && id.Length >= 4 && WholeWord(text, id, 0, false);
        }

        /// <summary>Finds <paramref name="word"/> where no letter or digit touches it (only its start is looked at when <paramref name="startOnly"/>).</summary>
        static bool WholeWord(string text, string word, int from, bool startOnly)
        {
            for (int at = text.IndexOf(word, from, StringComparison.Ordinal); at >= 0; at = text.IndexOf(word, at + 1, StringComparison.Ordinal))
            {
                bool before = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
                int after = at + word.Length;
                bool behind = startOnly ? after < text.Length && char.IsLetter(text[after]) : after >= text.Length || !char.IsLetterOrDigit(text[after]);
                if (before && behind) return true;
            }
            return false;
        }

        /// <summary>A section's heading: the mod's name and version, or what the Mods folder knows of a mod the game didn't load.</summary>
        static string SectionTitle(DoctorContext x, string key)
        {
            if (key == DoctorReport.GameSection) return x.T("sec-game", GameVersion.ShortGameVersion);
            if (key == DoctorReport.RestSection) return x.T("sec-othermods");
            ModEntry m = x.Ev?.Mods?.Get(key);
            if (m != null) return string.IsNullOrEmpty(m.Version) ? m.Name : m.Name + " " + m.Version;
            // A mod only the server has: this game knows its id and version.
            string onServer = (x.Doctor?.ServerMods ?? new string[0]).FirstOrDefault(s => s != null && s.StartsWith(key + "@", StringComparison.OrdinalIgnoreCase));
            if (onServer != null) return onServer.Replace('@', ' ');
            ModFile f = x.Ev?.Folder?.Files.FirstOrDefault(file => string.Equals(file.ModId, key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(file.ModName));
            return x.T("sec-mod-notloaded", f != null ? f.ModName : key);
        }

        /// <summary>How bad the worst card of a section is: 0 a problem, 1 a warning, 2 something to know, 3 passed, 4 no cards.</summary>
        static int Worst(IEnumerable<DoctorLine> lines)
        {
            int worst = 4;
            foreach (DoctorLine l in lines)
            {
                if (l.Style != DoctorLineStyle.Check || l.Indent != 0) continue;
                string t = l.Text ?? "";
                int rank = t.StartsWith(DoctorReport.ProblemMark, StringComparison.Ordinal) ? 0 : t.StartsWith(DoctorReport.WarnMark, StringComparison.Ordinal) ? 1
                    : t.StartsWith(DoctorReport.OkMark, StringComparison.Ordinal) ? 3 : 2;
                if (rank < worst) worst = rank;
            }
            return worst;
        }

        /// <summary>A page a mod on the other side of the game added: its id, its question, and the mod's id when the other side said.</summary>
        public struct OtherTopic
        {
            public string Id, Question, Mod;
        }

        /// <summary>"id|question" lines and the mods beside them, as the server sends its pages (the mods only since 1.3.0).</summary>
        public static List<OtherTopic> OtherTopics(string[] topics, string[] mods)
        {
            var list = new List<OtherTopic>();
            for (int i = 0; i < (topics?.Length ?? 0); i++)
            {
                string s = topics[i] ?? "";
                int bar = s.IndexOf('|');
                if (bar <= 0) continue;
                list.Add(new OtherTopic { Id = s.Substring(0, bar), Question = s.Substring(bar + 1), Mod = mods != null && i < mods.Length ? mods[i] : null });
            }
            return list;
        }

        /// <summary>
        /// The check-up as it is shown, written into <paramref name="x"/>'s report: a section for the game, then one
        /// for each mod with something against it or something to offer, worst first, then the mods nothing was found
        /// against. A section holds the cards about that mod from each half (this game's, the server's) and the
        /// buttons the mod itself added; the doctor's own buttons are the game's.
        /// </summary>
        /// <param name="halves">Each side's half, as <see cref="Build"/> made it, with the note that goes above its cards in a section (null for none).</param>
        /// <param name="otherTopics">Pages of mods on the other side that this side doesn't have.</param>
        public static void Arrange(DoctorContext x, IList<(DoctorReport half, string note)> halves, IEnumerable<OtherTopic> otherTopics, bool hasServer, bool hasClient)
        {
            DoctorReport r = x.R;
            var order = new List<string>();
            var cards = new Dictionary<string, List<DoctorLine>[]>(StringComparer.Ordinal);
            var titles = new Dictionary<string, string>(StringComparer.Ordinal);
            var asIs = new List<DoctorLine>();
            List<DoctorLine>[] Lists(string key)
            {
                if (!cards.TryGetValue(key, out List<DoctorLine>[] lists))
                {
                    cards[key] = lists = new List<DoctorLine>[halves.Count];
                    order.Add(key);
                }
                return lists;
            }
            Lists(DoctorReport.GameSection);

            for (int h = 0; h < halves.Count; h++)
            {
                DoctorReport half = halves[h].half;
                if (half == null) continue;
                foreach (var kv in half.SectionTitles) { if (!titles.ContainsKey(kv.Key)) titles[kv.Key] = kv.Value; }
                // A half from a Vintage Doctor before 1.3.0 has no sections: it is shown whole, under the rest.
                if (!half.Lines.Any(l => !string.IsNullOrEmpty(l.Section))) { asIs.AddRange(half.Lines.Select(l => l.Copy(null))); continue; }
                foreach (DoctorLine l in half.Lines)
                {
                    if (l.Style == DoctorLineStyle.Heading) continue;
                    if (string.IsNullOrEmpty(l.Section)) { r.Lines.Add(l.Copy(null)); continue; }
                    foreach (string key in l.Section.Split(','))
                    {
                        if (key.Length == 0) continue;
                        List<DoctorLine>[] lists = Lists(key);
                        (lists[h] ?? (lists[h] = new List<DoctorLine>())).Add(l.Copy(key));
                    }
                }
            }

            // The buttons: each mod's pages in its own section, the ones nobody is named for with the game's.
            var buttons = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void Button(string mod, string topic, string question)
            {
                if (!seen.Add(topic)) return;
                string key = string.IsNullOrEmpty(mod) ? DoctorReport.GameSection : x.Ev?.Mods?.Get(mod)?.Id ?? mod.ToLowerInvariant();
                if (ModSnapshot.IsGameMod(key)) key = DoctorReport.GameSection;
                Lists(key);
                if (!buttons.TryGetValue(key, out List<string> list)) buttons[key] = list = new List<string>();
                list.Add(DoctorReport.Open(topic, "", question));
            }
            foreach (DoctorTopic t in x.Doctor.AllTopics()) Button(t.ModId, t.Id, x.Words(t.Question ?? t.Id));
            foreach (OtherTopic t in otherTopics ?? Enumerable.Empty<OtherTopic>()) Button(t.Mod, t.Id, x.Words(t.Question ?? t.Id));

            // The game first, then the mods: the ones with problems, then warnings, then the rest, each lot by name.
            string Title(string key) => titles.TryGetValue(key, out string t) ? t : SectionTitle(x, key);
            int Rank(string key) => Worst(cards[key].Where(l => l != null).SelectMany(l => l));
            var sorted = order.Where(k => k != DoctorReport.GameSection).OrderBy(Rank).ThenBy(Title, StringComparer.OrdinalIgnoreCase).ToList();
            sorted.Insert(0, DoctorReport.GameSection);

            foreach (string key in sorted)
            {
                string title = Title(key);
                r.Lines.Add(new DoctorLine { Text = title, Style = DoctorLineStyle.Heading, Section = key });
                ModEntry mod = key == DoctorReport.GameSection ? null : x.Ev?.Mods?.Get(key);
                var said = new Dictionary<string, DoctorLine>(StringComparer.Ordinal);
                for (int h = 0; h < halves.Count; h++)
                {
                    List<DoctorLine> all = cards[key][h];
                    if (all == null || all.Count == 0) continue;
                    // What both sides found alike (no crash reports, a clean log) is said once.
                    // Each side has its own log, though: the one card keeps the buttons for both.
                    var list = new List<DoctorLine>();
                    bool again = false;
                    DoctorLine first = null;
                    foreach (DoctorLine l in all)
                    {
                        if (IsCard(l))
                        {
                            again = said.TryGetValue(l.Text, out first);
                            if (!again) said[l.Text] = l;
                        }
                        else if (!IsDetail(l)) again = false;
                        if (!again) { list.Add(l); continue; }
                        int at = first == null ? -1 : r.Lines.IndexOf(first);
                        if (at < 0) continue;
                        int end = at + 1;
                        while (end < r.Lines.Count && IsDetail(r.Lines[end])) end++;
                        var there = new HashSet<string>(r.Lines.Skip(at).Take(end - at).SelectMany(o => LogTokens(o.Text)).Select(o => o.file), StringComparer.OrdinalIgnoreCase);
                        Attach(r.Lines, at, end, true, LogTokens(l.Text).Where(o => there.Add(o.file)).Select(o => o.token).ToList());
                    }
                    if (list.Count == 0) continue;
                    // Plain text, not a note: the window takes a note under a card for one of the card's details.
                    if (!string.IsNullOrEmpty(halves[h].note)) r.Lines.Add(new DoctorLine { Text = halves[h].note, Style = DoctorLineStyle.Text, Section = key });
                    foreach (DoctorLine l in list)
                    {
                        // Under the mod's own heading a card needn't start with the mod's name.
                        if (mod != null && l.Style == DoctorLineStyle.Check && l.Indent == 0 && l.Text.Length > 2 && l.Text.IndexOf(mod.Name + ": ", 2, StringComparison.Ordinal) == 2)
                            l.Text = l.Text.Substring(0, 2) + l.Text.Substring(2 + mod.Name.Length + 2);
                        r.Lines.Add(l);
                    }
                }
                if (key == DoctorReport.GameSection) GameButtons(x, key, hasServer, hasClient);
                if (buttons.TryGetValue(key, out List<string> own))
                {
                    foreach (string b in own) r.Lines.Add(new DoctorLine { Text = b, Style = DoctorLineStyle.Text, Section = key });
                }
                if (mod != null && !x.Compact) r.Lines.Add(new DoctorLine { Text = DoctorReport.Open(Checks.TopicMod, mod.Id, x.T("btn-modpage-all", mod.Name)), Style = DoctorLineStyle.Text, Section = key });
            }

            // Everything else that is installed, in one card.
            var rest = (x.Ev?.Mods?.UserMods ?? Enumerable.Empty<ModEntry>()).Where(m => !cards.ContainsKey(m.Id)).Select(m => m.Name).ToList();
            foreach (string other in x.Doctor.ServerMods ?? new string[0])
            {
                string id = (other ?? "").Split('@')[0];
                if (id.Length == 0 || cards.ContainsKey(id) || x.Ev?.Mods?.Get(id) != null || rest.Contains(id)) continue;
                rest.Add(id);
            }
            if (rest.Count > 0)
            {
                r.Lines.Add(new DoctorLine { Text = SectionTitle(x, DoctorReport.RestSection), Style = DoctorLineStyle.Heading, Section = DoctorReport.RestSection });
                r.Lines.Add(new DoctorLine { Text = DoctorReport.OkMark + " " + x.T("mods-fine", rest.Count), Style = DoctorLineStyle.Check, Section = DoctorReport.RestSection });
                r.Lines.Add(new DoctorLine { Text = string.Join(", ", rest.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)), Style = DoctorLineStyle.Note, Indent = 1, Section = DoctorReport.RestSection });
                if (!x.Compact) r.Lines.Add(new DoctorLine { Text = DoctorReport.Open(Checks.TopicMods, "", x.T("btn-mods-all")), Style = DoctorLineStyle.Text, Section = DoctorReport.RestSection });
            }
            r.Lines.AddRange(asIs);
            // A note right under a card would be taken for one of its details: then it is plain text.
            DoctorLine last = r.Lines.Count > 0 ? r.Lines[r.Lines.Count - 1] : null;
            bool underCard = last != null && (IsDetail(last) || (last.Style == DoctorLineStyle.Check && last.Indent == 0));
            r.Lines.Add(new DoctorLine { Text = hasClient ? x.T("questions-commands") : x.T("questions-console"), Style = underCard ? DoctorLineStyle.Text : DoctorLineStyle.Note });
        }

        /// <summary>The doctor's own pages, as buttons in the game's section: what the player may be seeing, and where to look into it.</summary>
        static void GameButtons(DoctorContext x, string key, bool hasServer, bool hasClient)
        {
            void Line(string text, DoctorLineStyle style = DoctorLineStyle.Text) => x.R.Lines.Add(new DoctorLine { Text = text, Style = style, Section = key });
            Line(x.T("questions-help"));   // plain text: a note under the last card would be taken for one of its details
            Line(DoctorReport.Open(Checks.TopicCrash, "", x.T("q-crash")));
            Line(DoctorReport.Open(Checks.TopicPerf, "", x.T("q-perf")));
            Line(DoctorReport.Open(Checks.TopicMods, "", x.T("q-mods")));
            Line(DoctorReport.Open(Checks.TopicChanges, "", x.T("q-changes")));
            Line(DoctorReport.Open(Checks.TopicItem, "hand", x.HeldItem?.Collectible != null ? x.T("q-item-held", DoctorReport.NameOf(x.HeldItem.Collectible)) : x.T("q-item")));
            if (hasServer) Line(DoctorReport.Open(Checks.TopicBlock, "", x.T("q-block")));
            Line(DoctorReport.Open(Checks.TopicCraft, "hand", x.T("q-craft")));
            if (hasClient) Line(DoctorReport.Open(Checks.TopicHotkeys, "", x.T("q-hotkeys")));
            Line(DoctorReport.Open(Checks.TopicStartup, "", x.T("q-startup")));
            Line(DoctorReport.Open(Checks.TopicLog, "", x.T("q-log")));
            Line(DoctorReport.Open(Checks.TopicFiles, "", x.T("q-files")));
            Line(DoctorReport.Open(Checks.TopicClashes, "", x.T("q-clashes")));
            Line(DoctorReport.Open(Checks.TopicPatches, "", x.T("q-patches")));
            if (hasServer) Line(DoctorReport.Open(Checks.TopicWorld, "", x.T("q-world")));
            Line(DoctorReport.Open(BugReportTopic, "", x.T("q-bugreport")));
            Line(DoctorReport.Open(Api, "", x.T("q-api")));
        }

        /// <summary>Under any page but the check-up: the way back.</summary>
        public static void Back(DoctorContext x)
        {
            x.R.Section(x.T("sec-next"));
            x.Line(DoctorReport.Open(Overview, "", x.T("btn-home")) + " " + DoctorReport.Open(BugReportTopic, "", x.T("btn-bugreport")));
        }

        // ---------------------------------------------------------------- for mod authors

        static void ApiPage(DoctorContext x)
        {
            x.Section(x.T("sec-api"));
            x.Line(x.T("api-help"), DoctorLineStyle.Note);
            var checks = x.Doctor.ChecksFor(null).ToList();
            var topics = x.Doctor.AllTopics().ToList();
            var fixes = x.Doctor.AllFixes().Where(f => f.ModId != DoctorApi.ModId).ToList();
            KnownProblems known = x.Doctor.Known;
            x.Info(x.T("api-counts", checks.Count, fixes.Count, topics.Count, known.Count));
            foreach (var g in checks.GroupBy(c => c.ModId ?? "?")) x.Detail(x.ModName(g.Key) + ": " + string.Join(", ", g.Select(c => c.Id)));
            foreach (DoctorFix f in fixes) x.Detail(x.T("api-fix", x.ModName(f.ModId), f.Id), 2);
            foreach (DoctorTopic t in topics) x.Detail(x.T("api-topic", x.ModName(t.ModId), t.Id), 2);
            if (known.Sources.Count > 0)
            {
                x.Info(x.T("api-json", known.Sources.Count));
                foreach (string s in known.Sources) x.Detail(s);
            }
            if (known.LoadErrors.Count > 0)
            {
                x.Warn(x.T("api-json-errors", known.LoadErrors.Count));
                foreach (string e in known.LoadErrors.Take(30)) x.Detail(e);
            }
            if (known.Conflicts.Count > 0) x.Info(x.T("api-conflicts", known.Conflicts.Count));
            x.Line(x.T("api-docs"), DoctorLineStyle.Note);
            if (!x.Compact) x.Line(DoctorReport.Url("https://github.com/Comp-any/Vintage-Doctor/blob/main/docs/api.md", x.T("btn-api-docs")));
        }

        // ---------------------------------------------------------------- a report to post

        /// <summary>
        /// Everything a mod author needs, as text to paste: versions, the mod list, what the check-up found, the errors
        /// and the latest crash, with addresses, ids and user folders taken out. For one mod when <paramref name="modId"/> is given.
        /// </summary>
        public static void BugReport(DoctorContext x, string modId)
        {
            ModEntry only = string.IsNullOrWhiteSpace(modId) ? null : x.Ev.Mods.Get(modId.Trim());
            bool server = x.Side == EnumAppSide.Server;
            x.Section(only == null ? (server ? x.T("sec-bugreport-server") : x.T("sec-bugreport-client")) : x.T("sec-bugreport-mod", only.Name));
            x.Line(x.T("bugreport-help"), DoctorLineStyle.Note);

            var sb = new StringBuilder();
            LogScan log = x.Ev.Log;
            sb.Append("Vintage Doctor ").Append(x.Doctor.DoctorVersion).Append(" on Vintage Story ").Append(GameVersion.LongGameVersion).Append(", ")
              .Append(server ? (x.Doctor.IsDedicated ? "dedicated server" : "single player / hosted") : "client")
              .Append(", ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append('\n');
            if (log != null && log.Found)
            {
                var facts = new List<string>();
                if (log.Os != null) facts.Add(log.Os);
                if (log.Cpu != null) facts.Add(log.Cpu);
                if (log.Ram != null) facts.Add(log.Ram + " RAM");
                if (log.GpuRenderer != null) facts.Add(log.GpuRenderer);
                if (log.Framework != null) facts.Add(log.Framework);
                if (facts.Count > 0) sb.Append(string.Join("; ", facts)).Append('\n');
            }
            if (only != null) sb.Append("About: ").Append(only.Name).Append(' ').Append(only.Version).Append(" (").Append(only.Id).Append(")\n");

            // The mod list, wrapped, so sixty mods take a few lines.
            var mods = x.Ev.Mods.UserMods.Select(m => m.Id + " " + m.Version).ToList();
            sb.Append("\n== Mods (").Append(mods.Count).Append(") ==\n");
            int col = 0;
            foreach (string m in mods)
            {
                if (col > 0 && col + m.Length + 2 > 110) { sb.Append('\n'); col = 0; }
                if (col > 0) { sb.Append(", "); col += 2; }
                sb.Append(m); col += m.Length;
            }
            sb.Append('\n');

            // The findings, facts only.
            var inner = new DoctorContext
            {
                Doctor = x.Doctor, Api = x.Api, Side = x.Side, Player = x.Player, LanguageCode = "en", Topic = only == null ? Overview : Checks.TopicMod,
                Argument = only?.Id ?? "", Compact = true, HeldItem = x.HeldItem, LookedAt = x.LookedAt, SameProcess = x.SameProcess, Ev = x.Ev
            };
            if (only != null) Checks.Guarded(inner, "mod", () => Checks.ModPage(inner, only.Id));
            else if (server) ServerOverview(inner);
            else ClientOverview(inner);
            sb.Append("\n== Findings ==\n");
            foreach (string line in inner.R.PlainText(x.Api.World, labelsOnly: true).Split('\n'))
            {
                string t = line.TrimEnd();
                if (t.Length == 0 || t.StartsWith("== ", StringComparison.Ordinal)) continue;
                sb.Append(t).Append('\n');
            }

            if (only == null && log != null && log.Found)
            {
                var issues = log.Issues.Where(i => i.Known == null || i.Known.Level > DoctorLevel.Info).OrderByDescending(i => i.Bad).ThenByDescending(i => i.Count).Take(25).ToList();
                sb.Append("\n== Errors and warnings in ").Append(Path.GetFileName(log.File)).Append(" (most frequent first) ==\n");
                if (issues.Count == 0) sb.Append("(none)\n");
                foreach (LogIssue i in issues)
                {
                    sb.Append(i.Count > 1 ? "×" + i.Count + " " : "").Append('[').Append(i.Level).Append("] ").Append(LogScan.Shorten(i.Message, 300));
                    if (i.Mod != null) sb.Append("  {").Append(i.Mod).Append('}');
                    sb.Append('\n');
                    foreach (string frame in i.Stack.Take(2)) sb.Append("    ").Append(LogScan.Shorten(frame, 200)).Append('\n');
                }
            }
            CrashEntry crash = x.Ev.Crash?.Latest;
            if (crash != null && (only == null || crash.Culprits.Contains(only.Id)))
            {
                sb.Append("\n== Latest crash (").Append(crash.When ?? "?").Append(") ==\n");
                sb.Append(crash.ExceptionType).Append(": ").Append(crash.Message).Append('\n');
                if (crash.NamedMods.Count > 0) sb.Append("Named by the game: ").Append(string.Join(", ", crash.NamedMods)).Append('\n');
                if (crash.HarmonyIds.Count > 0) sb.Append("Harmony ids: ").Append(string.Join(", ", crash.HarmonyIds)).Append('\n');
                foreach (string frame in crash.Stack.Take(12)) sb.Append("   ").Append(LogScan.Shorten(frame, 220)).Append('\n');
            }

            string text = Anonymise.Line(sb.ToString());
            string file = null;
            try
            {
                file = Path.Combine(x.Doctor.DataFolder, "bug-report-" + (server ? "server" : "client") + (only != null ? "-" + only.Id : "") + ".txt");
                File.WriteAllText(file, text);
            }
            catch (Exception e) { x.Api.Logger.Warning("[vintagedoctor] Could not write the bug report: {0}", e.Message); file = null; }
            if (file != null) { x.Ok(x.T("bugreport-saved", Anonymise.Short(file))); x.R.File = file; }
            x.Line(x.T("bugreport-send"), DoctorLineStyle.Note);
            x.Section(x.T("sec-bugreport-text"));
            foreach (string line in text.Split('\n'))
            {
                if (line.StartsWith("== ", StringComparison.Ordinal)) x.Line(line.Trim('=', ' '), DoctorLineStyle.Total);
                else x.Line(DoctorReport.Safe(line), DoctorLineStyle.Note);
            }
        }
    }
}
