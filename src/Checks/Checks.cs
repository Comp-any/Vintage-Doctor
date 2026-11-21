using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using VintageDoctor.API;

namespace VintageDoctor
{
    /// <summary>
    /// The doctor's own checks. Each takes the page being built and writes cards to it: one line saying what was
    /// found (passed, worth knowing, may affect play, or a problem), with the details, the reason and what to do
    /// underneath, and buttons for the fixes and the fuller pages.
    /// </summary>
    public static partial class Checks
    {
        // ---------------------------------------------------------------- shared

        /// <summary>What an error message usually means, in plain words; null when the doctor has nothing to add.</summary>
        public static string Why(DoctorContext x, string message, IEnumerable<string> stack = null)
        {
            string m = (message ?? "").ToLowerInvariant();
            string key = WhyKey(m);
            if (key == null && stack != null)
            {
                foreach (string s in stack)
                {
                    key = WhyKey((s ?? "").ToLowerInvariant());
                    if (key != null) break;
                }
            }
            return key == null ? null : x.T(key);
        }

        static string WhyKey(string m)
        {
            if (m.Length == 0) return null;
            if (m.Contains("assembly with same name is already loaded") || m.Contains("already loaded and cannot be reloaded")) return "why-sameassembly";
            if (m.Contains("could not load file or assembly")) return "why-missingassembly";
            if (m.Contains("method not found") || m.Contains("missingmethodexception") || m.Contains("field not found") || m.Contains("missingfieldexception")
                || m.Contains("could not load type") || m.Contains("typeloadexception") || m.Contains("does not have an implementation") || m.Contains("missingmemberexception")
                || m.Contains("entry point was not found") || m.Contains("typeinitializationexception")) return "why-version";
            if (m.Contains("found multiple .dll files with modsystems") || m.Contains("there are no .dll files that contain at least one modsystem")
                || m.Contains("found both .cs and .dll files")) return "why-packaging";
            if (m.Contains("dllnotfoundexception") || m.Contains("unable to load shared library") || m.Contains("unable to load dll")) return "why-native";
            if (m.Contains("badimageformatexception") || m.Contains("bad il format") || m.Contains("is not a valid") && m.Contains("assembly")) return "why-badimage";
            if (m.Contains("harmonyexception") || m.Contains("patching exception") || m.Contains("il compile error") || m.Contains("invalidprogramexception")
                || m.Contains("detected an invalid program")) return "why-harmony";
            if (m.Contains("already registered") || m.Contains("already been registered") || m.Contains("has already been added")
                || m.Contains("duplicate key") || m.Contains("with such code already exists") || m.Contains("must use a unique code")) return "why-duplicate";
            if (m.Contains("index (zero based) must be greater than or equal to zero and less than the size of the argument list")) return "why-format";
            if (m.Contains("jsonreaderexception") || m.Contains("jsonserializationexception") || m.Contains("unexpected character encountered while parsing")
                || m.Contains("error converting value") || m.Contains("unterminated string") || m.Contains("after parsing a value an unexpected character")
                || m.Contains("invalid property identifier") || m.Contains("unexpected end when") || m.Contains("error reading jobject")) return "why-json";
            if (m.Contains("outofmemory") || m.Contains("insufficient memory") || m.Contains("out of memory")) return "why-memory";
            if (m.Contains("no space left on device") || m.Contains("not enough space on the disk")) return "why-diskfull";
            if (m.Contains("database disk image is malformed") || m.Contains("file is not a database") || (m.Contains("sqlite") && m.Contains("corrupt"))) return "why-savecorrupt";
            if (m.Contains("being used by another process") || m.Contains("unauthorizedaccessexception") || m.Contains("access to the path")) return "why-fileaccess";
            if (m.Contains("shader") && (m.Contains("compile") || m.Contains("link"))) return "why-graphics";
            if (m.Contains("opengl") || m.Contains("glfw") || m.Contains("framebuffer")) return "why-graphics";
            if (m.Contains("socketexception") || m.Contains("connection refused") || m.Contains("connection timed out") || m.Contains("actively refused")) return "why-network";
            if (m.Contains("object reference not set") || m.Contains("nullreferenceexception")) return "why-null";
            if (m.Contains("keynotfoundexception") || m.Contains("the given key") || m.Contains("indexoutofrange") || m.Contains("index was out of range")
                || m.Contains("index was outside the bounds") || m.Contains("argumentoutofrange") || m.Contains("invalidcastexception") || m.Contains("unable to cast")
                || m.Contains("collection was modified") || m.Contains("value cannot be null") || m.Contains("argumentnullexception") || m.Contains("stackoverflow")
                || m.Contains("sequence contains no") || m.Contains("invalidoperationexception") || m.Contains("dividebyzero")) return "why-bug";
            return null;
        }

