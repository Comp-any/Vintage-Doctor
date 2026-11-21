using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
        public const string Overview = "overview", Deep = "deep", BugReportTopic = "bugreport", Api = "api";

        /// <summary>Every page the doctor has on its own (mods add theirs through the API).</summary>
        public static readonly string[] Topics =
        {
            Overview, Deep, Checks.TopicCrash, Checks.TopicStartup, Checks.TopicLog, Checks.TopicMods, Checks.TopicMod, Checks.TopicPerf, Checks.TopicFiles,
            Checks.TopicPatches, Checks.TopicClashes, Checks.TopicChanges, Checks.TopicWorld, Checks.TopicItem, Checks.TopicBlock, Checks.TopicCraft, Checks.TopicHotkeys, BugReportTopic, Api
        };

        public static string Canonical(string topic)
        {
            topic = (topic ?? "").Trim().ToLowerInvariant();
            switch (topic)
            {
                case "": case "checkup": case "check": case "home": return Overview;
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
                case "deeper": case "full": return Deep;
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
                case Overview: if (server) ServerOverview(x); else ClientOverview(x); break;
                case Deep: DeepChecks(x); break;
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
            if (!x.SameProcess)
            {
                Checks.Guarded(x, "patches", () => Checks.CodePatches(x, false));
                Checks.Guarded(x, "changes", () => Checks.Changes(x, false));
            }
            Checks.Guarded(x, "modchecks", () => Checks.ModChecks(x, false));
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
            if (!x.Doctor.ServerAnswers) Checks.Guarded(x, "conflicts", () => Checks.Conflicts(x));
            Checks.Guarded(x, "patches", () => Checks.CodePatches(x, false));
            Checks.Guarded(x, "changes", () => Checks.Changes(x, false));
            Checks.Guarded(x, "modchecks", () => Checks.ModChecks(x, false));
        }

        /// <summary>The checks that take longer: every zip read to the end, JSON patches compared, and mods' own deeper checks.</summary>
        static void DeepChecks(DoctorContext x)
        {
            bool server = x.Side == EnumAppSide.Server;
            x.R.Section(server ? x.T("sec-deep-server") : x.T("sec-deep-client"));
            if (!(server && x.SameProcess))
            {
                Checks.Guarded(x, "folder", () => Checks.Folder(x, true, deep: true));
                Checks.Guarded(x, "configs", () => Checks.Configs(x, true));
                Checks.Guarded(x, "patches", () => Checks.CodePatches(x, true));
            }
            Checks.Guarded(x, "clashes", () => Checks.Clashes(x, true));
            if (server)
            {
                Checks.Guarded(x, "jsonpatches", () => Checks.JsonPatchOverlaps(x));
                Checks.Guarded(x, "world", () => Checks.WorldChecks(x, true));
            }
            if (server || !x.Doctor.ServerAnswers) Checks.Guarded(x, "names", () => Checks.MissingNames(x));
            Checks.Guarded(x, "modchecks", () => Checks.ModChecks(x, true));
        }

        // ---------------------------------------------------------------- questions

        /// <summary>
        /// What the player can look into next, as buttons. <paramref name="serverTopics"/> are pages other mods added
        /// on the server ("id|question"); the player's own game adds its own from its registry.
        /// </summary>
        public static void Questions(DoctorContext x, IEnumerable<string> serverTopics, bool hasServer, bool hasClient)
        {
            x.R.Section(x.T("sec-questions"));
            x.Line(x.T("questions-help"), DoctorLineStyle.Note);
            x.Line(DoctorReport.Open(Checks.TopicCrash, "", x.T("q-crash")));
            x.Line(DoctorReport.Open(Checks.TopicPerf, "", x.T("q-perf")));
            x.Line(DoctorReport.Open(Checks.TopicMods, "", x.T("q-mods")));
            x.Line(DoctorReport.Open(Checks.TopicChanges, "", x.T("q-changes")));
            x.Line(DoctorReport.Open(Checks.TopicItem, "hand", x.HeldItem?.Collectible != null ? x.T("q-item-held", DoctorReport.NameOf(x.HeldItem.Collectible)) : x.T("q-item")));
            if (hasServer) x.Line(DoctorReport.Open(Checks.TopicBlock, "", x.T("q-block")));
            x.Line(DoctorReport.Open(Checks.TopicCraft, "hand", x.T("q-craft")));
            if (hasClient) x.Line(DoctorReport.Open(Checks.TopicHotkeys, "", x.T("q-hotkeys")));
            x.Line(DoctorReport.Open(Checks.TopicStartup, "", x.T("q-startup")));
            x.Line(DoctorReport.Open(Checks.TopicLog, "", x.T("q-log")));
            x.Line(DoctorReport.Open(Checks.TopicFiles, "", x.T("q-files")));
            x.Line(DoctorReport.Open(Checks.TopicClashes, "", x.T("q-clashes")));
            x.Line(DoctorReport.Open(Checks.TopicPatches, "", x.T("q-patches")));
            if (hasServer) x.Line(DoctorReport.Open(Checks.TopicWorld, "", x.T("q-world")));
            foreach (DoctorTopic t in x.Doctor.AllTopics()) x.Line(DoctorReport.Open(t.Id, "", x.Words(t.Question ?? t.Id)));
            foreach (string s in serverTopics ?? Enumerable.Empty<string>())
            {
                int bar = s.IndexOf('|');
                if (bar <= 0 || x.Doctor.TopicFor(s.Substring(0, bar)) != null) continue;
                x.Line(DoctorReport.Open(s.Substring(0, bar), "", s.Substring(bar + 1)));
            }
            x.Line(DoctorReport.Open(Deep, "", x.T("q-deep")));
            x.Line(DoctorReport.Open(BugReportTopic, "", x.T("q-bugreport")));
            x.Line(DoctorReport.Open(Api, "", x.T("q-api")));
            x.Line(hasClient ? x.T("questions-commands") : x.T("questions-console"), DoctorLineStyle.Note);
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
            foreach (var g in checks.GroupBy(c => c.ModId ?? "?")) x.Detail(x.ModName(g.Key) + ": " + string.Join(", ", g.Select(c => c.Id + (c.Deep ? " (deep)" : ""))));
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