        /// <summary>A link to find a mod on the Mod DB by its id or name.</summary>
        public static string ModDbSearch(string idOrName) => "https://mods.vintagestory.at/list/mod?text=" + Uri.EscapeDataString(idOrName ?? "");

        static string Time(DateTime t) => t.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);

        static string Size(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024 / 1024).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " GB";
            if (bytes >= 1024 * 1024) return (bytes / 1024.0 / 1024).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " MB";
            return Math.Max(1, bytes / 1024) + " KB";
        }

        static string N(double v, string format = "0.#") => v.ToString(format, System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Buttons every finding about one mod gets: its page, and switching it off.</summary>
        static void ModButtons(DoctorContext x, string modId, bool offerDisable)
        {
            if (x.Compact || string.IsNullOrEmpty(modId)) return;
            ModEntry m = x.Ev?.Mods?.Get(modId);
            var tokens = new List<string> { DoctorReport.Open("mod", modId, x.T("btn-modpage", x.ModName(modId))) };
            if (m != null && offerDisable && !m.Core && m.Id != DoctorApi.ModId) tokens.Add(x.PlayerFixToken("doctor:disable-mod", m.Id, x.T("btn-disable", m.Name)));
            x.Buttons(tokens.ToArray());
        }

        /// <summary>
        /// Whether a check says so when it found nothing: on its own page (<paramref name="full"/>) and on the
        /// check-up, which lists every check with its result. A mod's page only says what it found.
        /// </summary>
        static bool Listed(DoctorContext x, bool full) => full || x.ListAll;

        /// <summary>Runs one of the doctor's own checks; if it throws, the page says so and carries on.</summary>
        public static void Guarded(DoctorContext x, string name, Action check)
        {
            try { check(); }
            catch (Exception e)
            {
                x.Owner = null;
                x.Warn(x.T("check-crashed", name, e.GetType().Name + ": " + e.Message));
                try { x.Api.Logger.Warning("[vintagedoctor] The check '{0}' failed: {1}", name, e); } catch { }
            }
        }

        // ---------------------------------------------------------------- crashes

        public const string TopicCrash = "crash";

        /// <summary>The game's crash log: when it last crashed, on what, and which mod the crash points at.</summary>
        public static void Crash(DoctorContext x, bool full)
        {
            CrashScan scan = x.Ev.Crash;
            if (full) x.Section(x.T("sec-crash"));
            if (scan == null || scan.Error != null) { x.Info(x.T("crash-unreadable", scan?.Error ?? "?")); return; }
            if (!scan.Found || scan.Entries.Count == 0)
            {
                x.Ok(x.T("crash-none"));
                if (full) x.Explain(x.T("crash-none-why", System.IO.Path.GetFileName(scan.File)));
                return;
            }
            List<CrashEntry> entries = scan.Entries.AsEnumerable().Reverse().Take(full ? 12 : 1).ToList();
            bool first = true;
            foreach (CrashEntry c in entries)
            {
                CrashCard(x, scan, c, first, full);
                first = false;
            }
            if (!full && scan.Entries.Count > 1) x.LinkButton(TopicCrash, "", x.T("btn-crash-all", scan.Entries.Count));
            if (full && scan.Entries.Count > entries.Count) x.Line(x.T("crash-older", scan.Entries.Count - entries.Count));
        }

        static void CrashCard(DoctorContext x, CrashScan scan, CrashEntry c, bool latest, bool full)
        {
            DateTime when = c.Time ?? (latest ? scan.LastWrite : DateTime.MinValue);
            double days = when == DateTime.MinValue ? 999 : (DateTime.Now - when).TotalDays;
            bool isNew = latest && x.Doctor.NewCrashThisSession;
            var culprits = c.Culprits.Where(id => !ModSnapshot.IsGameMod(id)).Distinct().ToList();
            string what = c.ExceptionType.Length > 0 ? ShortType(c.ExceptionType) : LogScan.Shorten(c.Message, 80);
            string title = culprits.Count > 0
                ? x.T("crash-title-mod", c.When ?? "?", what, string.Join(", ", culprits.Select(x.ModName)))
                : x.T("crash-title", c.When ?? "?", what);
            if (isNew) x.Problem(title);
            else if (latest && days <= 7) x.Warn(title);
            else x.Info(title);

            if (c.Message.Length > 0) x.Detail(LogScan.Shorten(c.ExceptionType + (c.ExceptionType.Length > 0 ? ": " : "") + c.Message, 400));
            string why = Why(x, c.ExceptionType + " " + c.Message, c.Stack);
            if (why != null) x.Explain(why);
            if (c.GameVersion != null && !string.Equals(c.GameVersion, GameVersion.LongGameVersion, StringComparison.Ordinal)) x.Detail(x.T("crash-othergame", c.GameVersion, GameVersion.LongGameVersion));

            foreach (string id in culprits)
            {
                string then = c.VersionAtCrash(id);
                ModEntry now = x.Ev.Mods?.Get(id);
                if (now == null) x.Detail(x.T("crash-mod-gone", id, then ?? "?"));
                else if (then != null && then != now.Version) x.Detail(x.T("crash-mod-updated", now.Name, then, now.Version));
                else x.Detail(x.T("crash-mod-same", now.Name, now.Version));
            }
            if (culprits.Count == 0) x.Explain(x.T("crash-nomod"));
            if (c.HarmonyIds.Count > 0) x.Detail(x.T("crash-harmony", string.Join(", ", c.HarmonyIds.Take(8))));
            int frames = full || latest ? 6 : 3;
            foreach (string frame in c.Stack.Take(frames)) x.Detail(LogScan.Shorten(Anonymise.Line(frame), 220), 2);
            if (c.Stack.Count > frames) x.Detail(x.T("more-lines", c.Stack.Count - frames), 2);

            if (culprits.Count > 0)
            {
                bool anyUpdated = culprits.Any(id => { ModEntry now = x.Ev.Mods?.Get(id); string then = c.VersionAtCrash(id); return now != null && then != null && then != now.Version; });
                x.Advice(anyUpdated ? x.T("crash-advice-updated") : x.T("crash-advice-mod"));
                foreach (string id in culprits.Take(3)) ModButtons(x, id, x.Ev.Mods?.Get(id) != null);
            }
            else x.Advice(x.T("crash-advice-nomod"));
            if (latest) x.Buttons(DoctorReport.Open("bugreport", "", x.T("btn-bugreport")), x.FolderToken("logs", x.T("btn-open-logs")));
        }

        static string ShortType(string type)
        {
            int dot = type.LastIndexOf('.');
            return dot >= 0 ? type.Substring(dot + 1) : type;
        }

        /// <summary>The game was killed or lost power last time, without writing a crash report.</summary>
        public static void Shutdown(DoctorContext x)
        {
            if (!x.Doctor.PreviousUnclean) return;
            // A crash report from that session already says why.
            if (x.Doctor.NewCrashThisSession) return;
            x.Warn(x.T("unclean-title", x.Doctor.PreviousSessionTime ?? "?"));
            x.Explain(x.T("unclean-why"));
            x.Advice(x.T("unclean-advice"));
        }

        // ---------------------------------------------------------------- start-up

        public const string TopicStartup = "startup";

        /// <summary>What the game reported while loading mods and starting: mods that threw, were left out, or clashed.</summary>
        public static int Startup(DoctorContext x, bool full)
        {
            LogScan log = x.Ev.Log;
            if (full) x.Section(x.T("sec-startup"));
            if (log == null || !log.Found)
            {
                x.Info(x.T("log-nofile", System.IO.Path.GetFileName(log?.File ?? "?")));
                if (log?.Error != null && log.Error != "nofile") x.Detail(log.Error);
                return 0;
            }
            int before = x.Problems + x.Warnings;

            // Files in the Mods folder the game could not even read as a mod. What the game says about these is of no
            // use to a player (for a zip without a modinfo.json it logs an error of its own), so the doctor's own look
            // at the file says what it is, and the Mods folder page has the fix.
            var fileFailures = log.StartFailures.Where(f => f.IsFile && x.Ev.Mods?.Get(f.Mod) == null).GroupBy(f => f.Mod).ToList();
            if (fileFailures.Count > 0)
            {
                // The game names a file by its name, or by the mod id when it got as far as reading that.
                string FileName(string key) => FailedFile(x, key)?.Name ?? key;
                x.Problem(x.T("start-files-failed", fileFailures.Count, string.Join(", ", fileFailures.Take(full ? 30 : 6).Select(g => FileName(g.Key))) + (!full && fileFailures.Count > 6 ? ", …" : "")));
                foreach (var g in fileFailures.Take(full ? 30 : 6))
                {
                    StartFailure f = g.First();
                    x.Detail(FileName(g.Key) + ": " + (FileReason(x, g.Key) ?? LogScan.Shorten(f.Error ?? f.What, 200)));
                }
                x.Explain(x.T("start-files-failed-why"));
                x.LinkButton(TopicFiles, "", x.T("btn-files"));
            }

            foreach (var group in log.StartFailures.Where(f => !(f.IsFile && x.Ev.Mods?.Get(f.Mod) == null)).GroupBy(f => f.Mod))
            {
                StartFailure f = group.First();
                ModEntry mod = x.Ev.Mods?.Get(f.Mod);
                string name = mod?.Name ?? f.Mod;
                string error = group.Select(g => g.Error).FirstOrDefault(e => !string.IsNullOrEmpty(e)) ?? f.What;
                x.Problem(x.T("start-failed", name, LogScan.Shorten(error, 160)));
                foreach (StartFailure g in group.Take(4))
                {
                    x.Detail(g.What);
                    if (!string.IsNullOrEmpty(g.Error) && g.Error != g.What) x.Detail(LogScan.Shorten(g.Error, 400), 2);
                    foreach (string frame in g.Stack.Take(full ? 8 : 3)) x.Detail(LogScan.Shorten(Anonymise.Line(frame), 220), 2);
                    if (g.Systems.Count > 0) x.Detail(x.T("start-systems", string.Join(", ", g.Systems)), 2);
                }
                string why = Why(x, error, group.SelectMany(g => g.Stack));
                x.Explain(why ?? x.T("start-failed-why"));
                x.Advice(x.T("start-failed-advice", name));
                if (mod != null) ModButtons(x, mod.Id, true);
            }

            if (log.Skipped.Count > 0)
            {
                string list = string.Join(", ", log.Skipped.Take(full ? 40 : 6).Select(s => s.Needs.Count == 0 ? s.Mod : x.T("start-needs", s.Mod, string.Join(", ", s.Needs.Select(n => Need(x, n))))));
                if (!full && log.Skipped.Count > 6) list += ", …";
                x.Warn(x.T("start-skipped", log.Skipped.Count, list));
                x.Explain(x.T("start-skipped-why"));
                x.Advice(x.T("start-skipped-advice"));
                var missing = log.Skipped.SelectMany(s => s.Needs).Where(n => n.why.StartsWith("Missing", StringComparison.OrdinalIgnoreCase)).Select(n => n.dep.Split('@')[0]).Distinct().Take(6).ToList();
                if (missing.Count > 0) x.Buttons(missing.Select(id => DoctorReport.Url(ModDbSearch(id), x.T("btn-find", id))).ToArray());
            }
            if (log.Blocked.Count > 0)
            {
                x.Warn(x.T("start-blocked", log.Blocked.Count));
                foreach (string b in log.Blocked.Take(12)) x.Detail(b);
                x.Explain(x.T("start-blocked-why"));
            }
            if (log.DuplicateIds.Count > 0)
            {
                x.Warn(x.T("start-duplicates", log.DuplicateIds.Count));
                foreach (string d in log.DuplicateIds.Take(12)) x.Detail(Anonymise.Line(d));
                x.Explain(x.T("start-duplicates-why"));
                x.LinkButton(TopicFiles, "", x.T("btn-files"));
            }
            if (log.CyclicDependencies != null)
            {
                x.Warn(x.T("start-cyclic", log.CyclicDependencies));
                x.Explain(x.T("start-cyclic-why"));
            }
            if (log.CompileFailed)
            {
                x.Problem(x.T("start-compile"));
                x.Explain(x.T("start-compile-why"));
            }
            if (log.FailedPatches.Count > 0)
            {
                int total = log.FailedPatches.Values.Sum();
                x.Warn(x.T("start-patches", total, string.Join(", ", log.FailedPatches.OrderByDescending(kv => kv.Value).Take(full ? 30 : 6).Select(kv => x.ModName(kv.Key) + " (" + kv.Value + ")"))));
                foreach (var kv in log.FailedPatches.OrderByDescending(kv => kv.Value).Take(full ? 12 : 3))
                    if (log.FailedPatchExample.TryGetValue(kv.Key, out string ex)) x.Detail(ex, 2);
                x.Explain(x.T("start-patches-why"));
                x.Advice(x.T("start-patches-advice"));
            }
            if (log.Unresolved.Count > 0)
            {
                int total = log.Unresolved.Values.Sum();
                x.Info(x.T("start-unresolved", total, string.Join(", ", log.Unresolved.OrderByDescending(kv => kv.Value).Take(full ? 30 : 6).Select(kv => kv.Key + " (" + kv.Value + ")"))));
                foreach (var kv in log.Unresolved.OrderByDescending(kv => kv.Value).Take(full ? 12 : 3))
                    if (log.UnresolvedExample.TryGetValue(kv.Key, out string ex)) x.Detail(ex, 2);
                x.Explain(x.T("start-unresolved-why"));
            }
            int found = x.Problems + x.Warnings - before;
            if (found == 0 && log.Unresolved.Count == 0)
            {
                int mods = x.Ev.Mods?.UserMods.Count() ?? 0;
                x.Ok(x.T("start-clean", mods));
                if (full) x.Explain(x.T("start-clean-why"));
            }
            if (!full && (found > 0 || log.Unresolved.Count > 0)) x.LinkButton(TopicStartup, "", x.T("btn-startup"));
            return found;
        }

        /// <summary>The file in the Mods folder that a start-up error is about: by its name, or by its mod id when the game had read that much before giving up.</summary>
        static ModFile FailedFile(DoctorContext x, string key)
        {
            var files = x.Ev.Folder?.Files;
            if (files == null || string.IsNullOrEmpty(key)) return null;
            return files.FirstOrDefault(m => string.Equals(m.Name, key, StringComparison.OrdinalIgnoreCase))
                ?? files.FirstOrDefault(m => !m.Loaded && string.Equals(m.ModId, key, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>What a file in the Mods folder is, in a few words, from the doctor's own look at it; null when it looked fine or wasn't looked at.</summary>
        static string FileReason(DoctorContext x, string fileName)
        {
            ModFile f = FailedFile(x, fileName);
            if (f == null) return null;
            if (f.ReadError != null) return x.T("reason-damaged");
            if (f.InnerZip != null) return x.T("reason-innerzip", f.InnerZip);
            if (f.NestedFolder != null) return f.SourceCode ? x.T("reason-source") : x.T("reason-nested", f.NestedFolder);
            if (f.NoModInfo) return f.SourceCode ? x.T("reason-source") : x.T("reason-nomodinfo");
            if (f.ModInfoError != null) return x.T("reason-modinfo");
            if (f.Kind == "zip" && f.Misplaced.Count > 0) return x.T("reason-misplaced", f.Misplaced[0]);
            return null;
        }

        static string Need(DoctorContext x, (string dep, string why) n)
        {
            if (n.why.StartsWith("Missing", StringComparison.OrdinalIgnoreCase)) return n.dep;
            if (n.why.StartsWith("Version mismatch", StringComparison.OrdinalIgnoreCase)) return x.T("start-need-version", n.dep, n.why.Replace("Version mismatch", "").Trim(' ', '(', ')').Replace("has ", ""));
            return n.dep + " (" + n.why + ")";
        }

        // ---------------------------------------------------------------- the log

        public const string TopicLog = "log";

        /// <summary>The errors and warnings in this session's log: counted in the check-up, listed by mod on their own page.</summary>
        public static void Log(DoctorContext x, bool full, string onlyMod = null)
        {
            LogScan log = x.Ev.Log;
            if (full) x.Section(onlyMod == null ? x.T("sec-log") : x.T("sec-log-mod", x.ModName(onlyMod)));
            if (log == null || !log.Found) { if (Listed(x, full)) x.Info(x.T("log-nofile", System.IO.Path.GetFileName(log?.File ?? "?"))); return; }

            List<LogIssue> issues = log.Issues.Where(i => onlyMod == null || string.Equals(i.Mod, onlyMod, StringComparison.OrdinalIgnoreCase)).ToList();

            // Messages the doctor, a mod or the owner can explain get a card each.
            if (onlyMod == null || full)
            {
                foreach (var g in issues.Where(i => i.Known != null).GroupBy(i => i.Known.Id ?? i.Known.Match))
                {
                    KnownProblem k = g.First().Known;
                    if (!full && k.Level <= DoctorLevel.Info) continue; // harmless ones stay on the log's own page
                    KnownCard(x, k, g.ToList());
                }
            }

            // The same line over and over costs time by itself.
            foreach (LogIssue spam in issues.Where(i => i.Count >= 300 || (i.Count >= 60 && i.PerMinute >= 60)).OrderByDescending(i => i.Count).Take(3))
            {
                x.Warn(x.T("log-spam", spam.Count, LogScan.Shorten(Anonymise.Line(spam.Message), 140)));
                if (spam.Mod != null) x.Detail(x.T("log-from", x.ModName(spam.Mod)));
                x.Detail(x.T("log-times", spam.FirstTime, spam.LastTime));
                x.Explain(x.T("log-spam-why"));
                if (spam.Mod != null) ModButtons(x, spam.Mod, true);
            }

            var unknown = issues.Where(i => i.Known == null).ToList();
            int errorKinds = unknown.Count(i => i.Bad), warnKinds = unknown.Count(i => !i.Bad);
            int errorLines = unknown.Where(i => i.Bad).Sum(i => i.Count), warnLines = unknown.Where(i => !i.Bad).Sum(i => i.Count);

            if (!full)
            {
                if (errorKinds == 0) { x.Ok(warnKinds == 0 ? x.T("log-clean") : x.T("log-clean-warnings", warnKinds, warnLines)); }
                else if (x.ListAll)
                {
                    // The check-up is by mod: each mod with errors gets a card of its own, in its own section. What no
                    // mod could be named for is the game's.
                    foreach (var g in unknown.Where(i => i.Bad).GroupBy(i => i.Mod ?? "").OrderByDescending(g => g.Sum(i => i.Count)).Take(40))
                    {
                        bool named = g.Key.Length > 0;
                        int kinds = g.Count(), lines = g.Sum(i => i.Count);
                        x.Warn(named ? x.T("log-errors-mod", x.ModName(g.Key), kinds, lines) : x.T("log-errors-nomod", kinds, lines));
                        x.About(named ? g.Key : DoctorReport.GameSection);
                        foreach (LogIssue i in g.OrderByDescending(i => i.Count).Take(3)) x.Detail((i.Count > 1 ? "×" + i.Count + " " : "") + LogScan.Shorten(Anonymise.Line(i.Message), 200));
                        if (kinds > 3) x.Detail(x.T("more-lines", kinds - 3));
                        x.Explain(x.T("log-errors-why"));
                        x.LinkButton(TopicLog, named ? g.Key : "", named ? x.T("btn-log-mod", x.ModName(g.Key)) : x.T("btn-log"));
                    }
                }
                else
                {
                    x.Warn(x.T("log-errors", errorKinds, errorLines, warnKinds));
                    foreach (var g in unknown.Where(i => i.Bad).GroupBy(i => i.Mod ?? "").OrderByDescending(g => g.Sum(i => i.Count)).Take(6))
                    {
                        string who = g.Key.Length == 0 ? x.T("log-nomod") : x.ModName(g.Key);
                        x.Detail(x.T("log-bymod", who, g.Count(), g.Sum(i => i.Count)) + (g.Key.Length > 0 ? " " + DoctorReport.Open("mod", g.Key, x.T("btn-modpage-short")) : ""));
                        LogIssue worst = g.OrderByDescending(i => i.Count).First();
                        x.Detail(LogScan.Shorten(Anonymise.Line(worst.Message), 200), 2);
                    }
                    x.Explain(x.T("log-errors-why"));
                }
                if (errorKinds + warnKinds > 0 && !(x.ListAll && errorKinds > 0)) x.LinkButton(TopicLog, "", x.T("btn-log"));
                if (log.Truncated) x.Detail(x.T("log-truncated", Size(log.Bytes)));
                return;
            }

            if (unknown.Count == 0) { x.Ok(onlyMod == null ? x.T("log-clean") : x.T("log-clean-mod", x.ModName(onlyMod))); return; }
            x.Line(x.T("log-summary", errorKinds, errorLines, warnKinds, warnLines, System.IO.Path.GetFileName(log.File)), DoctorLineStyle.Note);
            if (log.Truncated) x.Line(x.T("log-truncated", Size(log.Bytes)), DoctorLineStyle.Note);

            int shown = 0;
            foreach (var g in unknown.GroupBy(i => i.Mod ?? "").OrderByDescending(g => g.Where(i => i.Bad).Sum(i => i.Count)).ThenByDescending(g => g.Sum(i => i.Count)))
            {
                if (shown >= 80) break;
                var bad = g.Where(i => i.Bad).OrderByDescending(i => i.Count).ToList();
                var warns = g.Where(i => !i.Bad).OrderByDescending(i => i.Count).ToList();
                string who = g.Key.Length == 0 ? x.T("log-nomod") : x.ModName(g.Key);
                string title = x.T("log-mod-title", who, bad.Count, warns.Count);
                if (bad.Count > 0) x.Warn(title); else x.Info(title);
                foreach (LogIssue i in bad.Concat(warns).Take(onlyMod != null ? 40 : 12))
                {
                    shown++;
                    x.Detail((i.Count > 1 ? "×" + i.Count + " " : "") + "[" + i.Level + "] " + LogScan.Shorten(Anonymise.Line(i.Message), 320));
                    foreach (string frame in i.Stack.Take(3)) x.Detail(LogScan.Shorten(Anonymise.Line(frame), 220), 2);
                    string why = i.Bad ? Why(x, i.Message, i.Stack) : null;
                    if (why != null && !x.Compact) x.Detail(why, 2);
                }
                int more = bad.Count + warns.Count - (onlyMod != null ? 40 : 12);
                if (more > 0) x.Detail(x.T("more-lines", more));
                if (g.Key.Length > 0 && onlyMod == null) ModButtons(x, g.Key, false);
            }
        }

        static void KnownCard(DoctorContext x, KnownProblem k, List<LogIssue> issues)
        {
            int count = issues.Sum(i => i.Count);
            string title = x.Words(k.Title) + (count > 1 ? " (×" + count + ")" : "");
            string owner = !string.IsNullOrEmpty(k.ModId) && k.ModId != DoctorApi.ModId && x.Ev.Mods?.Get(k.ModId) != null ? x.ModName(k.ModId) + ": " : "";
            x.Result(k.Level, owner + title);
            x.Detail(LogScan.Shorten(Anonymise.Line(issues[0].Message), 300));
            var mods = issues.Select(i => i.Mod).Where(m => m != null).Distinct().Take(6).ToList();
            if (mods.Count > 0 && owner.Length == 0) x.Detail(x.T("log-from", string.Join(", ", mods.Select(x.ModName))));
            if (!string.IsNullOrEmpty(k.Explain)) x.Explain(x.Words(k.Explain));
            if (!string.IsNullOrEmpty(k.Advice)) x.Advice(x.Words(k.Advice));
            if (!string.IsNullOrEmpty(k.FixId))
            {
                JsonFix jf = x.Doctor.JsonFixFor(k.FixId);
                if (jf != null && jf.Type == "url") x.UrlButton(jf.Url, x.Words(jf.Label ?? x.T("btn-open-page")));
                else x.FixButton(k.FixId, k.FixArgument ?? "", x.Words(k.FixLabel ?? x.Doctor.FixLabel(k.FixId, x)));
            }
        }
    }
}
