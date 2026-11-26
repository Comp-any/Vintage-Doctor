using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using VintageDoctor.API;

namespace VintageDoctor
{
    /// <summary>
    /// The doctor examining itself on a real server (the CI run sets SelfTestOnStart): made-up logs, crash reports,
    /// Mods folders and config files with known faults go through the same readers and fixes the players get, every
    /// page is built from the real server's own logs, and the API is used the way another mod would. Results go to
    /// ModData/vintagedoctor/selftest (selftest.txt and one text per page).
    /// </summary>
    public static class SelfTest
    {
        sealed class Run_
        {
            public readonly List<string> Lines = new List<string>();
            public int Passed, Failed;
            public void Check(string name, bool ok, string detail = null)
            {
                if (ok) Passed++; else Failed++;
                Lines.Add((ok ? "PASS " : "FAIL ") + name + (string.IsNullOrEmpty(detail) ? "" : " (" + detail + ")"));
            }
            public void Skip(string name, string why) => Lines.Add("SKIP " + name + " (" + why + ")");
            public void Guard(string name, Action body)
            {
                try { body(); }
                catch (Exception e) { Check(name, false, "threw " + e.GetType().Name + ": " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n').FirstOrDefault()?.Trim()); }
            }
        }

        const string FixtureLog = @"21.11.2026 10:00:00 [Notification] Server logger started.
21.11.2026 10:00:00 [Notification] Game Version: v1.22.7 (Stable)
21.11.2026 10:00:00 [Notification] Operating System: Linux (Ubuntu 26.04.1 LTS) [Kernel 7.0.0.1019]
21.11.2026 10:00:00 [Notification] CPU: Test CPU
21.11.2026 10:00:00 [Notification] Available RAM: 15983 MB
21.11.2026 10:00:00 [Notification] Entering runphase LoadAssets
21.11.2026 10:00:00 [Notification] Will search the following paths for mods:
21.11.2026 10:00:00 [Notification]     /home/someone/.config/VintagestoryData/Mods
21.11.2026 10:00:01 [Warning] The following mods where blocked from loading:
21.11.2026 10:00:01 [Warning]   badmod@1.0.0: known to corrupt saves
21.11.2026 10:00:01 [Warning] Multiple mods share the mod ID 'twice' ('twice_1.0.0.zip', 'twice_1.1.0.zip'). Will only load the highest version one - v1.1.0.
21.11.2026 10:00:01 [Notification] Found 9 mods (1 disabled)
21.11.2026 10:00:01 [Error] [needy] Could not resolve some dependencies:
21.11.2026 10:00:01 [Error] [needy]     somelib@1.2.0 - Missing
21.11.2026 10:00:01 [Error] [needy]     otherlib@2.0.0 - Version mismatch (has 1.5.0)
21.11.2026 10:00:01 [Error] [broken.zip] Missing modinfo.json
21.11.2026 10:00:01 [Notification] Mods, sorted by dependency: game, creative, survival, crashy
21.11.2026 10:00:02 [Error] [crashy] An exception was thrown when trying to start the mod:
21.11.2026 10:00:02 [Error] [crashy] Exception: Method not found: 'Void Vintagestory.API.Common.Something.Gone()'.
   at Crashy.CrashySystem.StartServerSide(ICoreServerAPI api) in C:\Users\someone\src\Crashy.cs:line 12
   at Vintagestory.Common.ModLoader.TryRunModPhase(Mod mod, ModSystem system, ICoreAPI api, ModRunPhase phase)
21.11.2026 10:00:02 [Error] Failed to start system Crashy.CrashySystem
21.11.2026 10:00:03 [Error] Patch 0 (target: game:blocktypes/plant/crop/pineapple.json) in patchy:patches/crops.json failed because supplied path /dropsByType/*-16/1/x is invalid
21.11.2026 10:00:03 [Error] Patch 3 (target: game:itemtypes/x.json) in patchy:patches/items.json failed because supplied path /a/b is invalid
21.11.2026 10:00:03 [Error] Patch 1 in patchy:patches/compat.json: File selftestabsentmod:itemtypes/thing.json not found
21.11.2026 10:00:03 [Error] Patch 2 in oldpatchy:patches/old.json: File game:itemtypes/removed.json not found
21.11.2026 10:00:03 [Error] Block with code ghosty:machine-north has defined a block class GhostMachine, no such class registered. Will ignore.
21.11.2026 10:00:03 [Error] Block with code ghosty:machine-north has defined a block entity class GhostMachineEntity, no such class registered. Will ignore.
21.11.2026 10:00:03 [Error] Item with code ghosty:wand has defined an item class GhostWand, but no such class registered. Will ignore.
21.11.2026 10:00:03 [Warning] Collectible behavior GhostGlow for item ghosty:wand not found
21.11.2026 10:00:03 [Warning] Block or Collectible behavior GhostHum for block ghosty:machine-north not found
21.11.2026 10:00:03 [Warning] Block entity behavior GhostPower for block ghosty:machine-north not found
21.11.2026 10:00:03 [Error] Failed registering item ghosty:wand:
21.11.2026 10:00:03 [Error] Failed registering block ghosty:machine-north
21.11.2026 10:00:03 [Error] Item type ghosty:itemtypes/broken.json could not be loaded. Will ignore. Exception:
21.11.2026 10:00:03 [Error] Syntax error in json file 'ghosty:recipes/grid/cutshort.json': Failed deserializing cutshort.json: Unexpected end when reading token. Path ''.
21.11.2026 10:00:03 [Warning] [ghosty] Is a Content mod, but .cs or .dll files were found. These will be ignored.
21.11.2026 10:00:04 [Warning] Failed resolving crafting recipe ingredient with code armory:part-hammerhead-steel in Grid recipe
21.11.2026 10:00:04 [Error] Grid Recipe with output 'armory:part-hammerhead-steel' contains an ingredient that cannot be resolved: Item code armory:part-hammerhead-steel
21.11.2026 10:00:05 [Notification] Entering runphase RunGame
21.11.2026 10:00:06 [Warning] Drink portion size 1 is greater than capacity 0.4 for bricks:mug-red, setting drink portion size to capacity.
21.11.2026 10:00:06 [Warning] Drink portion size 1 is greater than capacity 0.2 for bricks:mug-blue, setting drink portion size to capacity.
21.11.2026 10:00:07 [Error] Exception: Object reference not set to an instance of an object.
   at Crashy.Machines.Pump.Tick(Single dt)
   at Vintagestory.Common.GameTickListener.OnTriggered(Int64 ellapsedMilliseconds)
21.11.2026 10:00:08 [Error] Exception: Object reference not set to an instance of an object.
   at Crashy.Machines.Pump.Tick(Single dt)
   at Vintagestory.Common.GameTickListener.OnTriggered(Int64 ellapsedMilliseconds)
21.11.2026 10:00:09 [Warning] Server overloaded. A tick took 2377ms to complete.
21.11.2026 10:00:10 [Error] The doctor self-test's made-up problem number 7 from 203.0.113.9:42420
";

        const string FixtureCrash = @"Game Version: v1.22.6 (Stable)
11/20/2026 9:15:01 PM: Critical error occurred in the following mod: crashy@0.9.0
Loaded Mods: crashy@0.9.0, game@1.22.6, creative@1.22.6, survival@1.22.6
Involved Harmony IDs: crashy.patches
System.NullReferenceException: Object reference not set to an instance of an object.
   at Crashy.Machines.Pump.Tick(Single dt) in C:\Users\someone\src\Pump.cs:line 40
   at Vintagestory.Common.GameTickListener.OnTriggered(Int64 ellapsedMilliseconds)
Game Version: v1.22.7 (Stable)
11/21/2026 8:01:44 AM: Critical error occurred
Loaded Mods: game@1.22.7, creative@1.22.7, survival@1.22.7
System.IO.FileNotFoundException: Could not load file or assembly 'SomeLib, Version=1.0.0.0'. The system cannot find the file specified.
   at Vintagestory.Server.ServerMain.Process()
";

        public static string Run(VintageDoctorSystem doctor, ICoreServerAPI sapi)
        {
            var t = new Run_();
            string folder = Path.Combine(doctor.DataFolder, "selftest");
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { }
            Directory.CreateDirectory(folder);
            bool ci = doctor.Config.SelfTestOnStart;

            // The test run's second server is full of broken things on purpose: there, only those are looked at.
            if (File.Exists(Path.Combine(Vintagestory.API.Config.GamePaths.ModConfig, SickMarker)))
            {
                t.Guard("the sick server", () => SickServer(t, doctor, sapi, folder));
                Finish(t, doctor, sapi, folder);
                return string.Join("\n", t.Lines);
            }

            t.Guard("small things", () => SmallThings(t));
            t.Guard("the log reader", () => LogReader(t, doctor));
            t.Guard("the crash reader", () => CrashReader(t));
            t.Guard("the Mods folder", () => ModsFolder(t, folder));
            t.Guard("config files", () => ConfigFiles(t, folder));
            t.Guard("known problems from JSON", () => KnownJson(t));
            t.Guard("the API", () => Api(t, doctor, sapi));
            t.Guard("mods as guests", () => Guests(t, doctor, sapi));
            t.Guard("the pages", () => PagesOnRealServer(t, doctor, sapi, folder));
            t.Guard("opening logs", () => OpeningLogs(t, doctor, sapi, folder));
            t.Guard("code patches", () => SharedPatches(t, doctor, sapi));
            t.Guard("formatting tags", () => FormattingTags(t, doctor, sapi, folder));
            t.Guard("where the window opens, and folded sections", () => WindowRules(t));
            t.Guard("a mod closing the game on purpose", () => AnnouncedClose(t, doctor, sapi));
            t.Guard("the example mods", () => Examples(t, doctor, sapi, folder));
            if (ci) t.Guard("fixes on the real server", () => RealFixes(t, doctor, sapi));
            else t.Skip("fixes on the real server", "only with SelfTestOnStart");

            // What is known so far, in case the server doesn't live to see the end.
            try { File.WriteAllText(Path.Combine(folder, "selftest-sync.txt"), string.Join("\n", t.Lines) + "\n" + t.Passed + " passed, " + t.Failed + " failed before the profiler\n"); } catch { }

            // A page and a fix of the example mod that it delivers half a second later, through the game's own ticks:
            // asked for here, looked at when the profiler is done.
            DoctorReport slowPage = null;
            string slowFix = null;
            bool withExample = doctor.Snapshot().Has("doctorexample");
            if (withExample)
            {
                t.Guard("asking the example mod for what takes a while", () =>
                {
                    Evidence evNow = doctor.EvidenceNow(false);
                    doctor.BuildPageThen(evNow, null, "en", "example-tanks", "slow", null, null, false, null, null, false, r => slowPage = r);
                    doctor.RunFixThen("bus:doctorexample:slowfill", "", null, "en", evNow, null, o => slowFix = o);
                    t.Check("example: a page and a fix that take a while aren't there at once", slowPage == null && slowFix == null, slowFix);
                    sapi.InjectConsole("/doctor example-tanks slow");
                });
            }
            Action whatCameLater = () =>
            {
                if (!withExample) return;
                t.Guard("what the example mod delivered later", () =>
                {
                    string page = slowPage?.PlainText(sapi.World) ?? "";
                    t.Check("example: the page came, through the game's own ticks", page.Contains("This page came a moment later") && page.Contains("== The example tanks =="), page.Replace("\n", " / "));
                    t.Check("example: so did the fix's result", slowFix == "Filled 2 example tanks, slowly", slowFix);
                    string last = File.ReadAllText(Path.Combine(doctor.DataFolder, "last-report-server.txt"));
                    t.Check("example: asked for from the console, the page is written out when it comes", last.Contains("This page came a moment later"), First(last, "This page"));
                    t.Check("example: nothing is still being waited for", doctor.WaitingRequests().Count == 0, string.Join(",", doctor.WaitingRequests()));
                });
            };

            // The measured run takes a few seconds: its result is added when it is over, and the file written then.
            bool netStarted = doctor.Net.Start(5);
            t.Check("network: measuring starts, with the game's own count by kind switched on", netStarted && doctor.Net.GameCounting && doctor.Net.SecondsLeft >= 3, doctor.Net.MeasureError);
            t.Check("network: counting mods' messages by channel is patched into both of the server's ways of sending them", NetWatch.ChannelPatches == 2, NetWatch.ChannelPatches + " patched");
            bool started = doctor.Profile.Start(sapi.World, 5);
            t.Check("the tick profiler starts", started, doctor.Profile.Error);
            t.Check("while it runs it knows its seconds left, which go to the player's window with each page", !started || (doctor.Profile.Running && doctor.Profile.SecondsLeft >= 3 && doctor.Profile.SecondsLeft <= 5), doctor.Profile.SecondsLeft + " s left");
            if (started)
            {
                sapi.Event.RegisterCallback(_ =>
                {
                    t.Guard("the tick profiler", () =>
                    {
                        TickProfile p = doctor.Profile;
                        t.Check("the tick profiler finished with ticks", p.HasResult, "ticks " + p.Ticks + ", entries " + p.ByPath.Count + (p.Error != null ? ", " + p.Error : ""));
                        var (all, work) = p.PerTick();
                        t.Check("a tick's time was measured", all > 0, "all " + all.ToString("0.00") + " ms, work " + work.ToString("0.00") + " ms");
                        DoctorReport r = doctor.BuildPage(doctor.EvidenceNow(false), null, "en", Checks.TopicPerf, "");
                        string text = r.PlainText(sapi.World);
                        File.WriteAllText(Path.Combine(folder, "page-perf-profiled.txt"), text);
                        t.Check("the performance page shows the measured run", text.Contains("ms") && text.Contains("%"), null);
                    });
                    t.Guard("the network", () =>
                    {
                        NetWatch n = doctor.Net;
                        t.Check("network: the traffic is sampled every second", n.Error == null && n.Seconds().Count >= 5, (n.Error ?? "") + " " + n.Seconds().Count + " seconds");
                        t.Check("network: the measured run finished, and the game's count by kind is off again", n.HasResult && !n.GameCounting && n.MeasureError == null, n.MeasureError);
                        DoctorReport r = doctor.BuildPage(doctor.EvidenceNow(false), null, "en", Checks.TopicNetwork, "");
                        string text = r.PlainText(sapi.World);
                        File.WriteAllText(Path.Combine(folder, "page-network-measured.txt"), text);
                        t.Check("network: the page shows the measured run and graphs", text.Contains("Measured over") && text.Contains("Last minute") && text.Contains("avg"), First(text, "Measured"));
                        t.Check("network: the page has the measure button and the perf page a button to it", text.Contains("fix doctor:netmeasure")
                            && doctor.BuildPage(doctor.EvidenceNow(false), null, "en", Checks.TopicPerf, "").PlainText(sapi.World).Contains("/doctor network"), null);
                    });
                    whatCameLater();
                    Finish(t, doctor, sapi, folder);
                }, 8000);
            }
            else sapi.Event.RegisterCallback(_ => { whatCameLater(); Finish(t, doctor, sapi, folder); }, 4000);
            return string.Join("\n", t.Lines) + "\n" + t.Passed + " passed, " + t.Failed + " failed so far; the profiler's result follows in " + Anonymise.Short(Path.Combine(folder, "selftest.txt"));
        }

        static void Finish(Run_ t, VintageDoctorSystem doctor, ICoreServerAPI sapi, string folder)
        {
            string summary = "Self-test: " + t.Passed + " passed, " + t.Failed + " failed";
            try { File.WriteAllText(Path.Combine(folder, "selftest.txt"), string.Join("\n", t.Lines) + "\n" + summary + "\n"); }
            catch (Exception e) { sapi.Logger.Error("[vintagedoctor] Could not write the self-test's results: {0}", e.Message); }
            if (t.Failed == 0) sapi.Logger.Notification("[vintagedoctor self-test] {0}", summary);
            else sapi.Logger.Error("[vintagedoctor self-test] {0}: {1}", summary, string.Join(" | ", t.Lines.Where(l => l.StartsWith("FAIL", StringComparison.Ordinal)).Take(10)));
            // The console command, as an admin would type it (its text lands in the server's console).
            try { sapi.InjectConsole("/doctor"); sapi.InjectConsole("/doctor mods"); } catch (Exception e) { sapi.Logger.Warning("[vintagedoctor] /doctor from the console failed: {0}", e.Message); }
        }

        static void SmallThings(Run_ t)
        {
            string g = DoctorReport.Graph("Sent and received", "bytes", new double[] { 0, 1024, 2048 }, new double[] { 10, 20.5, 30 });
            var gm = DoctorReport.ActionToken.Match(g);
            var gv = DoctorReport.GraphValues(gm.Groups[2].Value);
            t.Check("graph: a token holds two lines of numbers and reads back", gm.Success && gm.Groups[1].Value == "graph" && gv.Count == 2 && gv[0].Count == 3 && gv[1][1] == 20.5, g);
            string gp = DoctorReport.Plain(null, g);
            t.Check("graph: written out, it is the newest, the average and the top of each line", gp == "Sent and received: 2.0 KB/s (avg 1.0 KB/s, top 2.0 KB/s) / 30 B/s (avg 20 B/s, top 30 B/s)", gp);
            t.Check("network: rates in B/s, KB/s, MB/s", DoctorReport.Rate(500) == "500 B/s" && DoctorReport.Rate(20 * 1024) == "20 KB/s" && DoctorReport.Rate(3 * 1024 * 1024) == "3.00 MB/s");
            t.Check("network: a lot is over 1.5 MB/s for each player for half a minute", Checks.NetHigh(4e6, 2, 60) && !Checks.NetHigh(2e6, 2, 60) && !Checks.NetHigh(4e6, 2, 10) && Checks.NetHigh(2e6, 0, 60));
            var secs = Enumerable.Range(0, 120).Select(i => new NetWatch.Second { SentTcp = i }).ToList();
            var pts = NetWatch.Points(secs, q => q.Sent, 60);
            t.Check("network: ten minutes put together into points for a graph", pts.Count == 60 && pts[0] == 0.5 && pts[59] == 118.5, string.Join(",", pts.Take(3)));
            t.Check("versions: 1.10.0 is newer than 1.9.2", Versions.Compare("1.10.0", "1.9.2") > 0);
            t.Check("versions: 1.0.0 is newer than 1.0.0-rc.1", Versions.Compare("1.0.0", "1.0.0-rc.1") > 0);
            t.Check("versions: rc.2 is newer than rc.1", Versions.Compare("1.0.0-rc.2", "1.0.0-rc.1") > 0);
            t.Check("versions: v1.2 equals 1.2.0", Versions.Compare("v1.2", "1.2.0") == 0);
            string anon = Anonymise.Line("from 203.0.113.9:42420 at /home/someone/data and C:\\Users\\Some One\\x, Version=1.2.3.4");
            t.Check("addresses and user folders are taken out", !anon.Contains("203.0.113.9") && !anon.Contains("someone") && anon.Contains("1.2.3.4"), anon);
            t.Check("messages that differ only in a code or a number count as one",
                LogScan.Normalise("Drink portion size 1 is greater than capacity 0.4 for bricks:mug-red") == LogScan.Normalise("Drink portion size 2 is greater than capacity 0.2 for bricks:mug-blue"));
            var r = new DoctorReport();
            r.Add(DoctorReport.OkMark + " fine", DoctorLineStyle.Check);
            r.Add(DoctorReport.ProblemMark + " bad " + DoctorReport.Fix('s', "doctor:x", "a|b{c}", "Do it"), DoctorLineStyle.Check);
            r.Add(DoctorReport.Open("mod", "carryon", "Carry On's page"), DoctorLineStyle.Text, 1);
            string plain = r.PlainText(null);
            t.Check("a page as text names the commands behind its buttons", plain.Contains("/doctor fix doctor:x a/b(c)") && plain.Contains("/doctor mod carryon"), plain.Replace("\n", " / "));
            t.Check("a page counts its problems", r.Problems == 1 && r.Warnings == 0);
            DoctorReport back = DoctorReport.FromPacket(r.ToPacket(7));
            t.Check("a page survives being sent", back.Lines.Count == r.Lines.Count && back.Lines[2].Indent == 1 && back.Lines[0].Style == DoctorLineStyle.Check && back.Lines[0].Section == null);
            r.Lines[0].Section = "carryon";
            r.Lines[1].Section = "carryon,game";
            r.SectionTitles["carryon"] = "Carry On 1.14.3";
            back = DoctorReport.FromPacket(r.ToPacket(8));
            t.Check("so do the check-up's sections and their headings", back.Lines[0].Section == "carryon" && back.Lines[1].Section == "carryon,game" && back.Lines[2].Section == "" && back.SectionTitles["carryon"] == "Carry On 1.14.3");
            r.Add(DoctorReport.ProblemMark + " bad " + DoctorReport.Fix('s', "doctor:x", "a|b{c}", "Do it"), DoctorLineStyle.Check);
            t.Check("a card that is under two mods counts once", r.Problems == 1);

            t.Check("check-up: a card is filed under the mods its words name: a name as whole words, a log tag, a file of the mod's, and for a mod that wasn't loaded its bare id",
                Pages.Names("Carry On can't lift this", "carryon", "Carry On", true) && !Pages.Names("Carry Onwards", "carryon", "Carry On", true) && Pages.Names("[Error] [carryon] oops", "carryon", "Carry On", true)
                && Pages.Names("in carryon:patches/x.json", "carryon", "Carry On", true) && !Pages.Names("a carryon bag", "carryon", "Carry On", true) && Pages.Names("left out: configlib (needs vsimgui)", "configlib", null, false)
                && !Pages.Names("the Map is fine", "map", "Map", true));

            // The game's font can't be counted on for symbols: what is handed to it to draw has them written out.
            string marks = DoctorReport.OkMark + " fine\n" + DoctorReport.WarnMark + "\uFE0F careful\n" + DoctorReport.ProblemMark + " broken\n" + DoctorReport.InfoMark + " to know\n  " + DoctorReport.AdviceMark + " do this";
            string drawn = GameFont.Drawable(marks);
            t.Check("font: a page's marks are written in ordinary characters where the game draws them", drawn == "[ok] fine\n[!] careful\n[x] broken\n\u2022 to know\n  -> do this", drawn.Replace("\n", " / "));
            string symbols = GameFont.Drawable("Esc \u2192 Settings \u2190 back \u25B8 open \u25BE shut, a \u2264 b \u2260 c, \u2692 Fix it, 5\u202F000, done \uD83D\uDD27 here \u2B50");
            t.Check("font: arrows, triangles, signs and pictures too", symbols == "Esc -> Settings <- back > open v shut, a <= b != c, Fix it, 5 000, done here *", symbols);
            string plainWords = "Pr\u00FCfung \u00D7 3 \u00B7 \u2018quoted\u2019 \u2026 \u2013 \u2014 \u041F\u0440\u0438\u0432\u0435\u0442 \u65E5\u672C\u8A9E 50 \u00B0C";
            t.Check("font: letters of any language and ordinary punctuation are left alone", GameFont.Drawable(plainWords) == plainWords && GameFont.Drawable("") == "" && GameFont.Drawable(null) == "");
            string chat = VintageDoctorSystem.ChatSafe(DoctorReport.WarnMark + " <b>1 < 2</b> " + DoctorReport.AdviceMark + " go");
            t.Check("font: chat gets the same, with its own markup signs written out", chat == "[!] &lt;b&gt;1 &lt; 2&lt;/b&gt; -&gt; go", chat);
        }

        /// <summary>The part of a page under the heading that has <paramref name="heading"/> in it, up to the next heading.</summary>
        static string Under(string page, string heading)
        {
            var sb = new StringBuilder();
            bool inside = false;
            foreach (string line in page.Split('\n'))
            {
                if (line.StartsWith("== ", StringComparison.Ordinal)) { inside = line.IndexOf(heading, StringComparison.Ordinal) >= 0; continue; }
                if (inside) sb.Append(line).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>The symbols the game's font may lack that are still in a text (none, after GameFont.Drawable).</summary>
        static string Undrawable(string text)
        {
            var found = new StringBuilder();
            foreach (char c in text ?? "")
            {
                bool symbol = (c >= '\u2190' && c <= '\u21FF') || (c >= '\u2300' && c <= '\u27BF') || (c >= '\u2B00' && c <= '\u2BFF') || (c >= '\uFE00' && c <= '\uFE0F') || char.IsSurrogate(c);
                if (symbol && found.ToString().IndexOf(c) < 0) found.Append(c);
            }
            return found.ToString();
        }

        static void LogReader(Run_ t, VintageDoctorSystem doctor)
        {
            LogScan log = LogScan.FromText(FixtureLog, doctor.Snapshot());
            t.Check("log: read without error", log.Error == null, log.Error);
            t.Check("log: the game version", log.GameVersion == "v1.22.7 (Stable)", log.GameVersion);
            t.Check("log: a mod left out for missing dependencies, with both reasons", log.Skipped.Count == 1 && log.Skipped[0].Mod == "needy" && log.Skipped[0].Needs.Count == 2
                && log.Skipped[0].Needs[0].dep == "somelib@1.2.0" && log.Skipped[0].Needs[1].why.StartsWith("Version mismatch"), log.Skipped.Count + " skipped");
            t.Check("log: a blocked mod", log.Blocked.Count == 1 && log.Blocked[0].StartsWith("badmod@1.0.0"), string.Join(";", log.Blocked));
            t.Check("log: a mod id installed twice", log.DuplicateIds.Count == 1 && log.DuplicateIds[0].StartsWith("twice"), string.Join(";", log.DuplicateIds));
            t.Check("log: mods found and disabled", log.ModsFound == 9 && log.ModsDisabled == 1, log.ModsFound + "/" + log.ModsDisabled);
            StartFailure crashy = log.StartFailures.FirstOrDefault(f => f.Mod == "crashy");
            t.Check("log: a mod that threw while starting, with its error, trace and system", crashy != null && crashy.Error.StartsWith("Method not found") && crashy.Stack.Count == 2 && crashy.Systems.Contains("Crashy.CrashySystem"),
                crashy == null ? "none" : crashy.Error + " / " + crashy.Stack.Count + " frames / " + string.Join(",", crashy.Systems));
            t.Check("log: a file that isn't a mod", log.StartFailures.Any(f => f.Mod == "broken.zip" && f.IsFile), string.Join(";", log.StartFailures.Select(f => f.Mod)));
            t.Check("log: failed JSON patches by mod", log.FailedPatches.TryGetValue("patchy", out int n) && n == 2, string.Join(";", log.FailedPatches.Select(kv => kv.Key + "=" + kv.Value)));
            t.Check("log: recipes naming things that don't exist, once each", log.Unresolved.TryGetValue("armory", out int u) && u == 1, string.Join(";", log.Unresolved.Select(kv => kv.Key + "=" + kv.Value)));
            t.Check("log: a patch for a mod that isn't installed is noted, not counted as failed", log.MissingTargets.TryGetValue("patchy", out List<string> compat) && compat.Count == 1
                && compat[0] == "selftestabsentmod:itemtypes/thing.json", string.Join(";", log.MissingTargets.Select(kv => kv.Key + "=" + string.Join(",", kv.Value))));
            t.Check("log: a patch for a file the game no longer has counts as failed", log.FailedPatches.TryGetValue("oldpatchy", out int old) && old == 1 && log.MissingTargets.ContainsKey("oldpatchy"));
            t.Check("log: blocks and items whose code is missing", log.MissingClasses.Count == 6 && log.MissingClasses.Contains(("block", "ghosty:machine-north", "GhostMachine"))
                && log.MissingClasses.Contains(("behavior", "ghosty:wand", "GhostGlow")) && log.MissingClasses.Contains(("behavior", "ghosty:machine-north", "GhostHum"))
                && log.MissingClasses.Contains(("behavior", "ghosty:machine-north", "GhostPower"))
                && log.MissingClasses.Contains(("entity", "ghosty:machine-north", "GhostMachineEntity")) && log.MissingClasses.Contains(("item", "ghosty:wand", "GhostWand")),
                string.Join(";", log.MissingClasses.Select(m => m.kind + " " + m.code + " " + m.className)));
            t.Check("log: files of a mod the game couldn't read, in the words of two game versions", log.BrokenTypes.TryGetValue("ghosty", out List<string> unread) && unread.Count == 2
                && unread[0] == "itemtypes/broken.json" && unread[1] == "recipes/grid/cutshort.json",
                string.Join(";", log.BrokenTypes.Select(kv => kv.Key + "=" + string.Join(",", kv.Value))));
            t.Check("log: blocks and items the game refused to add", log.FailedRegistrations.Count == 2 && log.FailedRegistrations.Contains(("item", "ghosty:wand"))
                && log.FailedRegistrations.Contains(("block", "ghosty:machine-north")), string.Join(";", log.FailedRegistrations.Select(r => r.kind + " " + r.code)));
            t.Check("log: code in a mod that calls itself a content mod", log.IgnoredCode.Count == 1 && log.IgnoredCode[0] == "ghosty", string.Join(";", log.IgnoredCode));
            t.Check("log: a slow tick", log.Overloads.Count == 1 && log.Overloads[0].ms == 2377);
            LogIssue drink = log.Issues.FirstOrDefault(i => i.Message.StartsWith("Drink portion"));
            t.Check("log: the same warning twice is one entry with a count", drink != null && drink.Count == 2 && !drink.Startup, drink?.Count.ToString());
            LogIssue nre = log.Issues.FirstOrDefault(i => i.Message.StartsWith("Exception: Object reference"));
            t.Check("log: an exception keeps its stack trace and count", nre != null && nre.Count == 2 && nre.Stack.Count == 2, nre == null ? "none" : nre.Count + "x, " + nre.Stack.Count + " frames");
            t.Check("log: start-up lines aren't counted as errors of their own", !log.Issues.Any(i => i.Message.Contains("Could not resolve") || i.Message.Contains("Patch 0")), log.Issues.Count + " issues");
            t.Check("an error is explained in plain words", WhyFor(doctor, "Method not found: 'Void X.Y()'") != null && WhyFor(doctor, "Could not load file or assembly 'A'") != null && WhyFor(doctor, "all is well") == null);
            t.Check("more errors explained: a name taken twice, a library for another system, a mod packed wrong",
                WhyFor(doctor, "System.ArgumentException: An item with the same key has already been added. Key: pump") == WhyFor(doctor, "Block class 'Pump' is already registered")
                && WhyFor(doctor, "System.DllNotFoundException: Unable to load shared library 'thing'") != null && WhyFor(doctor, "System.BadImageFormatException: Bad IL format.") != null
                && WhyFor(doctor, "Found multiple .dll files with ModSystems and/or ModInfo attributes") != null
                && WhyFor(doctor, "Please restart the game. The mod's .dll was already loaded and cannot be reloaded.") != null && WhyFor(doctor, "Block class 'Pump' is already registered") != null
                && WhyFor(doctor, "Attempted to register Item with code a:b, but an item with such code already exists. Must use a unique code") == WhyFor(doctor, "Block class 'Pump' is already registered"));
            var growing = new MemoryWatch();
            var steady = new MemoryWatch();
            for (int step = 0; step < 8; step++) { growing.Add(40 - step * 5, (1000L + step * 150) * 1024 * 1024); steady.Add(40 - step * 5, (1000L + step % 2 * 30) * 1024 * 1024); }
            var up = growing.Trend();
            var flat = steady.Trend();
            t.Check("memory that keeps growing is told from memory that is only large", up != null && flat != null && Checks.Leaking(up.Value.minutes, up.Value.was, up.Value.now)
                && !Checks.Leaking(flat.Value.minutes, flat.Value.was, flat.Value.now) && new MemoryWatch().Trend() == null,
                up == null ? "no trend" : up.Value.minutes + " min, " + up.Value.was / 1048576 + " to " + up.Value.now / 1048576 + " MB");

            // The same through the known-problem list and the ignore list.
            var known = new KnownProblems();
            known.Add(new KnownProblem { Id = "t:made-up", ModId = "tester", Match = "made-up problem number \\d+", IsRegex = true, Level = DoctorLevel.Problem, Title = "A made-up problem" });
            var ev = new Evidence { Side = EnumAppSide.Server, Mods = doctor.Snapshot(), Log = LogScan.FromText(FixtureLog, doctor.Snapshot()) };
            ev.Apply(known, new DoctorConfig { IgnoreLogMessages = new List<string> { "Drink portion" } });
            LogIssue made = ev.Log.Issues.FirstOrDefault(i => i.Message.Contains("made-up"));
            t.Check("a known problem is recognised by its pattern and counted against its mod", made?.Known != null && made.Known.Id == "t:made-up" && made.Mod == "tester", made?.Mod);
            t.Check("ignored messages are left out", !ev.Log.Issues.Any(i => i.Message.StartsWith("Drink portion")));
        }

        static string WhyFor(VintageDoctorSystem doctor, string message)
        {
            var x = new DoctorContext { Doctor = doctor, LanguageCode = "en" };
            return Checks.Why(x, message);
        }

        static void CrashReader(Run_ t)
        {
            CrashScan scan = CrashScan.FromText(FixtureCrash, null);
            t.Check("crash: two reports read", scan.Error == null && scan.Entries.Count == 2, scan.Error ?? scan.Entries.Count.ToString());
            if (scan.Entries.Count < 2) return;
            CrashEntry a = scan.Entries[0], b = scan.Entries[1];
            t.Check("crash: the mod the game named, and its version then", a.NamedMods.Count == 1 && a.NamedMods[0] == "crashy@0.9.0" && a.VersionAtCrash("crashy") == "0.9.0" && a.Culprits.First() == "crashy", string.Join(",", a.NamedMods));
            t.Check("crash: the error and its trace", a.ExceptionType == "System.NullReferenceException" && a.Message.StartsWith("Object reference") && a.Stack.Count == 2, a.ExceptionType + " / " + a.Stack.Count);
            t.Check("crash: the Harmony ids involved", a.HarmonyIds.Count == 1 && a.HarmonyIds[0] == "crashy.patches");
            t.Check("crash: when it happened is kept as written", a.When == "11/20/2026 9:15:01 PM", a.When);
            t.Check("crash: one with no mod named", b.NamedMods.Count == 0 && b.ExceptionType == "System.IO.FileNotFoundException" && b.LoadedMods.Count == 3, b.ExceptionType);
            t.Check("crash: the latest is the last in the file", scan.Latest == b);
            // A server's own report: the mod list is empty there.
            CrashScan server = CrashScan.FromText("Game Version: v1.22.7 (Stable)\n11/21/2026 05:56:18: Critical error occurred\nLoaded Mods: \nSystem.NullReferenceException: Object reference not set to an instance of an object.\n   at Vintagestory.API.Common.FrameProfilerUtil.End()\n", null);
            t.Check("crash: a server's report with an empty mod list", server.Entries.Count == 1 && server.Latest.ExceptionType == "System.NullReferenceException" && server.Latest.LoadedMods.Count == 0 && server.Latest.Stack.Count == 1,
                server.Latest?.ExceptionType + " / " + server.Latest?.Message);
        }

        static void WriteZip(string path, params (string name, string text)[] entries)
        {
            using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var (name, text) in entries)
            {
                using Stream s = zip.CreateEntry(name).Open();
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                s.Write(bytes, 0, bytes.Length);
            }
        }

        static string Info(string id, string version, string game = "1.22.0") =>
            "{ \"type\": \"content\", \"modid\": \"" + id + "\", \"name\": \"" + id + "\", \"version\": \"" + version + "\", \"dependencies\": { \"game\": \"" + game + "\", \"somelib\": \"1.0.0\" } }";

        static void ModsFolder(Run_ t, string folder)
        {
            string mods = Path.Combine(folder, "fixture-mods"), backups = Path.Combine(folder, "fixture-backups");
            Directory.CreateDirectory(mods);
            WriteZip(Path.Combine(mods, "good_1.0.0.zip"), ("modinfo.json", Info("good", "1.0.0")), ("assets/good/lang/en.json", "{}"));
            WriteZip(Path.Combine(mods, "backslash.zip"), ("modinfo.json", Info("backslash", "1.0.0")), ("assets\\backslash\\lang\\en.json", "{}"));
            WriteZip(Path.Combine(mods, "nested.zip"), ("Nested-main/modinfo.json", Info("nested", "2.0.0")), ("Nested-main/assets/nested/lang/en.json", "{}"));
            WriteZip(Path.Combine(mods, "notamod.zip"), ("readme.txt", "hello"));
            WriteZip(Path.Combine(mods, "source.zip"), ("Thing.csproj", "<Project/>"), ("src/Thing.cs", "class Thing {}"));
            WriteZip(Path.Combine(mods, "twice_1.0.0.zip"), ("modinfo.json", Info("twice", "1.0.0")));
            WriteZip(Path.Combine(mods, "twice_1.10.0.zip"), ("modinfo.json", Info("twice", "1.10.0")));
            WriteZip(Path.Combine(mods, "future.zip"), ("modinfo.json", Info("future", "1.0.0", "9.0.0")));
            WriteZip(Path.Combine(mods, "badinfo.zip"), ("modinfo.json", "{ \"modid\": \"badinfo\", "));
            WriteZip(Path.Combine(folder, "inner-real.zip"), ("modinfo.json", Info("wrapped", "1.0.0")));
            using (ZipArchive outer = ZipFile.Open(Path.Combine(mods, "wrapped-download.zip"), ZipArchiveMode.Create)) outer.CreateEntryFromFile(Path.Combine(folder, "inner-real.zip"), "wrapped_1.0.0.zip");
            File.WriteAllBytes(Path.Combine(mods, "corrupt.zip"), Encoding.ASCII.GetBytes("this is not a zip file at all, just some text pretending"));
            File.WriteAllText(Path.Combine(mods, "packed.rar"), "x");
            File.WriteAllText(Path.Combine(mods, "half.zip.crdownload"), "x");
            File.WriteAllText(Path.Combine(mods, "notes.txt"), "x");
            Directory.CreateDirectory(Path.Combine(mods, "foldermod"));
            File.WriteAllText(Path.Combine(mods, "foldermod", "modinfo.json"), Info("foldermod", "0.1.0"));
            Directory.CreateDirectory(Path.Combine(mods, "foldermod", "assets", "game", "recipes", "grid"));
            File.WriteAllText(Path.Combine(mods, "foldermod", "assets", "game", "recipes", "grid", "x.json"), "[]");
            WriteZip(Path.Combine(mods, "libs_1.0.0.zip"), ("modinfo.json", Info("libs", "1.0.0")), ("libs.dll", "not really code"), ("0Harmony.dll", "not really code"), ("native/thing.dll", "x"),
                ("assets/libs/itemtypes/thing.json", "{}"), ("assets/libs/lang/en.json", "{}"), ("assets/game/recipes/grid/x.json", "[]"), ("assets/game/lang/en.json", "{}"),
                ("assets/game/textures/block/z.png", "x"), ("assets/othermod/itemtypes/y.json", "{}"));
            WriteZip(Path.Combine(mods, "stray.zip"), ("modinfo.json", Info("stray", "1.0.0")), ("lib/Helper.dll", "not really code"));

            FolderScan scan = FolderScan.Read(new[] { mods }, null);
            ModFile F(string name) => scan.Files.FirstOrDefault(f => f.Name == name);
            t.Check("folder: every mod file found, a text file left alone", scan.Files.Count == 16 && F("notes.txt") == null, scan.Files.Count + ": " + string.Join(",", scan.Files.Select(f => f.Name)));
            t.Check("folder: a good zip", F("good_1.0.0.zip") is ModFile g && !g.Broken && g.ModId == "good" && g.Version == "1.0.0" && g.GameDep == "1.22.0" && g.Deps.Count == 1);
            t.Check("folder: backslashes in file names", F("backslash.zip")?.Backslashes == true);
            t.Check("folder: modinfo one folder down", F("nested.zip")?.NestedFolder == "Nested-main" && F("nested.zip").ModId == "nested");
            t.Check("folder: a zip that isn't a mod", F("notamod.zip")?.NoModInfo == true && F("notamod.zip").SourceCode == false);
            t.Check("folder: source code instead of a release", F("source.zip")?.SourceCode == true);
            t.Check("folder: a broken modinfo.json", F("badinfo.zip")?.ModInfoError != null);
            t.Check("folder: a zip inside a zip", F("wrapped-download.zip")?.InnerZip == "wrapped_1.0.0.zip");
            t.Check("folder: a damaged zip", F("corrupt.zip")?.ReadError != null);
            t.Check("folder: an archive the game can't read, and a download that didn't finish", F("packed.rar")?.Kind == "archive" && F("half.zip.crdownload")?.Kind == "partial");
            t.Check("folder: a mod in a folder", F("foldermod")?.Kind == "folder" && F("foldermod").ModId == "foldermod");
            ModFile libs = F("libs_1.0.0.zip");
            t.Check("folder: what a mod brings: its libraries, its own files, and what it puts into the game's and other mods' folders", libs != null && !libs.Broken
                && libs.Dlls.Count == 2 && libs.Dlls.Contains("0Harmony.dll") && libs.OwnData.Count == 1 && libs.OwnData.Contains("itemtypes/thing.json")
                && libs.ForeignAssets.Count == 3 && libs.ForeignAssets.Contains("game:recipes/grid/x.json") && libs.ForeignAssets.Contains("othermod:itemtypes/y.json")
                && libs.ForeignAssets.Contains("game:textures/block/z.png") && libs.Misplaced.Count == 0,
                libs == null ? "not found" : string.Join(",", libs.Dlls) + " / " + string.Join(",", libs.OwnData) + " / " + string.Join(",", libs.ForeignAssets) + " / " + string.Join(",", libs.Misplaced));
            t.Check("folder: the same for a mod kept as a folder", F("foldermod").ForeignAssets.Count == 1 && F("foldermod").ForeignAssets[0] == "game:recipes/grid/x.json", string.Join(",", F("foldermod").ForeignAssets));
            t.Check("folder: a .dll in a subfolder, which the game refuses to load", F("stray.zip") is ModFile stray && stray.Broken && stray.Misplaced.Count == 1 && stray.Misplaced[0] == "lib/Helper.dll");
            var dups = scan.Duplicates();
            t.Check("folder: a mod installed twice, newest first", dups.Count == 1 && dups[0].id == "twice" && dups[0].copies[0].Version == "1.10.0", dups.Count + " duplicates");
            t.Check("folder: reading a damaged zip to the end fails, a good one doesn't", FolderScan.TestZip(Path.Combine(mods, "good_1.0.0.zip")) == null && FolderScan.TestZip(Path.Combine(mods, "corrupt.zip")) != null);
            ZipHealth.Test(scan.Files, -1);
            t.Check("folder: every zip that opens is read to its end as part of the look, and the whole ones pass", F("good_1.0.0.zip") is ModFile whole && whole.Tested && whole.Damage == null
                && scan.Files.Where(f => f.Kind == "zip" && f.ReadError == null).All(f => f.Tested) && !(F("corrupt.zip")?.Tested ?? true));

            int changed = FolderScan.Repack(Path.Combine(mods, "backslash.zip"), backups, out string backup1);
            ModFile fixedSlash = FolderScan.Look(Path.Combine(mods, "backslash.zip"));
            t.Check("fix: backslashes repacked, the original kept", changed == 1 && fixedSlash != null && !fixedSlash.Backslashes && !fixedSlash.Broken && fixedSlash.Entries == 2 && File.Exists(backup1), "changed " + changed);
            FolderScan.Repack(Path.Combine(mods, "nested.zip"), backups, out string backup2);
            ModFile fixedNested = FolderScan.Look(Path.Combine(mods, "nested.zip"));
            t.Check("fix: the outer folder taken away, the original kept", fixedNested != null && fixedNested.NestedFolder == null && !fixedNested.NoModInfo && fixedNested.ModId == "nested" && File.Exists(backup2));
            int dropped = FolderScan.Repack(Path.Combine(mods, "libs_1.0.0.zip"), backups, out string backup3, n => n == "0Harmony.dll");
            ModFile stripped = FolderScan.Look(Path.Combine(mods, "libs_1.0.0.zip"));
            t.Check("fix: a library the game already has taken out of a mod, the rest and the original kept", dropped == 1 && stripped != null && !stripped.Broken && stripped.Dlls.Count == 1
                && stripped.Dlls[0] == "libs.dll" && stripped.ForeignAssets.Count == 3 && stripped.Entries == 9 && File.Exists(backup3), "dropped " + dropped + ", " + stripped?.Entries + " entries");
            bool refused = false;
            try { FolderScan.Repack(Path.Combine(mods, "notamod.zip"), backups, out _); } catch (InvalidOperationException) { refused = true; }
            t.Check("fix: a zip that can't be made right is left as it was", refused && File.Exists(Path.Combine(mods, "notamod.zip")) && !File.Exists(Path.Combine(mods, "notamod.zip.doctor-tmp")));
            string moved = FolderScan.MoveAside(Path.Combine(mods, "corrupt.zip"), backups);
            string movedFolder = FolderScan.MoveAside(Path.Combine(mods, "foldermod"), backups);
            t.Check("fix: a file and a folder moved aside, not deleted", File.Exists(moved) && !File.Exists(Path.Combine(mods, "corrupt.zip")) && Directory.Exists(movedFolder) && !Directory.Exists(Path.Combine(mods, "foldermod")));
            string again = FolderScan.MoveAside(Path.Combine(mods, "packed.rar"), backups);
            File.WriteAllText(Path.Combine(mods, "packed.rar"), "y");
            string again2 = FolderScan.MoveAside(Path.Combine(mods, "packed.rar"), backups);
            t.Check("fix: a second file of the same name doesn't overwrite the first backup", again != again2 && File.Exists(again) && File.Exists(again2));
        }

        static void ConfigFiles(Run_ t, string folder)
        {
            string dir = Path.Combine(folder, "fixture-config");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "good.json"), "{ // a comment, as the game allows\n  \"a\": 1, \"b\": [1, 2, 3] }");
            File.WriteAllText(Path.Combine(dir, "broken.json"), "{ \"a\": 1,\n  \"b\": [1, 2,\n}");
            File.WriteAllText(Path.Combine(dir, "cutshort.json"), "{ \"a\": 1, \"b\": ");
            File.WriteAllText(Path.Combine(dir, "empty.json"), "");
            File.WriteAllBytes(Path.Combine(dir, "zeros.json"), new byte[64]);
            ConfigFile good = ConfigScan.Check(Path.Combine(dir, "good.json")), broken = ConfigScan.Check(Path.Combine(dir, "broken.json")),
                empty = ConfigScan.Check(Path.Combine(dir, "empty.json")), zeros = ConfigScan.Check(Path.Combine(dir, "zeros.json"));
            t.Check("config: a good file with a comment passes", !good.Bad, good.Error);
            t.Check("config: a broken file fails with where", broken.Error != null && broken.Error.Contains("line"), broken.Error);
            t.Check("config: an empty file and one full of zeros", empty.Empty && zeros.Empty);
            t.Check("config: a file cut short fails", ConfigScan.Check(Path.Combine(dir, "cutshort.json")).Error != null);
            ConfigScan scan = ConfigScan.Read(dir, dir, null, new[] { new KeyValuePair<string, string>("good.json", "ownermod") });
            t.Check("config: a folder is read, and a declared owner kept", scan.Files.Count == 5 && scan.Files.First(f => f.Name == "good.json").Mod == "ownermod", scan.Files.Count + " files");
        }

        static void KnownJson(Run_ t)
        {
            var known = new KnownProblems();
            JObject root = JObject.Parse(@"{
              ""knownProblems"": [
                { ""id"": ""old"", ""match"": ""config is outdated"", ""level"": ""warning"", ""title"": ""The config is from an older version"", ""explain"": ""why"", ""advice"": ""do"",
                  ""fix"": { ""type"": ""resetConfig"", ""file"": ""mymod.json"", ""label"": ""Start it afresh"" } },
                { ""id"": ""rx"", ""match"": ""pump \\d+ jammed"", ""regex"": true, ""level"": ""problem"", ""title"": ""A pump jammed"", ""side"": ""server"",
                  ""fix"": { ""type"": ""command"", ""command"": ""/mymod unjam"" } },
                { ""match"": ""no title here"" },
                { ""id"": ""bad"", ""match"": ""x"", ""title"": ""y"", ""fix"": { ""type"": ""format-disk"" } }
              ],
              ""conflicts"": [ { ""with"": ""othermod"", ""level"": ""problem"", ""why"": ""both replace the firepit"" } ],
              ""recommends"": [ { ""mod"": ""helper"", ""why"": ""nicer"" } ],
              ""configFiles"": [ ""mymod.json"", ""mymod/extra.json"" ]
            }");
            List<JsonFix> fixes = known.LoadJson("mymod", "test.json", root);
            t.Check("json: known problems read, the incomplete one refused", known.Count == 3 && known.LoadErrors.Count == 2, known.Count + " read, errors: " + string.Join(" | ", known.LoadErrors));
            t.Check("json: fixes read, an unknown kind refused", fixes.Count == 2 && fixes[0].Type == "resetconfig" && fixes[0].File == "mymod.json" && fixes[1].Type == "command" && fixes[1].Command == "/mymod unjam", fixes.Count.ToString());
            t.Check("json: conflicts, recommendations and config files", known.Conflicts.Count == 1 && known.Conflicts[0].other == "othermod" && known.Conflicts[0].level == DoctorLevel.Problem
                && known.Recommends.Count == 1 && known.ConfigOwners.Count == 2 && known.ConfigOwners["mymod/extra.json"] == "mymod");
            var issue = new LogIssue { Level = "Error", Message = "[mymod] pump 12 jammed" };
            t.Check("json: a pattern matches on its side only", known.Match(issue, EnumAppSide.Server)?.Id == "mymod:rx" && known.Match(issue, EnumAppSide.Client) == null);
        }

        static void Api(Run_ t, VintageDoctorSystem doctor, ICoreServerAPI sapi)
        {
            IDoctorApi api = DoctorApi.Get(sapi);
            t.Check("api: other mods can find the doctor", api != null && api.ApiVersion == 3 && api.DoctorVersion == doctor.DoctorVersion);
            if (api == null) return;
            const string me = "doctorselftest";
            bool fixRan = false;
            api.RegisterCheck(new DoctorCheck(me, me + ":pipes", ctx =>
            {
                ctx.Warn("3 pipes aren't connected");
                ctx.Detail("at 10 5 10");
                ctx.Explain("pipes need a neighbour");
                ctx.Advice("connect them");
                ctx.FixButton(me + ":reconnect", "all", "Reconnect them");
                ctx.Ok("pumps are fine");
            }));
            api.RegisterCheck(new DoctorCheck(me, me + ":slow", ctx => ctx.Info("a deeper look")) { Deep = true });
            api.RegisterCheck(new DoctorCheck(me, me + ":throws", ctx => throw new InvalidOperationException("the check's own bug")));
            api.RegisterFix(new DoctorFix(me, me + ":reconnect", ctx => { fixRan = true; return ctx.Argument == "all" ? "Reconnected 3 pipes" : ctx.Failed("which?"); }));
            api.RegisterTopic(new DoctorTopic(me, "selftest-pipes", "My pipes aren't moving anything", ctx => ctx.Ok("page built with " + ctx.Argument)));
            api.RegisterKnownProblem(new KnownProblem { Id = me + ":known", ModId = me, Match = "pipe network overflow", Title = "The pipe network overflowed" });
            api.ReportIncident(me, DoctorLevel.Warning, "A pump jammed", "at 1 2 3");
            api.ReportIncident(me, DoctorLevel.Warning, "A pump jammed", "at 4 5 6");
            busTest = true;
            EventBusListenerDelegate collect = (string name, ref EnumHandling handling, IAttribute data) =>
            {
                if (!busTest || !(data is ITreeAttribute tree) || !(tree["results"] is ITreeAttribute results)) return;
                ITreeAttribute r = results.GetOrAddTreeAttribute("busmod:tanks");
                r.SetString("level", "warning");
                r.SetString("title", "2 tanks are leaking");
                r.SetString("details", "tank at 1 1 1\ntank at 2 2 2");
                r.SetString("fix", "busmod:seal");
                r.SetString("fixLabel", "Seal them");
            };
            EventBusListenerDelegate busFix = (string name, ref EnumHandling handling, IAttribute data) =>
            {
                if (busTest && data is ITreeAttribute tree && tree.GetString("id") == "busmod:seal") tree.SetString("result", "Sealed 2 tanks");
            };
            sapi.Event.RegisterEventBusListener(collect, 0.5, VintageDoctorSystem.CollectEvent);
            sapi.Event.RegisterEventBusListener(busFix, 0.5, VintageDoctorSystem.FixEvent);

            Evidence ev = doctor.EvidenceNow(false);
            string overview = doctor.BuildPage(ev, null, "en", Pages.Overview, "").PlainText(sapi.World);
            t.Check("api: a registered check shows in the check-up with its details and fix", overview.Contains("3 pipes aren't connected") && overview.Contains("at 10 5 10") && overview.Contains("/doctor fix " + me + ":reconnect all")
                && overview.Contains("pumps are fine"), null);
            t.Check("api: a check marked Deep runs in the check-up like any other (there is no second, slower round since 1.3.0)", overview.Contains("a deeper look") && Pages.Canonical("deep") == Pages.Overview);
            t.Check("api: a check that throws is reported, and the rest still runs", overview.Contains("the check's own bug") && overview.Contains("A pump jammed"));
            t.Check("api: an incident reported twice is shown once with a count", overview.Contains("A pump jammed (×2)"));
            t.Check("api: findings handed over on the event bus show too", overview.Contains("2 tanks are leaking") && overview.Contains("tank at 2 2 2") && overview.Contains("/doctor fix bus:busmod:seal"));
            string outcome = doctor.RunFix(me + ":reconnect", "all", null, "en", ev);
            t.Check("api: a registered fix runs and reports", fixRan && outcome == "Reconnected 3 pipes", outcome);
            t.Check("api: a fix can say it failed", doctor.RunFix(me + ":reconnect", "", null, "en", ev).StartsWith(DoctorReport.WarnMark));
            t.Check("api: an unknown fix is refused", doctor.RunFix("nobody:nothing", "", null, "en", ev).StartsWith(DoctorReport.WarnMark));
            t.Check("api: a fix over the event bus runs", doctor.RunFix("bus:busmod:seal", "", null, "en", ev) == "Sealed 2 tanks");
            t.Check("api: a fix nobody answers on the event bus is reported", doctor.RunFix("bus:busmod:other", "", null, "en", ev).StartsWith(DoctorReport.WarnMark));
            string topic = doctor.BuildPage(ev, null, "en", "selftest-pipes", "north").PlainText(sapi.World);
            t.Check("api: a registered page opens with its argument", topic.Contains("page built with north") && topic.Contains("My pipes aren't moving anything"), topic.Replace("\n", " / "));
            bool refused = false;
            try { api.RegisterTopic(new DoctorTopic(me, "crash", "x", ctx => { })); } catch (ArgumentException) { refused = true; }
            t.Check("api: a page can't take one of the doctor's own names", refused);
            // The check-up as it is shown: the game's section first, then one for each mod, with that mod's cards and the buttons it added.
            DoctorReport half = doctor.BuildPage(ev, null, "en", Pages.Overview, "");
            DoctorReport whole = doctor.CheckUp(half, ev, null, "en");
            string shown = whole.PlainText(sapi.World);
            File.WriteAllText(Path.Combine(doctor.DataFolder, "selftest", "checkup-by-mod.txt"), shown);
            string mine = Under(shown, me), game = Under(shown, "The game (Vintage Story");
            t.Check("check-up: every card has a section, and the page begins with the game's", half.Lines.All(l => l.Section != null) && shown.TrimStart().StartsWith("== The game (Vintage Story " + GameVersion.ShortGameVersion + ") =="),
                First(shown, "=="));
            t.Check("check-up: a mod's cards are under that mod's heading, not the game's", mine.Contains("3 pipes aren't connected") && mine.Contains("pumps are fine") && mine.Contains("A pump jammed") && !game.Contains("3 pipes aren't connected"),
                mine.Replace("\n", " / "));
            t.Check("check-up: findings handed over on the event bus are under their mod too", whole.Lines.Any(l => l.Section == me && l.Style == DoctorLineStyle.Check) && Under(shown, "busmod").Contains("2 tanks are leaking"),
                Under(shown, "busmod").Replace("\n", " / "));
            t.Check("check-up: a page a mod added is a button in that mod's section", mine.Contains("My pipes aren't moving anything (/doctor selftest-pipes)") && !game.Contains("selftest-pipes"));
            t.Check("check-up: the doctor's own pages are buttons in the game's section", game.Contains("/doctor crash") && game.Contains("/doctor bugreport") && !mine.Contains("/doctor crash"));
            t.Check("check-up: every line that is shown is in one section, so the window can show one mod alone",
                whole.Lines.Where(l => l.Style == DoctorLineStyle.Check || l.Style == DoctorLineStyle.Heading).All(l => !string.IsNullOrEmpty(l.Section) && l.Section.IndexOf(',') < 0),
                string.Join(" | ", whole.Lines.Where(l => (l.Style == DoctorLineStyle.Check || l.Style == DoctorLineStyle.Heading) && (string.IsNullOrEmpty(l.Section) || l.Section.IndexOf(',') >= 0)).Select(l => l.Text).Take(3)));
            // Two halves, as a player's game puts its own and the server's together.
            DoctorReport other = DoctorReport.FromPacket(half.ToPacket(1));
            other.Lines.Add(new DoctorLine { Text = DoctorReport.WarnMark + " only the server saw this", Style = DoctorLineStyle.Check, Section = DoctorReport.GameSection });
            var both = new DoctorReport();
            var bothX = new DoctorContext { Doctor = doctor, Api = sapi, Side = EnumAppSide.Server, LanguageCode = "en", Topic = Pages.Overview, Ev = ev, R = both };
            Pages.Arrange(bothX, new List<(DoctorReport half, string note)> { (half, null), (other, "On the server:") }, null, true, false);
            string merged = both.PlainText(sapi.World);
            int noteAt = merged.IndexOf("On the server:", StringComparison.Ordinal), onlyAt = merged.IndexOf("only the server saw this", StringComparison.Ordinal);
            t.Check("check-up: two halves make one list: what both found alike is said once, and what only the server found comes under a line saying so",
                both.Lines.Count(l => l.Text == "On the server:") == 1 && noteAt > 0 && onlyAt > noteAt && both.Lines.First(l => l.Text == "On the server:").Style == DoctorLineStyle.Text
                && both.Lines.Where(l => l.Style == DoctorLineStyle.Check).GroupBy(l => l.Section + "|" + l.Text).All(g => g.Count() == 1) && both.Lines.Count(l => l.Style == DoctorLineStyle.Check) == whole.Lines.Count(l => l.Style == DoctorLineStyle.Check) + 1,
                both.Lines.Count(l => l.Style == DoctorLineStyle.Check) + " cards from two halves, " + whole.Lines.Count(l => l.Style == DoctorLineStyle.Check) + " from one; the note at " + noteAt + ", the server's card at " + onlyAt);
            int gameAt = shown.IndexOf("== The game (", StringComparison.Ordinal), mineAt = shown.IndexOf(me, StringComparison.Ordinal), restAt = shown.IndexOf("== The other mods ==", StringComparison.Ordinal);
            t.Check("check-up: the game first, then the mods with something to say, then the rest in one card", gameAt == 0 && mineAt > gameAt && (restAt < 0 || restAt > mineAt), gameAt + ", " + mineAt + ", " + restAt);
            t.Check("check-up: nothing in it needs a character the game's font may lack once it is made drawable", Undrawable(GameFont.Drawable(shown)).Length == 0, Undrawable(GameFont.Drawable(shown)));
            t.Check("check-up: a check that found nothing says so too", (shown.Contains("no two mods replacing the same thing") || shown.Contains("replaced or removed by more than one mod"))
                && (shown.Contains("have names") || shown.Contains("instead of a name")), First(shown, "names") + " / " + First(shown, "replac"));
            LogIssue issue = new LogIssue { Level = "Error", Message = "pipe network overflow in sector 7" };
            t.Check("api: a registered known problem matches", doctor.Known.Match(issue, EnumAppSide.Server)?.Id == me + ":known");
            api.Unregister(me);
            busTest = false;
            string after = doctor.BuildPage(ev, null, "en", Pages.Overview, "").PlainText(sapi.World);
            t.Check("api: unregistering takes everything of that mod away", !after.Contains("3 pipes aren't connected") && !after.Contains("A pump jammed") && doctor.TopicFor("selftest-pipes") == null
                && doctor.Known.Match(issue, EnumAppSide.Server) == null);
        }

        static bool guestTest;

        /// <summary>
        /// A mod as a guest (1.2.0): pages it writes itself over the event bus, now or later, fixes that finish later,
        /// what its copy on the player's game sends along, cards on the pages about one thing, opening the window.
        /// The "later" cases are delivered by the test itself here; the example mod does it through real game ticks.
        /// </summary>
        static void Guests(Run_ t, VintageDoctorSystem doctor, ICoreServerAPI sapi)
        {
            IDoctorApi api = DoctorApi.Get(sapi);
            if (api == null) { t.Check("guests: the doctor is there", false); return; }
            const string me = "guestmod";
            guestTest = true;
            string askedFacts = null, askedHeld = null, askedAbout = null, laterRequest = null;

            void Fill(ITreeAttribute tree, string last)
            {
                tree["texts"] = new StringArrayAttribute(new[]
                {
                    "Guest notes", DoctorReport.WarnMark + " 1 note is unread", "note 7",
                    "{{fix|guestmod:read|7|Read it}} {{fix|c:bus:guestmod:show|7|Show it}} {{open|guest-notes|all|All notes}}", last
                });
                tree.SetBytes("styles", new byte[] { 1, 5, 3, 0, 3 });
                tree.SetBytes("indents", new byte[] { 0, 0, 1, 1, 0 });
            }
            EventBusListenerDelegate topics = (string name, ref EnumHandling handling, IAttribute data) =>
            {
                if (!guestTest || !(data is ITreeAttribute tree)) return;
                ITreeAttribute list = tree.GetOrAddTreeAttribute("topics");
                ITreeAttribute notes = list.GetOrAddTreeAttribute("guest-notes");
                notes.SetString("mod", me);
                notes.SetString("question", "My guest notes are missing");
                notes.SetBool("ownHeadings", true);
                list.GetOrAddTreeAttribute("crash").SetString("question", "a name the doctor uses itself");
                // A question given as a lang key is shown in the asker's language.
                list.GetOrAddTreeAttribute("guest-keyed").SetString("question", "vintagedoctor:q-crash");
            };
            EventBusListenerDelegate page = (string name, ref EnumHandling handling, IAttribute data) =>
            {
                if (!guestTest || !(data is ITreeAttribute tree) || tree.GetString("topic") != "guest-notes") return;
                byte[] sent = (tree["clientfacts"] as ITreeAttribute)?.GetBytes(me, null);
                askedFacts = sent == null ? null : Encoding.UTF8.GetString(sent);
                askedHeld = tree.GetString("heldCode");
                if (tree.GetString("argument") == "slow")
                {
                    laterRequest = tree.GetString("request");
                    tree.SetBool("later", true);
                    tree.SetInt("seconds", 30);
                    return;
                }
                Fill(tree, "argument: " + tree.GetString("argument"));
            };
            EventBusListenerDelegate fix = (string name, ref EnumHandling handling, IAttribute data) =>
            {
                if (!guestTest || !(data is ITreeAttribute tree)) return;
                string id = tree.GetString("id");
                if (id == "guestmod:read") tree.SetString("result", "Read note " + tree.GetString("argument"));
                if (id == "guestmod:slowread") { laterRequest = tree.GetString("request"); tree.SetBool("later", true); }
            };
            EventBusListenerDelegate about = (string name, ref EnumHandling handling, IAttribute data) =>
            {
                if (!guestTest || !(data is ITreeAttribute tree) || !(tree["results"] is ITreeAttribute results)) return;
                askedAbout = tree.GetString("topic") + " " + tree.GetString("subject");
                ITreeAttribute r = results.GetOrAddTreeAttribute("guestmod:worth");
                r.SetString("level", "info");
                r.SetString("title", "Worth 3 guest notes");
                r.SetString("link", "guest-notes");
                r.SetString("linkArgument", "all");
                r.SetString("linkLabel", "All guest notes");
            };
            EventBusListenerDelegate clientFacts = (string name, ref EnumHandling handling, IAttribute data) =>
            {
                if (!guestTest || !(data is ITreeAttribute tree) || !(tree["facts"] is ITreeAttribute f)) return;
                f.SetBytes("busfacts", Encoding.UTF8.GetBytes("asked for " + tree.GetString("topic")));
                f.SetBytes("toobig", new byte[70000]);
                f.SetString("notbytes", "left out");
            };
            sapi.Event.RegisterEventBusListener(topics, 0.5, VintageDoctorSystem.TopicsEvent);
            sapi.Event.RegisterEventBusListener(page, 0.5, VintageDoctorSystem.PageEvent);
            sapi.Event.RegisterEventBusListener(fix, 0.5, VintageDoctorSystem.FixEvent);
            sapi.Event.RegisterEventBusListener(about, 0.5, VintageDoctorSystem.AboutEvent);
            sapi.Event.RegisterEventBusListener(clientFacts, 0.5, VintageDoctorSystem.ClientFactsEvent);
            doctor.ForgetBusTopics();

            Evidence ev = doctor.EvidenceNow(false);
            t.Check("guests: a page offered on the event bus is known, one with a name the doctor uses isn't", doctor.TopicFor("guest-notes")?.ModId == me && !doctor.AllTopics().Any(tp => tp.Id == "crash"),
                string.Join(",", doctor.AllTopics().Select(tp => tp.Id)));
            var ask = new DoctorContext { Doctor = doctor, Api = sapi, Side = EnumAppSide.Server, LanguageCode = "en", Ev = ev };
            string asked = doctor.CheckUp(doctor.BuildPage(ev, null, "en", Pages.Overview, ""), ev, null, "en").PlainText(sapi.World);
            t.Check("guests: it is a button on the check-up, in its mod's section", Under(asked, me).Contains("My guest notes are missing (/doctor guest-notes)"), First(asked, "guest-notes"));
            string crashWords = ask.T("q-crash");
            t.Check("guests: a question given as a lang key is shown in words, here and for a player's game", asked.Contains(crashWords + " (/doctor guest-keyed)") && !asked.Contains("vintagedoctor:q-crash")
                && VintageDoctorSystem.WordsIn("en", "vintagedoctor:q-crash") == crashWords && VintageDoctorSystem.WordsIn("en", "Plain words: kept") == "Plain words: kept", First(asked, "guest-keyed"));

            // What the player's game sends along: gathered there, packed, unpacked here, handed to the mod.
            api.RegisterClientFacts(me, (topic, arg) => Encoding.UTF8.GetBytes("table 5 for " + topic + " " + arg));
            ITreeAttribute gathered = doctor.GatherClientFacts("guest-notes", "all");
            ITreeAttribute facts = VintageDoctorSystem.FactsFromBytes(VintageDoctorSystem.FactsToBytes(gathered));
            t.Check("guests: what mods send along from the player's game is gathered, packed and unpacked; too much and the wrong kind are left out",
                facts != null && facts.Count == 2 && Encoding.UTF8.GetString(facts.GetBytes(me, null) ?? new byte[0]) == "table 5 for guest-notes all"
                && Encoding.UTF8.GetString(facts.GetBytes("busfacts", null) ?? new byte[0]) == "asked for guest-notes", facts == null ? "nothing" : facts.Count + " entries");

            Item copper = sapi.World.GetItem(new AssetLocation("game:ingot-copper"));
            ItemStack held = copper == null ? null : new ItemStack(copper);
            DoctorReport now = null;
            doctor.BuildPageThen(ev, null, "en", "guest-notes", "all", held, null, false, null, facts, false, r => now = r);
            string text = now?.PlainText(sapi.World) ?? "";
            t.Check("guests: the mod's own lines make the page, at once, under its own heading", now != null && text.Contains("== Guest notes ==") && text.Contains(DoctorReport.WarnMark + " 1 note is unread")
                && text.Contains("  note 7") && text.Contains("argument: all") && !text.Contains("My guest notes are missing") && now.Warnings == 1, text.Replace("\n", " / "));
            t.Check("guests: its buttons: a fix for the event bus on this side, one for the player's side, a link", text.Contains("Read it (/doctor fix bus:guestmod:read 7)")
                && text.Contains("Show it (.doctor fix bus:guestmod:show 7)") && text.Contains("All notes (/doctor guest-notes all)"), First(text, "Read it"));
            t.Check("guests: what the player's game sent along and what they hold reach the mod", askedFacts == "table 5 for guest-notes all" && askedHeld == "item:game:ingot-copper", askedFacts + " / " + askedHeld);

            // A page that comes later.
            DoctorReport slow = null;
            int told = 0;
            doctor.BuildPageThen(ev, null, "en", "guest-notes", "slow", null, null, false, null, null, false, r => slow = r, s => told = s);
            string firstRequest = laterRequest;
            t.Check("guests: a page that comes later isn't handed on yet, and how long it may take is passed on", slow == null && told == 30 && firstRequest != null && doctor.WaitingRequests().Contains(firstRequest),
                "told " + told + ", waiting " + string.Join(",", doctor.WaitingRequests()));
            var answer = new TreeAttribute();
            answer.SetString("request", firstRequest ?? "");
            Fill(answer, "delivered later");
            sapi.Event.PushEvent(VintageDoctorSystem.DeliverEvent, answer);
            t.Check("guests: it is handed on when the mod delivers", slow != null && slow.PlainText(sapi.World).Contains("delivered later") && answer.GetBool("accepted") && !doctor.WaitingRequests().Contains(firstRequest ?? ""));
            var again = new TreeAttribute();
            again.SetString("request", firstRequest ?? "");
            sapi.Event.PushEvent(VintageDoctorSystem.DeliverEvent, again);
            t.Check("guests: a second delivery for the same page is ignored", !again.GetBool("accepted"));
            DoctorReport never = null;
            laterRequest = null;
            doctor.BuildPageThen(ev, null, "en", "guest-notes", "slow", null, null, false, null, null, false, r => never = r);
            bool expired = laterRequest != null && doctor.ExpireNow(laterRequest);
            t.Check("guests: a mod that never delivers leaves a warning, not a page that hangs", expired && never != null && never.Warnings == 1 && never.PlainText(sapi.World).Contains("didn't answer"),
                never?.PlainText(sapi.World).Replace("\n", " / "));
            string sync = doctor.BuildPage(ev, null, "en", "guest-notes", "slow").PlainText(sapi.World);
            t.Check("guests: where nothing can wait, the page says the mod is still working", sync.Contains("is still putting its part together"), sync.Replace("\n", " / "));

            // Fixes, at once and later.
            string fixNow = null, fixLater = null;
            int fixTold = 0;
            doctor.RunFixThen("bus:guestmod:read", "7", null, "en", ev, null, o => fixNow = o);
            t.Check("guests: a fix over the event bus answers at once when it can", fixNow == "Read note 7", fixNow);
            laterRequest = null;
            doctor.RunFixThen("bus:guestmod:slowread", "8", null, "en", ev, null, o => fixLater = o, s => fixTold = s);
            bool fixWaits = fixLater == null && laterRequest != null && fixTold == 60;
            var done = new TreeAttribute();
            done.SetString("request", laterRequest ?? "");
            done.SetString("result", "Read note 8, slowly");
            sapi.Event.PushEvent(VintageDoctorSystem.DeliverEvent, done);
            t.Check("guests: a fix that needs time is waited for", fixWaits && fixLater == "Read note 8, slowly", fixLater + ", told " + fixTold);
            laterRequest = null;
            doctor.RunFixThen("bus:guestmod:slowread", "9", null, "en", ev, null, o => fixLater = o);
            t.Check("guests: a fix that is never finished says so", laterRequest != null && doctor.ExpireNow(laterRequest) && fixLater.StartsWith(DoctorReport.WarnMark), fixLater);
            t.Check("guests: where nothing can wait, the fix says the mod is working on it", doctor.RunFix("bus:guestmod:slowread", "9", null, "en", ev).Contains("working on it"));

            // Cards on the pages about one thing, from the event bus and from code.
            api.RegisterCheck(new DoctorCheck(me, me + ":about", ctx => ctx.Info("typed check about " + ctx.Subject?.Code + (ctx.ClientFacts(me) != null ? " with facts" : ""))) { Pages = new[] { "item", "craft" } });
            string item = doctor.BuildPage(ev, null, "en", Checks.TopicItem, "game:ingot-copper", facts: facts).PlainText(sapi.World);
            t.Check("guests: a mod's card on an item's page, with a link to its own page", item.Contains("What your mods say about it") && item.Contains("guestmod: Worth 3 guest notes")
                && item.Contains("All guest notes (/doctor guest-notes all)") && askedAbout == "item item:game:ingot-copper", askedAbout + " / " + First(item, "Worth"));
            t.Check("guests: a check registered for the item page runs there, with the thing and the facts", item.Contains("typed check about game:ingot-copper with facts"), First(item, "typed check"));
            string craft = doctor.BuildPage(ev, null, "en", Checks.TopicCraft, "game:ingot-copper").PlainText(sapi.World);
            t.Check("guests: and on the crafting page", craft.Contains("typed check about game:ingot-copper") && craft.Contains("Worth 3 guest notes"), First(craft, "typed check"));
            string overview = doctor.BuildPage(ev, null, "en", Pages.Overview, "").PlainText(sapi.World);
            t.Check("guests: but not on the check-up", !overview.Contains("typed check about") && !overview.Contains("Worth 3 guest notes"));

            // A page written in code with its own headings, several buttons on a line, and the block looked at.
            api.RegisterTopic(new DoctorTopic(me, "guest-typed", "A typed guest page", ctx =>
            {
                ctx.Section("My own heading");
                ctx.Write("Plain line " + ctx.LinkText("guest-notes", "all", "notes") + " " + ctx.FixText(me + ":x", "1", "do it"), 1);
                ctx.Info("looked at " + (ctx.LookedAt == null ? "nothing" : ctx.LookedAt.X + "," + ctx.LookedAt.Y + "," + ctx.LookedAt.Z));
            }) { OwnHeadings = true });
            string typed = doctor.BuildPage(ev, null, "en", "guest-typed", "", lookedAt: new Vintagestory.API.MathTools.BlockPos(1, 2, 3, 0)).PlainText(sapi.World);
            t.Check("guests: a page in code with its own heading, buttons on one line and the block looked at", typed.Contains("== My own heading ==") && !typed.Contains("A typed guest page")
                && typed.Contains("Plain line notes (/doctor guest-notes all) do it (/doctor fix guestmod:x 1)") && typed.Contains("looked at 1,2,3"), typed.Replace("\n", " / "));

            // A page that only the other side of the game has: nothing from here, and not the check-up instead.
            DoctorReport other = null;
            doctor.BuildPageThen(ev, null, "en", "only-on-the-client", "x", null, null, false, null, null, true, r => other = r);
            t.Check("guests: a page only the other side has gets nothing from this side", other != null && other.Lines.Count == 0 && other.Key == "only-on-the-client|x", other?.Lines.Count + " lines, " + other?.Key);

            // Opening the window for someone.
            var open = new TreeAttribute();
            open.SetString("topic", "guest-notes");
            open.SetString("player", "nobody");
            sapi.Event.PushEvent(VintageDoctorSystem.OpenEvent, open);
            t.Check("guests: opening the window for a player who isn't there says it couldn't", !api.OpenPage("guest-notes", "all", null) && open.TryGetBool("opened") == false);

            api.Unregister(me);
            guestTest = false;
            doctor.ForgetBusTopics();
            t.Check("guests: when the mod stops answering, its page is gone", doctor.TopicFor("guest-notes") == null && doctor.TopicFor("guest-keyed") == null && doctor.TopicFor("guest-typed") == null && doctor.GatherClientFacts("x", "") == null);
        }

        public const string SickMarker = "doctor-sick-server.marker";

        static string SickPage(VintageDoctorSystem doctor, ICoreServerAPI sapi, Evidence ev, string folder, string topic, string arg, string name)
        {
            string text = doctor.BuildPage(ev, null, "en", topic, arg).PlainText(sapi.World);
            File.WriteAllText(Path.Combine(folder, name + ".txt"), text);
            return text;
        }

        /// <summary>
        /// A real server set up by tools/ci_sick_server.py with the mods in tests/mods: a mod that fails to start, a
        /// patch and a recipe that don't fit, zips packed wrongly, a mod installed twice, a config file cut short and
        /// an old crash report. Everything the doctor says here comes from what the game itself wrote and left on disk,
        /// so this is where a wrong guess about the game's messages would show. Then the fixes are used for real.
        /// </summary>
        static void SickServer(Run_ t, VintageDoctorSystem doctor, ICoreServerAPI sapi, string folder)
        {
            Evidence ev = doctor.EvidenceNow(true);
            string overview = SickPage(doctor, sapi, ev, folder, Pages.Overview, "", "sick-overview");
            string startup = SickPage(doctor, sapi, ev, folder, Checks.TopicStartup, "", "sick-startup");
            string files = SickPage(doctor, sapi, ev, folder, Checks.TopicFiles, "", "sick-files");
            string crash = SickPage(doctor, sapi, ev, folder, Checks.TopicCrash, "", "sick-crash");
            string log = SickPage(doctor, sapi, ev, folder, Checks.TopicLog, "", "sick-log");
            string mods = SickPage(doctor, sapi, ev, folder, Checks.TopicMods, "", "sick-mods");
            string crashy = SickPage(doctor, sapi, ev, folder, Checks.TopicMod, "doctorcrashy", "sick-mod-doctorcrashy");
            SickPage(doctor, sapi, ev, folder, Pages.BugReportTopic, "", "sick-bugreport");
            string clashes = SickPage(doctor, sapi, ev, folder, Checks.TopicClashes, "", "sick-clashes");
            string flint = SickPage(doctor, sapi, ev, folder, Checks.TopicCraft, "game:flint", "sick-craft-flint");
            string checkup = doctor.CheckUp(doctor.BuildPage(ev, null, "en", Pages.Overview, ""), ev, null, "en").PlainText(sapi.World);
            File.WriteAllText(Path.Combine(folder, "sick-checkup.txt"), checkup);
            string sickMod = SickPage(doctor, sapi, ev, folder, Checks.TopicMod, "doctorsick", "sick-mod-doctorsick");

            t.Check("sick: a mod that failed to start is named, with its error", startup.Contains("Doctor Crashy threw an error while starting: Method not found"), First(startup, "threw an error"));
            t.Check("sick: the error is explained", startup.Contains("built for a different version"));
            t.Check("sick: the part of the mod the game gave up on is named", startup.Contains("Not started: ") && startup.Contains("DoctorCrashy"), First(startup, "Not started"));
            t.Check("sick: files the game couldn't load as mods, each with what it really is", startup.Contains("5 file(s) in the Mods folder aren't mods the game can load") && !startup.Contains("doctorsickstray")
                && startup.Contains("doctor-sick-straydll.zip: it has code in the wrong place (lib/Helper.dll)")
                && startup.Contains("doctor-sick-notamod.zip: it has no modinfo.json") && startup.Contains("doctor-sick-nested.zip: the mod sits one folder down")
                && startup.Contains("doctor-sick-wrapped.zip: it holds another zip") && startup.Contains("doctor-sick-corrupt.zip: the zip is damaged"), First(startup, "aren't mods"));
            t.Check("sick: the game's own notice of a mod installed twice", startup.Contains("in the Mods folder more than once") && startup.Contains("doctordup"), First(startup, "more than once"));
            t.Check("sick: a JSON patch that didn't apply, by mod", startup.Contains("JSON patch(es) didn't apply") && startup.Contains("Doctor Sick ("), First(startup, "patch"));
            t.Check("sick: a recipe naming something that doesn't exist", startup.Contains("name things that don't exist") && startup.Contains("doctorsick ("), First(startup, "don't exist"));
            t.Check("sick: backslashes in a zip", files.Contains("doctor-sick-backslash.zip was packed with"), First(files, "backslash"));
            t.Check("sick: a mod one folder down in its zip", files.Contains("doctor-sick-nested.zip has its files one folder down, in 'Nested-main'"), First(files, "nested"));
            t.Check("sick: a zip that isn't a mod", files.Contains("doctor-sick-notamod.zip isn't a mod"), First(files, "notamod"));
            t.Check("sick: a zip inside a zip", files.Contains("doctor-sick-wrapped.zip holds nothing but another zip"), First(files, "wrapped"));
            t.Check("sick: a damaged zip", files.Contains("doctor-sick-corrupt.zip can't be opened"), First(files, "corrupt"));
            t.Check("sick: an archive the game can't read", files.Contains("doctor-sick-packed.rar is an archive the game can't read"), First(files, ".rar"));
            t.Check("sick: a mod installed twice, newest first", files.Contains("doctordup is installed 2 times (1.1.0, 1.0.0)"), First(files, "doctordup"));
            t.Check("sick: a config file cut short, and whose it is", files.Contains("The config file doctorcrashy.json can't be read (most likely Doctor Crashy's)"), First(files, "doctorcrashy.json"));
            t.Check("sick: the crash report left by an earlier session", crash.Contains("The game crashed (11/21/2026 05:56:18): NullReferenceException"), First(crash, "crashed"));
            t.Check("sick: the crash is explained and its trace shown", crash.Contains("used something that wasn't there") && crash.Contains("FrameProfilerUtil.End()"));
            // Formatting tags out of place: the test mod had the game's own parser read two of doctorsick's texts.
            {
                var tagged = ev.Log?.TagErrors ?? new List<(string, string)>();
                string lever = TextTags.Errors("Pull the lever -> the gate opens")[0].message, hot = TextTags.Errors("<strong>Hot!</font> Don't touch")[0].message;   // font-ok: the test mod's text
                t.Check("sick: the game's errors about formatting tags are read from its log, and are word for word what the doctor expects", tagged.Any(e => e.Item2 == lever) && tagged.Any(e => e.Item2 == hot),
                    string.Join(" | ", tagged.Select(e => e.Item2)));
                string doctorSick = Under(checkup, "Doctor Sick");
                t.Check("sick: on the check-up they are traced to doctorsick's texts, under that mod, with the texts' keys", doctorSick.Contains("Doctor Sick's texts have formatting tags out of place: 2 error(s)")
                    && doctorSick.Contains("doctorsick:lever-help") && doctorSick.Contains("doctorsick:hot-warning") && !doctorSick.Contains("doctorsick:fine-text"), First(doctorSick, "formatting tags") + " / " + First(doctorSick, "lever-help"));
                t.Check("sick: and they aren't counted again as errors no mod could be named for", !log.Contains("Found closing tag"), First(log, "Found closing tag"));
                // Whether the game wrote the whole text to a debug log is what this run finds out: said, not asserted.
                t.Check("sick: the debug log was looked in for the texts themselves", true, ev.TagTexts.Count + " text(s) found there: " + string.Join(" | ", ev.TagTexts.Values.Take(2)));
            }
            // The same pages as the world's side of a single-player game builds them: there the logs are on the
            // player's own computer, and what speaks of one has a button that opens it.
            {
                string Built(string topic, string arg)
                {
                    var c = new DoctorContext { Doctor = doctor, Api = sapi, Side = EnumAppSide.Server, LanguageCode = "en", Topic = topic, Argument = arg, Ev = ev, SameProcess = true };
                    Pages.Build(c);
                    string text = string.Join("\n", c.R.Lines.Select(l => new string(' ', l.Indent * 2) + l.Text));
                    File.WriteAllText(Path.Combine(folder, "sick-single-player-" + topic + (arg.Length > 0 ? "-" + arg : "") + ".txt"), text);
                    return text;
                }
                const string crashButton = "{{folder|logs|server-crash.log|Open server-crash.log}}", logButton = "{{folder|logs|server-main.log|Open server-main.log}}";
                string spCrash = Built(Checks.TopicCrash, ""), spLog = Built(Checks.TopicLog, ""), spMods = Built(Checks.TopicMods, ""), spUp = Built(Pages.Overview, ""), spMod = Built(Checks.TopicMod, "doctorcrashy");
                string[] crashLines = spCrash.Split('\n');
                t.Check("sick: the crash reports' page has a button that opens server-crash.log at its top and on the latest crash", crashLines.Length > 1 && crashLines[1].Trim() == crashButton
                    && crashLines.Count(l => l.Contains(crashButton)) == 2, crashLines.Length > 1 ? crashLines[1] + " (" + crashLines.Count(l => l.Contains(crashButton)) + " in all)" : spCrash);
                t.Check("sick: the log's page has one that opens server-main.log at its top", spLog.Split('\n').Length > 1 && spLog.Split('\n')[1].Trim() == logButton, First(spLog, "{{folder"));
                t.Check("sick: on the check-up, the card for a mod's errors in the log has it next to the button for the doctor's own page of them",
                    spUp.Split('\n').Any(l => l.Contains("{{open|log|doctorcrashy|") && l.Contains(logButton)), First(spUp, "{{open|log|doctorcrashy|"));
                t.Check("sick: in the list of mods, a mod with errors in the log has it too", spMods.Split('\n').Any(l => l.Contains("{{open|mod|doctorcrashy|") && l.Contains(logButton)), First(spMods, "{{open|mod|doctorcrashy|"));
                t.Check("sick: this server's own pages, which its players would read from another computer, have none", !crash.Contains("Open server-crash.log") && !log.Contains("Open server-main.log") && !checkup.Contains("Open server-"));
            }
            t.Check("sick: the same error three times is one kind, counted against its mod", log.Contains("×3 [Error] [doctorcrashy] a made-up error") && log.Contains("Doctor Crashy: 1 kind(s) of errors"), First(log, "Doctor Crashy"));
            t.Check("sick: the mods page puts the broken mod first", mods.Contains("Doctor Crashy 1.0.0: 1 error(s) while starting"), First(mods, "Doctor Crashy"));
            t.Check("sick: the broken mod's own page has all of it and offers to switch it off", crashy.Contains("threw an error while starting") && crashy.Contains("doctorcrashy.json can't be read")
                && crashy.Contains("/doctor fix doctor:disable-mod doctorcrashy") && crashy.Contains("a made-up error"));

            // Mods in each other's way, or their own.
            bool LineHas(string text, string key, params string[] words)
            {
                foreach (string line in text.Split('\n')) if (line.Contains(key)) return words.All(w => line.Contains(w));
                return false;
            }
            string replacedFile = "";
            try { replacedFile = File.ReadAllText(Path.Combine(Vintagestory.API.Config.GamePaths.ModConfig, "doctor-sick-override.txt")).Trim(); } catch { }
            t.Check("sick: a .dll in a subfolder of a zip", files.Contains("doctor-sick-straydll.zip has code where the game doesn't accept it (lib/Helper.dll)"), First(files, "straydll"));
            t.Check("sick: a block and an item whose code is missing, the block's data code too", clashes.Contains("Doctor Sick: 2 block(s) or item(s) need code that isn't in the game")
                && clashes.Contains("doctorsick:ghost (its code: DoctorGhostItem)") && clashes.Contains("doctorsick:ghostblock (its code: DoctorGhostBlock)")
                && clashes.Contains("doctorsick:ghostblock (its data code: DoctorGhostEntity)"), First(clashes, "need code") + " / " + First(clashes, "DoctorGhostEntity"));
            t.Check("sick: a behaviour no mod has", clashes.Contains("doctorsick:ghost (a behaviour: DoctorGhostBehavior)"), First(clashes, "DoctorGhostBehavior"));
            t.Check("sick: an item two files both add", clashes.Contains("Doctor Sick: 1 block(s) or item(s) couldn't be added to the game") && clashes.Contains("doctorsick:ghost"), First(clashes, "couldn't be added"));
            t.Check("sick: an item file the game couldn't read", clashes.Contains("Doctor Sick: 1 of its files couldn't be read") && clashes.Contains("doctorsick:itemtypes/broken.json"),
                First(clashes, "couldn't be read"));
            t.Check("sick: a patch for a file the game doesn't have, and one for a mod that isn't installed", clashes.Contains("Doctor Sick tries to change 1 file(s) that aren't there")
                && clashes.Contains("game:itemtypes/doctor-no-such-file.json") && clashes.Contains("Doctor Sick has 1 patch(es) for mods that aren't installed (doctornosuchmod)"),
                First(clashes, "aren't there") + " / " + First(clashes, "aren't installed"));
            t.Check("sick: code in a mod that calls itself a content mod", LineHas(clashes, "doctorsickbundled has code in it", "0Harmony.dll", "SickShared.dll", "calls itself a content mod"), First(clashes, "has code in it"));
            t.Check("sick: a mod whose items never arrived, with the reason and the fix", clashes.Contains("doctorsickslash has 1 block and item file(s), but none of its blocks or items are in the game")
                && clashes.Contains("/doctor fix doctor:repack"), First(clashes, "none of its"));
            t.Check("sick: a library the game already has, and one that two mods bring", clashes.Contains("doctorsickbundled brings its own copy of 0Harmony.dll")
                && LineHas(clashes, "SickShared.dll is in more than one mod", "doctorsickbundled", "doctorsickshared") && clashes.Contains("/doctor fix doctor:strip-libraries"),
                First(clashes, "brings its own") + " / " + First(clashes, "more than one mod"));
            if (replacedFile.Length > 0)
            {
                t.Check("sick: two mods replacing the same files", clashes.Contains("2 file(s) are replaced by more than one mod") && LineHas(clashes, "game:" + replacedFile + ":", "Doctor Sick", "doctorsickold")
                    && LineHas(clashes, "doctorsick:config/note.json:", "Doctor Sick", "doctorsickold"), First(clashes, "replaced by more") + " / " + First(clashes, replacedFile));
                t.Check("sick: a mod made for an older game that replaces the game's own files", clashes.Contains("1 mod(s) made for an older game replace 1 of the game's own files")
                    && clashes.Contains("doctorsickold (made for 1.19.0): 1 file(s)") && clashes.Contains("Doctor Sick replaces 1 of the game's own files outright"),
                    First(clashes, "older game replace") + " / " + First(clashes, "outright"));
            }
            else t.Skip("sick: replaced files", "no file of the game's was found to replace");
            t.Check("sick: a mod with code made for an older game that has errors now", LineHas(clashes, "with code were made for an older game", "1 mod(s)", "have errors now", "Doctor Crashy (1.20.0)"),
                First(clashes, "with code were made"));
            t.Check("sick: two recipes with the same ingredients", clashes.Contains("1 recipe(s) have exactly the same ingredients as another recipe"), First(clashes, "same ingredients"));
            t.Check("sick: the same on the item's own crafting page", flint.Contains("Another recipe has exactly the same ingredients"), First(flint, "same ingredients"));
            t.Check("sick: a mod's own page has what is wrong with its files", sickMod.Contains("2 block(s) or item(s) need code that isn't in the game") && sickMod.Contains("1 of its files couldn't be read")
                && sickMod.Contains("tries to change 1 file(s) that aren't there") && sickMod.Contains("It replaces 1 of the game's own files"), First(sickMod, "need code") + " / " + First(sickMod, "replaces"));
            t.Check("sick: the check-up lists every clash check, the harmless findings too, and leads to the page", overview.Contains("need code that isn't in the game") && overview.Contains("/doctor clashes")
                && overview.Contains("brings its own copy"), First(overview, "/doctor clashes"));
            string crashyPart = Under(checkup, "Doctor Crashy"), sickPart = Under(checkup, "Doctor Sick ");
            t.Check("sick: on the check-up a mod that failed to start has its own section, with the failure and its errors in the log", crashyPart.Contains("threw an error while starting") && crashyPart.Contains("kind(s) of errors in the log")
                && crashyPart.Contains("/doctor mod doctorcrashy"), crashyPart.Length + " chars: " + First(crashyPart, "error"));
            t.Check("sick: and a mod with broken files has them under its own heading", sickPart.Contains("need code that isn't in the game") && sickPart.Contains("kind(s) of errors in the log"), First(sickPart, "need code"));
            t.Check("sick: what isn't about one mod is the game's (the crash, files that aren't mods)", Under(checkup, "The game (").Contains("The game crashed") && Under(checkup, "The game (").Contains("aren't mods the game can load"),
                First(Under(checkup, "The game ("), "crashed"));

            var counted = new DoctorReport();
            foreach (string line in overview.Split('\n')) if (line.StartsWith(DoctorReport.ProblemMark) || line.StartsWith(DoctorReport.WarnMark)) counted.Add(line, DoctorLineStyle.Check);
            t.Check("sick: the check-up counts the problems", counted.Problems >= 6 && counted.Warnings >= 5, counted.Problems + " problems, " + counted.Warnings + " warnings");

            // The fixes, on the real Mods folder.
            string Arg(string name) { ModFile f = ev.Folder.Files.FirstOrDefault(x => x.Name == name); return f == null ? "?/" + name : Checks.FileArg(ev.Folder, f); }
            string Where(string name) => ev.Folder.Files.FirstOrDefault(x => x.Name == name)?.Path ?? "";
            bool Ok(string outcome) => !outcome.StartsWith(DoctorReport.WarnMark);
            string slash = Where("doctor-sick-backslash.zip"), nested = Where("doctor-sick-nested.zip"), wrapped = Where("doctor-sick-wrapped.zip");
            string r1 = doctor.RunFix("doctor:repack", Arg("doctor-sick-backslash.zip"), null, "en", ev);
            t.Check("sick fix: the backslash zip repacked", Ok(r1) && FolderScan.Look(slash) is ModFile a && !a.Backslashes && !a.Broken, r1);
            string r2 = doctor.RunFix("doctor:repack", Arg("doctor-sick-nested.zip"), null, "en", ev);
            t.Check("sick fix: the nested zip repacked", Ok(r2) && FolderScan.Look(nested) is ModFile b && b.NestedFolder == null && b.ModId == "doctorsicknested", r2);
            string r3 = doctor.RunFix("doctor:unwrap", Arg("doctor-sick-wrapped.zip"), null, "en", ev);
            t.Check("sick fix: the inner zip taken out", Ok(r3) && !File.Exists(wrapped) && File.Exists(Path.Combine(Path.GetDirectoryName(wrapped), "doctorwrapped_1.0.0.zip")), r3);
            string r4 = doctor.RunFix("doctor:move-file", Arg("doctor-sick-notamod.zip"), null, "en", ev);
            string r5 = doctor.RunFix("doctor:move-file", Arg("doctor-sick-corrupt.zip"), null, "en", ev);
            string r6 = doctor.RunFix("doctor:move-file", Arg("doctor-sick-packed.rar"), null, "en", ev);
            t.Check("sick fix: three files that aren't mods moved aside", Ok(r4) && Ok(r5) && Ok(r6) && !File.Exists(Where("doctor-sick-notamod.zip")) && !File.Exists(Where("doctor-sick-corrupt.zip")) && !File.Exists(Where("doctor-sick-packed.rar")), r4 + " | " + r5 + " | " + r6);
            string r7 = doctor.RunFix("doctor:move-duplicates", "doctordup", null, "en", ev);
            t.Check("sick fix: the older copy moved, the newer kept", Ok(r7) && !File.Exists(Where("doctordup_1.0.0.zip")) && File.Exists(Where("doctordup_1.1.0.zip")), r7);
            string r8 = doctor.RunFix("doctor:reset-config", "doctorcrashy.json", null, "en", ev);
            t.Check("sick fix: the broken config moved aside", Ok(r8) && !File.Exists(Path.Combine(Vintagestory.API.Config.GamePaths.ModConfig, "doctorcrashy.json")), r8);
            string r9 = doctor.RunFix("doctor:move-file", Arg("doctordup_1.1.0.zip"), null, "en", ev);
            t.Check("sick fix: a mod the game has loaded is not moved", !Ok(r9) && File.Exists(Where("doctordup_1.1.0.zip")), r9);
            string bundled = Where("doctor-sick-bundled.zip");
            string r11 = doctor.RunFix("doctor:strip-libraries", Arg("doctor-sick-bundled.zip"), null, "en", ev);
            t.Check("sick fix: the game's library taken out of a mod, the mod's own files kept", Ok(r11) && FolderScan.Look(bundled) is ModFile c && c.Dlls.Count == 1 && c.Dlls[0] == "SickShared.dll" && c.ModId == "doctorsickbundled", r11);
            string r12 = doctor.RunFix("doctor:strip-libraries", Arg("doctor-sick-shared.zip"), null, "en", ev);
            t.Check("sick fix: a mod with nothing of the game's in it is left alone", !Ok(r12), r12);
            string r13 = doctor.RunFix("doctor:move-file", Arg("doctor-sick-straydll.zip"), null, "en", ev);
            t.Check("sick fix: the zip the game refuses moved aside", Ok(r13) && !File.Exists(Where("doctor-sick-straydll.zip")), r13);
            string r10 = doctor.RunFix("doctor:disable-mod", "doctorcrashy", null, "en", ev);
            t.Check("sick fix: the broken mod switched off for the next start", Ok(r10), r10);

            Evidence after = doctor.EvidenceNow(true);
            string filesAfter = SickPage(doctor, sapi, after, folder, Checks.TopicFiles, "", "sick-files-after-fixes");
            t.Check("sick: after the fixes the Mods folder and the config files are clean", filesAfter.Contains("nothing wrong with them") && filesAfter.Contains("read without a problem"), First(filesAfter, DoctorReport.ProblemMark) + First(filesAfter, DoctorReport.WarnMark));
            int kept = Directory.Exists(Path.Combine(doctor.DataFolder, "backups")) ? Directory.GetFiles(Path.Combine(doctor.DataFolder, "backups"), "*", SearchOption.AllDirectories).Length : 0;
            t.Check("sick: everything that was moved is in the backups, nothing deleted", kept == 10, kept + " files in the backups");
        }

        /// <summary>The first line of a page that has a word in it, for a failed check's detail.</summary>
        static string First(string text, string word)
        {
            foreach (string line in text.Split('\n')) if (line.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return LogScan.Shorten(line.Trim(), 260);
            return "no line with '" + word + "'";
        }

        /// <summary>The self-test's listeners on the event bus only answer while it runs (they can't be taken off again).</summary>
        static bool busTest;

        static void PagesOnRealServer(Run_ t, VintageDoctorSystem doctor, ICoreServerAPI sapi, string folder)
        {
            Evidence ev = doctor.EvidenceNow(true);
            t.Check("real: the server's log was read", ev.Log != null && ev.Log.Found && ev.Log.Error == null && ev.Log.GameVersion != null, ev.Log?.Error ?? (ev.Log?.Lines + " lines, " + ev.Log?.Issues.Count + " kinds of errors and warnings, read in " + ev.ReadMilliseconds + " ms"));
            t.Check("real: the Mods folders were looked through", ev.Folder != null && ev.Folder.Folders.Count > 0 && ev.Folder.Files.Any(f => f.ModId == DoctorApi.ModId && f.Loaded),
                ev.Folder == null ? "none" : ev.Folder.Folders.Count + " folders, " + ev.Folder.Files.Count + " files");
            t.Check("real: the doctor knows its own mod", ev.Mods.Get(DoctorApi.ModId) != null && ev.Mods.ModOfType(typeof(SelfTest).FullName) == DoctorApi.ModId
                && ev.Mods.ModOfFrame("   at VintageDoctor.SelfTest.Run(VintageDoctorSystem doctor) in x.cs:line 1") == DoctorApi.ModId && ev.Mods.ModOfFrame("   at Vintagestory.Server.ServerMain.Process()") == null);
            t.Check("real: the doctor's own list of known problems was read from its assets", doctor.Known.Sources.Any(s => s.StartsWith("vintagedoctor:")) && doctor.Known.Count > 0,
                doctor.Known.Count + " known, sources: " + string.Join(", ", doctor.Known.Sources) + (doctor.Known.LoadErrors.Count > 0 ? "; errors: " + string.Join(" | ", doctor.Known.LoadErrors) : ""));
            t.Check("real: no known-problem list failed to load", doctor.Known.LoadErrors.Count == 0, string.Join(" | ", doctor.Known.LoadErrors));

            string firstMod = ev.Mods.UserMods.FirstOrDefault(m => m.Id != DoctorApi.ModId)?.Id ?? DoctorApi.ModId;
            var pages = new List<(string topic, string arg)>
            {
                (Pages.Overview, ""), ("deep", ""), (Checks.TopicCrash, ""), (Checks.TopicStartup, ""), (Checks.TopicLog, ""), (Checks.TopicLog, firstMod), (Checks.TopicMods, ""),
                (Checks.TopicMod, DoctorApi.ModId), (Checks.TopicMod, firstMod), (Checks.TopicMod, "nosuchmod"), (Checks.TopicPerf, ""), (Checks.TopicNetwork, ""), (Checks.TopicFiles, ""), (Checks.TopicPatches, ""), (Checks.TopicClashes, ""),
                (Checks.TopicChanges, ""), (Checks.TopicWorld, ""), (Checks.TopicItem, "game:ingot-copper"), (Checks.TopicItem, "game:nosuchitem"), (Checks.TopicItem, ""), (Checks.TopicBlock, ""),
                (Checks.TopicCraft, "game:axe-copper"), (Checks.TopicCraft, "game:ingot-copper"), (Pages.BugReportTopic, ""), (Pages.BugReportTopic, firstMod), (Pages.Api, ""),
                ("example-tanks", ""), ("example-pipes", ""), ("nosuchpage", ""), (firstMod, ""), ("game:ingot-copper", "")
            };
            foreach (var (topic, arg) in pages)
            {
                string name = "page-" + topic.Replace(':', '_') + (arg.Length > 0 ? "-" + arg.Replace(':', '_') : "");
                try
                {
                    var x = new DoctorContext { Doctor = doctor, Api = sapi, Side = EnumAppSide.Server, LanguageCode = "en", Topic = topic, Argument = arg, Ev = ev };
                    Pages.Build(x);
                    string text = (x.Topic == Pages.Overview ? doctor.CheckUp(x.R, ev, null, "en") : x.R).PlainText(sapi.World);
                    File.WriteAllText(Path.Combine(folder, name + ".txt"), text);
                    // A text missing from the lang file comes back as its key.
                    bool keys = System.Text.RegularExpressions.Regex.IsMatch(text, @"vintagedoctor:[a-z]+-[a-z0-9-]+");
                    bool crashed = text.Contains("check-crashed") || text.Contains("stopped with an error");
                    t.Check("real: " + name, text.Length > 20 && !keys && !crashed, text.Length + " chars" + (keys ? ", a text is missing from the lang file" : "") + (crashed ? ", a check threw" : ""));
                }
                catch (Exception e) { t.Check("real: " + name, false, e.GetType().Name + ": " + e.Message); }
            }
            string overview = File.ReadAllText(Path.Combine(folder, "page-overview.txt"));
            t.Check("real: the check-up has cards and buttons", overview.Contains(DoctorReport.OkMark) && overview.Contains("/doctor crash") && overview.Contains("/doctor bugreport"));
            t.Check("real: what used to be the deeper checks is in it (patches of two mods on one thing, things without a name, every zip read to its end)",
                (overview.Contains("no two mods replacing the same thing") || overview.Contains("replaced or removed by more than one mod")) && (overview.Contains("have names") || overview.Contains("instead of a name"))
                && ev.Folder.Files.Any(f => f.Tested) && ev.Folder.Files.Where(f => f.Kind == "zip" && f.ReadError == null).All(f => f.Tested),
                First(overview, "names") + " / " + First(overview, "replac") + " / " + ev.Folder.Files.Count(f => f.Tested) + " zips read to the end");
            string bug = File.ReadAllText(Path.Combine(folder, "page-bugreport.txt"));
            t.Check("real: the bug report has the mod list and no addresses", bug.Contains("Mods (") && bug.Contains("Findings") && !System.Text.RegularExpressions.Regex.IsMatch(bug, @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}:\d+"));
            t.Check("real: the bug report was saved", File.Exists(Path.Combine(doctor.DataFolder, "bug-report-server.txt")));
        }

        /// <summary>
        /// A place for code patches to meet, for the test run only: the example mod patches it (its code runs after
        /// it) and the self-test patches it too (before it), so the page about code patches has a real place that
        /// two mods share to show.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static int SharedPlace() => 1;
        public static void BeforeSharedPlace() { }

        /// <summary>
        /// Wherever a page speaks of a log there is a button that opens it (1.4.0): the file where one log is meant,
        /// the Logs folder for the logs as a whole. Only on the player's own computer: a server that is another
        /// computer (as this test's is) has nothing the player could open, so the pages are built here as single
        /// player would build them, too.
        /// </summary>
        static void OpeningLogs(Run_ t, VintageDoctorSystem doctor, ICoreServerAPI sapi, string folder)
        {
            Evidence ev = doctor.EvidenceNow(false);
            DoctorContext New(EnumAppSide side, bool sameComputer, string topic) => new DoctorContext { Doctor = doctor, Api = sapi, Side = side, LanguageCode = "en", Topic = topic, Ev = ev, SameProcess = sameComputer };
            // The files a line's buttons open ("folder" for the Logs folder itself).
            IEnumerable<string> Opens(DoctorLine l) => DoctorReport.ActionToken.Matches(l.Text ?? "").Cast<System.Text.RegularExpressions.Match>()
                .Where(m => m.Groups[1].Value == "folder" && m.Groups[2].Value == DoctorReport.LogsFolder).Select(m => m.Groups[3].Value.Length == 0 ? "folder" : m.Groups[3].Value);
            // What the buttons of a card (or of a line by itself) open: the line with these words, and what is under it.
            string Of(DoctorContext c, string words)
            {
                int i = c.R.Lines.FindIndex(l => (l.Text ?? "").Contains(words));
                if (i < 0) return "(no line says '" + words + "')";
                var found = new List<string>();
                for (int k = i; k < c.R.Lines.Count; k++)
                {
                    DoctorLine l = c.R.Lines[k];
                    bool buttonsOnly = DoctorReport.ActionToken.Replace(l.Text ?? "", "").Trim().Length == 0;
                    if (k > i && (l.Style == DoctorLineStyle.Heading || !(l.Indent > 0 || l.Style == DoctorLineStyle.Note || buttonsOnly))) break;
                    found.AddRange(Opens(l));
                }
                return string.Join("+", found.OrderBy(f => f, StringComparer.Ordinal));
            }
            bool Offers(DoctorContext c) => c.R.Lines.Any(l => (l.Text ?? "").Contains("{{folder|"));

            t.Check("logs: only a plain log or text file's name is ever opened", DoctorReport.OpenableFile("client-main.log") && DoctorReport.OpenableFile("server-main-2.log") && DoctorReport.OpenableFile("notes.txt")
                && !DoctorReport.OpenableFile("../client-main.log") && !DoctorReport.OpenableFile("a/b.log") && !DoctorReport.OpenableFile("a\\b.log") && !DoctorReport.OpenableFile("evil.exe")
                && !DoctorReport.OpenableFile("x.log.exe") && !DoctorReport.OpenableFile(".log") && !DoctorReport.OpenableFile("") && !DoctorReport.OpenableFile(null) && !DoctorReport.OpenableFile("a b.log") && !DoctorReport.OpenableFile("C:x.log"));

            // A page put together by hand, with every way a log is spoken of.
            void Write(DoctorContext x)
            {
                x.Section(x.T("sec-log"));
                x.Line(x.T("log-summary", 1, 2, 3, 4, x.MainLogName), DoctorLineStyle.Note);
                x.Warn(x.T("log-errors", 2, 5, 1));
                x.Explain(x.T("log-errors-why"));
                x.LinkButton(Checks.TopicLog, "", x.T("btn-log"));
                x.Ok(x.T("crash-none"));
                x.Info("A mod's card: see server-debug.log, and client-main.log on your side");
                x.Info("An oak log is worth nothing, the catalogue is long and so is the backlog");
                x.Warn("Some Mod 1.0: " + x.T("mods-crashes", 2) + ", " + x.T("mods-errors", 3));
                x.Info("A card that has its button already");
                x.Detail("from its author, who names " + x.MainLogName);
                x.LogButton();
                Pages.LogButtons(x);
            }
            // Whether a log's file is there decides between the file and its folder: here, first, they all are.
            System.Func<string, bool> real = Pages.LogFileExists;
            try
            {
                Pages.LogFileExists = _ => true;
                DoctorContext here = New(EnumAppSide.Server, true, Checks.TopicLog);
                Write(here);
                File.WriteAllText(Path.Combine(folder, "log-buttons.txt"), string.Join("\n", here.R.Lines.Select(l => new string(' ', l.Indent * 2) + l.Text)));
                t.Check("logs: the log's own page has one button for its file, under its heading, however often the page names it",
                    Of(here, x_("sec-log")) == "server-main.log" && here.R.Lines.Count > 1 && Opens(here.R.Lines[1]).SequenceEqual(new[] { "server-main.log" }), Of(here, x_("sec-log")));
                t.Check("logs: a card that speaks of the log gets a button that opens it, next to the buttons it has",
                    Of(here, "kind(s) of errors in the log this session") == "server-main.log" && here.R.Lines.Any(l => l.Text.Contains("{{open|log|") && l.Text.Contains("{{folder|logs|server-main.log|Open server-main.log}}")),
                    Of(here, "kind(s) of errors in the log this session"));
                t.Check("logs: where it is the logs as a whole, or a file that may not be there, the button opens the Logs folder", Of(here, "No crash reports") == "folder"
                    && here.R.Lines.Any(l => l.Text.Contains("{{folder|logs||Open the Logs folder}}")), Of(here, "No crash reports"));
                t.Check("logs: a log named by its file gets a button whoever wrote the line, and a wooden log gets none", Of(here, "A mod's card") == "client-main.log+server-debug.log" && Of(here, "An oak log") == "",
                    Of(here, "A mod's card") + " / " + Of(here, "An oak log"));
                t.Check("logs: a card about a mod's crashes and its errors opens the crash reports and the log", Of(here, "Some Mod 1.0") == "server-crash.log+server-main.log", Of(here, "Some Mod 1.0"));
                t.Check("logs: a card that has the button already keeps the one", Of(here, "A card that has its button already") == "server-main.log", Of(here, "A card that has its button already"));
                string once = string.Join("\n", here.R.Lines.Select(l => l.Text));
                Pages.LogButtons(here);
                t.Check("logs: looking twice adds nothing", string.Join("\n", here.R.Lines.Select(l => l.Text)) == once);
                DoctorContext away = New(EnumAppSide.Server, false, Checks.TopicLog);
                Write(away);
                t.Check("logs: a server that is another computer offers no buttons for its files", !Offers(away), string.Join(" | ", away.R.Lines.Where(l => l.Text.Contains("{{folder|")).Select(l => l.Text).Take(3)));
                DoctorContext game = New(EnumAppSide.Client, false, Checks.TopicLog);
                Write(game);
                t.Check("logs: the player's game opens its own log, and not a server's by name when that server is elsewhere",
                    Of(game, "kind(s) of errors in the log this session") == "client-main.log" && Of(game, "A mod's card") == "client-main.log" && Of(game, "Some Mod 1.0") == "client-crash.log+client-main.log",
                    Of(game, "kind(s) of errors in the log this session") + " / " + Of(game, "A mod's card") + " / " + Of(game, "Some Mod 1.0"));
                // A file that isn't there: its folder instead, and no button at all for it next to a file that is.
                Pages.LogFileExists = name => name == "server-main.log";
                DoctorContext some = New(EnumAppSide.Server, true, Checks.TopicLog);
                Write(some);
                t.Check("logs: a log that was never written (no crash yet) isn't offered: the folder is, or the file that is there", Of(some, "Some Mod 1.0") == "server-main.log" && Of(some, "A mod's card") == "folder"
                    && Of(some, "kind(s) of errors in the log this session") == "server-main.log", Of(some, "Some Mod 1.0") + " / " + Of(some, "A mod's card") + " / " + Of(some, "kind(s) of errors in the log this session"));
            }
            finally { Pages.LogFileExists = real; }
            bool crashed = File.Exists(Path.Combine(GamePaths.Logs, "server-crash.log"));

            // The real pages, as the world's side of a single-player game builds them, and as this server does.
            foreach (bool sp in new[] { true, false })
            {
                var built = new Dictionary<string, DoctorContext>();
                foreach (string topic in new[] { Checks.TopicLog, Checks.TopicCrash, Checks.TopicStartup, Checks.TopicMods, Pages.Overview })
                {
                    DoctorContext c = New(EnumAppSide.Server, sp, topic);
                    Pages.Build(c);
                    built[topic] = c;
                    if (sp) File.WriteAllText(Path.Combine(folder, "single-player-" + topic + ".txt"), string.Join("\n", c.R.Lines.Select(l => new string(' ', l.Indent * 2) + l.Text)));
                }
                if (!sp)
                {
                    t.Check("logs: on this server, which is another computer to its players, the real pages offer none either", !built.Values.Any(Offers),
                        string.Join(" | ", built.Values.SelectMany(c => c.R.Lines).Where(l => l.Text.Contains("{{folder|")).Select(l => l.Text).Take(3)));
                    continue;
                }
                t.Check("logs: the real page of the log has the button for server-main.log at its top", Of(built[Checks.TopicLog], x_("sec-log")) == "server-main.log", Of(built[Checks.TopicLog], x_("sec-log")));
                t.Check("logs: the real page of crash reports has the one for server-crash.log, or for the folder when there never was a crash",
                    crashed ? Of(built[Checks.TopicCrash], x_("sec-crash")) == "server-crash.log" && Of(built[Checks.TopicCrash], "The game crashed").Contains("server-crash.log")
                        : Of(built[Checks.TopicCrash], x_("sec-crash")) == "folder" && Of(built[Checks.TopicCrash], "No crash reports") == "folder",
                    Of(built[Checks.TopicCrash], x_("sec-crash")) + " / " + Of(built[Checks.TopicCrash], "No crash reports") + " / " + Of(built[Checks.TopicCrash], "The game crashed"));
                t.Check("logs: the real page of the start-up, which is read from the log, has it too", Of(built[Checks.TopicStartup], x_("sec-startup")) == "server-main.log", Of(built[Checks.TopicStartup], x_("sec-startup")));
                // Every card of the check-up that says "the log" or "crash report" has a button for one or the other.
                DoctorContext up = built[Pages.Overview];
                var without = new List<string>();
                int spoken = 0;
                for (int i = 0; i < up.R.Lines.Count; i++)
                {
                    DoctorLine l = up.R.Lines[i];
                    if (l.Style != DoctorLineStyle.Check || l.Indent != 0) continue;
                    string title = DoctorReport.ActionToken.Replace(l.Text ?? "", "");
                    if (!System.Text.RegularExpressions.Regex.IsMatch(title, @"\bin the log\b|\b[Cc]rash reports?\b")) continue;
                    spoken++;
                    bool has = false;
                    for (int k = i; k < up.R.Lines.Count && (k == i || up.R.Lines[k].Indent > 0 || up.R.Lines[k].Style == DoctorLineStyle.Note); k++) has |= Opens(up.R.Lines[k]).Any();
                    if (!has) without.Add(title);
                }
                t.Check("logs: on the real check-up every card that speaks of the log or of crash reports has a button for it", spoken > 0 && without.Count == 0, spoken + " such card(s); without: " + string.Join(" | ", without.Take(4)));
            }

            // Both sides of a single-player game find "no errors in the log": said once, with a button for each side's log.
            var mine = new DoctorReport();
            var worlds = new DoctorReport();
            void Clean(DoctorReport r, string file)
            {
                r.Lines.Add(new DoctorLine { Text = DoctorReport.OkMark + " No errors in the log this session.", Style = DoctorLineStyle.Check, Section = DoctorReport.GameSection });
                r.Lines.Add(new DoctorLine { Text = DoctorReport.FileIn(DoctorReport.LogsFolder, file, "Open " + file), Style = DoctorLineStyle.Text, Indent = 1, Section = DoctorReport.GameSection });
            }
            Clean(mine, "client-main.log");
            Clean(worlds, "server-main.log");
            var both = new DoctorReport();
            var bothX = new DoctorContext { Doctor = doctor, Api = sapi, Side = EnumAppSide.Server, LanguageCode = "en", Topic = Pages.Overview, Ev = ev, R = both };
            Pages.Arrange(bothX, new List<(DoctorReport half, string note)> { (mine, null), (worlds, "From the world's side:") }, null, true, true);
            string buttons = both.Lines.FirstOrDefault(l => l.Text.Contains("|client-main.log|"))?.Text ?? "";
            t.Check("logs: what both sides found alike is one card with a button for each side's log", both.Lines.Count(l => l.Text.Contains("No errors in the log this session.")) == 1
                && buttons.Contains("|server-main.log|") && !both.Lines.Any(l => l.Text == "From the world's side:"), both.Lines.Count(l => l.Text.Contains("No errors in the log this session.")) + " card(s): " + buttons);

            string x_(string key) => Vintagestory.API.Config.Lang.GetL("en", "vintagedoctor:" + key);
        }

        /// <summary>
        /// Formatting tags out of place (1.5.0): the game's errors read the way the game writes them, and traced to
        /// the text, and so the mod, they came from. The sick server has the real thing (a test mod makes the game's
        /// own parser write them).
        /// </summary>
        static void FormattingTags(Run_ t, VintageDoctorSystem doctor, ICoreServerAPI sapi, string folder)
        {
            string Msgs(string text) => string.Join(" | ", TextTags.Errors(text).Select(e => e.message));
            const string tail = " See debug log for full text.";
            t.Check("tags: a > on its own is found where the game finds it, in the game's words", Msgs("Pull the lever -> the gate opens")
                == "Found closing tag char > but no tag was opened at 16. Use &gt;/&lt; if you want to display them as plain characters." + tail, Msgs("Pull the lever -> the gate opens"));
            t.Check("tags: a closing tag that doesn't match the one open", Msgs("<strong>Hot!</font> Don't touch") == "Found closing tag <font> at position 18 but <strong> should be closed first." + tail, Msgs("<strong>Hot!</font> Don't touch"));
            t.Check("tags: a closing tag never opened", Msgs("done</i>") == "Found closing tag <i> at position 7 but it was never opened." + tail, Msgs("done</i>"));
            t.Check("tags: what the game reads fine is fine", Msgs("A <strong>proper</strong> text, &gt; written out<br><icon name=x/> and <font color=\"#fff\">this</font>") == "", Msgs("A <strong>proper</strong> text"));
            t.Check("tags: as in the game, a > on its own then counts as a tag left open", Msgs("a > b </x>") == "Found closing tag char > but no tag was opened at 2. Use &gt;/&lt; if you want to display them as plain characters." + tail
                + " | Found closing tag <x> at position 9 but <> should be closed first." + tail, Msgs("a > b </x>"));
            t.Check("tags: an error's kind and place are read back from its words", TextTags.Kind(TextTags.Errors("x -> y")[0].message) == ">" && TextTags.Position(TextTags.Errors("x -> y")[0].message) == 3
                && TextTags.Kind(TextTags.Errors("<b>x</i>")[0].message) == "</i><b>" && !TextTags.IsTagError("Found closing tags everywhere") && TextTags.IsTagError(TextTags.Errors("x -> y")[0].message));
            string fixture = "1.1.2026 10:00:00 [Error] " + TextTags.Errors("x -> y")[0].message + "\n1.1.2026 10:00:01 [Error] " + TextTags.Errors("x -> y")[0].message + "\n1.1.2026 10:00:02 [Error] [somemod] a real error\n";
            LogScan scanned = LogScan.FromText(fixture, doctor.Snapshot());
            t.Check("tags: the log reader sets the game's tag errors aside, each time, and doesn't count them among the errors", scanned.TagErrors.Count == 2 && scanned.Issues.Count == 1, scanned.TagErrors.Count + " / " + scanned.Issues.Count);

            // Whose they are: made-up texts of made-up mods, and made-up errors in the log.
            string Run(string[] errors, Dictionary<string, string> shown, params (string key, string text)[] texts)
            {
                var ev = new Evidence { Side = EnumAppSide.Server, Mods = doctor.Snapshot(), Log = LogScan.FromText("", doctor.Snapshot()) };
                foreach (string e in errors) ev.Log.TagErrors.Add(("1.1.2026 10:00:00", e));
                if (shown != null) ev.TagTexts = shown;
                var x = new DoctorContext { Doctor = doctor, Api = sapi, Side = EnumAppSide.Server, LanguageCode = "en", Topic = Checks.TopicClashes, Ev = ev, ListAll = true };
                Checks.TextTagsCheck(x, true, null, TextTags.Scan(texts.Select(p => new KeyValuePair<string, string>(p.key, p.text))));
                return x.R.PlainText(sapi.World);
            }
            string err = TextTags.Errors("Press it -> done")[0].message;
            string exact = Run(new[] { err, err }, null, ("alpha:help", "Press it -> done"), ("alpha:other", "a <b>x</i>"), ("beta:fine", "all good"));
            t.Check("tags: errors that fit one mod's text word for word are that mod's, with the text's key and what is wrong",
                exact.Contains("alpha's texts have formatting tags out of place: 2 error(s) about them in the log this session") && exact.Contains("alpha:help: a > with no < before it, at character 10: Press it -> done")
                && exact.Contains("Also alpha:other: </i> while <b> is still open") && !exact.Contains("beta"), exact.Replace("\n", " / "));
            string moved = Run(new[] { TextTags.Errors("Name: Press it -> done")[0].message }, null, ("alpha:help", "Press it -> done"));
            t.Check("tags: the same mistake at another place (something was put in front) is that mod's too, as likely", moved.Contains("alpha's texts are the likely cause of 1 error(s)") && moved.Contains("(likely"), moved.Replace("\n", " / "));
            string shownIn = Run(new[] { err }, new Dictionary<string, string> { [err] = "Press it -> done" }, ("alpha:help", "Press it -> done"), ("gamma:same", "Press it -> done!"));
            t.Check("tags: with the text the debug log held, the one text it is decides", shownIn.Contains("alpha's texts have formatting tags out of place") && !shownIn.Contains("gamma's texts have"), shownIn.Replace("\n", " / "));
            string nobody = Run(new[] { err }, new Dictionary<string, string> { [err] = "Press it -> done" });
            t.Check("tags: an error no mod's text fits is said as such, with the text shown and where to find it", nobody.Contains("1 error(s) about formatting tags in the log this session that no mod could be named for")
                && nobody.Contains("Press it -> done") && nobody.Contains("server-debug.log"), nobody.Replace("\n", " / "));
            string quiet = Run(new string[0], null, ("alpha:help", "Press it -> done"));
            t.Check("tags: a mod's texts that would make the error, with none in the log, are something to know", quiet.Contains("• alpha: 1 of its texts have formatting tags out of place") && !quiet.Contains("⚠"), quiet.Replace("\n", " / "));
            string clean = Run(new string[0], null, ("alpha:help", "all good"));
            t.Check("tags: nothing out of place, and the check-up says so", clean.Contains("No formatting tags out of place"), clean.Replace("\n", " / "));
            // A command's help with a tag left open: the game wraps the help in its own formatting, so "<id>" there
            // makes "</font> ... but <id> should be closed first" (seen in a real game with the doctor's own .doctor help, 1.6.1).
            t.Check("tags: a tag left open is found", TextTags.Unclosed("mod <id>, log [what]").SequenceEqual(new[] { "id" }) && TextTags.Unclosed("a <b>x</b>").Count == 0 && TextTags.Errors("mod <id>, log").Count == 0);
            string help = "A test command: /doctortagtest mod " + "<" + "id" + ">";
            sapi.ChatCommands.Create("doctortagtest").WithDescription(help).RequiresPrivilege(Privilege.controlserver).HandleWith(_ => TextCommandResult.Success());
            {
                var ev = new Evidence { Side = EnumAppSide.Server, Mods = doctor.Snapshot(), Log = LogScan.FromText("", doctor.Snapshot()) };
                ev.Log.TagErrors.Add(("1.1.2026 10:00:00", "Found closing tag <font> at position 5366 but <id> should be closed first. See debug log for full text."));
                var x = new DoctorContext { Doctor = doctor, Api = sapi, Side = EnumAppSide.Server, LanguageCode = "en", Topic = Checks.TopicClashes, Ev = ev, ListAll = true };
                Checks.TextTagsCheck(x, true, null, TextTags.CommandProblems(sapi));
                string page = x.R.PlainText(sapi.World);
                t.Check("tags: an error from a command's help is traced to that command", page.Contains("the help of the command /doctortagtest: <id> is opened and never closed") && page.Contains("(likely"), page.Replace("\n", " / "));
                t.Check("tags: the doctor's own commands' help has no tag left open", !TextTags.CommandProblems(sapi).Any(b => b.Command == "/doctor"), string.Join(" | ", TextTags.CommandProblems(sapi).Select(b => b.Command)));
            }
            // The chat window (1.7.0): the game draws a tab's lines as one text, so one line with a tag left open or a
            // stray closing tag upsets all of it. The lines here are made up; on the player's game they are the chat's own.
            {
                var lines = new List<(int, IList<string>)> { (0, new List<string> { "Hello there", "<strong>Bob:</strong> fine", "A mod says <font color=\"#f00\">red forever", "x -> y" }), (1, new List<string> { "<i>ok</i>" }) };
                List<Checks.ChatBreak> breaks = Checks.ChatBreaks(lines);
                t.Check("chat: the lines that break the chat are found (a tag left open, a stray >), and the fine ones aren't", breaks.Count == 2 && breaks.Any(b => b.Open.SequenceEqual(new[] { "font" })) && breaks.Any(b => b.Message != null && b.Text == "x -> y"),
                    string.Join(" | ", breaks.Select(b => b.Text)));
                var x = new DoctorContext { Doctor = doctor, Api = sapi, Side = EnumAppSide.Client, LanguageCode = "en", Topic = Pages.Overview, Ev = doctor.EvidenceNow(false), ListAll = true };
                Checks.Chat(x, false, breaks);
                string card = x.R.PlainText(sapi.World);
                t.Check("chat: the card says how many lines, shows them, and offers to clear the chat and to copy the lines", card.Contains("2 line(s) in the chat have formatting tags out of place")
                    && card.Contains("red forever") && card.Contains("Clear the chat") && card.Contains("doctor:clear-chat") && card.Contains("Copy the lines"), card.Replace("\n", " / "));
                var none = new DoctorContext { Doctor = doctor, Api = sapi, Side = EnumAppSide.Client, LanguageCode = "en", Topic = Pages.Overview, Ev = doctor.EvidenceNow(false), ListAll = true };
                Checks.Chat(none, false, Checks.ChatBreaks(new List<(int, IList<string>)> { (0, new List<string> { "<i>ok</i>", "plain" }) }));
                t.Check("chat: a chat without such lines is said to be fine", none.R.PlainText(sapi.World).Contains("The chat's lines have no formatting tags out of place."));
            }
            // The real texts of the mods on this server.
            List<BrokenText> real = TextTags.LangProblems();
            File.WriteAllText(Path.Combine(folder, "formatting-tags-in-lang.txt"), string.Join("\n", real.Select(b => b.Key + " | " + b.Message + " | " + b.Text.Replace("\n", "\\n"))));
            t.Check("tags: every text of the game and its mods was read", real != null, real.Count + " text(s) with tags out of place, by domain: " + string.Join(", ", real.GroupBy(b => b.Domain).Select(g => g.Key + " " + g.Count())));
        }

        /// <summary>
        /// A mod that closes the game on purpose says so first (1.6.0): the doctor notes it at once, and the next start
        /// says who closed it and why instead of "didn't close properly". Undone afterwards, so this run's own record stays true.
        /// </summary>
        static void AnnouncedClose(Run_ t, VintageDoctorSystem doctor, ICoreServerAPI sapi)
        {
            string file = Path.Combine(doctor.DataFolder, "state-server.json");
            var tree = new TreeAttribute();
            tree.SetString("mod", "vintageautoupdate");
            tree.SetString("reason", "restarting to install 3 mod update(s)");
            sapi.Event.PushEvent(VintageDoctorSystem.ClosingEvent, tree);
            SessionRecord now = DoctorState.Load(file).Sessions.LastOrDefault();
            t.Check("closing: a mod's word that it is closing the game is noted in this session's record at once", tree.GetBool("noted") && now?.ClosedBy == "vintageautoupdate" && now.ClosedWhy == "restarting to install 3 mod update(s)" && !now.CleanExit,
                now == null ? "no record" : now.ClosedBy + " / " + now.ClosedWhy);
            SessionRecord before = doctor.PreviousSession;
            try
            {
                doctor.PreviousSession = new SessionRecord { Time = "2026-11-24 17:00", CleanExit = false, ClosedBy = "vintageautoupdate", ClosedWhy = "restarting to install 3 mod update(s)" };
                Evidence ev = doctor.EvidenceNow(false);
                string crash = doctor.BuildPage(ev, null, "en", Checks.TopicCrash, "").PlainText(sapi.World);
                t.Check("closing: at the next start it is said as on purpose, by whom and why, and not as the game not closing properly",
                    crash.Contains("closed the game last time (2026-11-24 17:00), on purpose: restarting to install 3 mod update(s)") && !crash.Contains("didn't close properly"), First(crash, "closed the game"));
            }
            finally { doctor.PreviousSession = before; }
            // This run isn't closing on purpose.
            doctor.ForgetClosing();
            t.Check("closing: taken back, the record is as it was", DoctorState.Load(file).Sessions.LastOrDefault()?.ClosedBy == null);
        }

        /// <summary>Where the window opens when another mod asks, and which sections of the check-up start folded (1.5.0).</summary>
        static void WindowRules(Run_ t)
        {
            t.Check("window: a mod's key or command with nothing more said opens the check-up, not the mod's page", VintageDoctorSystem.Landing("emc", "") == Pages.Overview && VintageDoctorSystem.Landing("farrelief", " ") == Pages.Overview);
            t.Check("window: asked for something in particular, it opens that", VintageDoctorSystem.Landing("emc", "machines") == "emc" && VintageDoctorSystem.Landing("crash", "") == "crash" && VintageDoctorSystem.Landing("mods", "") == "mods");
            t.Check("window: a mod's section starts folded when nothing in it needs attention, and open when something does; the game's starts open",
                GuiDialogDoctor.FoldedAtFirst("carryon", 0) && !GuiDialogDoctor.FoldedAtFirst("carryon", 2) && !GuiDialogDoctor.FoldedAtFirst(DoctorReport.GameSection, 0) && GuiDialogDoctor.FoldedAtFirst(DoctorReport.RestSection, 0));
            t.Check("window: a section is coloured, and marked in the list of mods, by the worst in it", GuiDialogDoctor.Worst(1, 3) == 2 && GuiDialogDoctor.Worst(0, 3) == 1 && GuiDialogDoctor.Worst(0, 0) == 0
                && GuiDialogDoctor.ListName("Carry On 1.14.3", 1, 0) == "[x] Carry On 1.14.3" && GuiDialogDoctor.ListName("Carry On 1.14.3", 0, 2) == "[!] Carry On 1.14.3" && GuiDialogDoctor.ListName("Carry On 1.14.3", 0, 0) == "Carry On 1.14.3");
        }

        /// <summary>
        /// The page about code patches says which places more than one mod changes, which mods, and what each does
        /// there (1.4.0). With made-up patches here; the example mods' test has a real one.
        /// </summary>
        static void SharedPatches(Run_ t, VintageDoctorSystem doctor, ICoreServerAPI sapi)
        {
            Evidence ev = doctor.EvidenceNow(false);
            DoctorContext New() => new DoctorContext { Doctor = doctor, Api = sapi, Side = EnumAppSide.Server, LanguageCode = "en", Topic = Checks.TopicPatches, Ev = ev };
            var hurt = new Checks.PatchedMethod { Name = "EntityPlayer.OnHurt" };
            hurt.Add("alpha", 'b'); hurt.Add("beta", 'a');
            var tick = new Checks.PatchedMethod { Name = "BlockEntityChest.OnTick" };
            tick.Add("alpha", 'r'); tick.Add("beta", 'r'); tick.Add("beta", 'a');
            var three = new Checks.PatchedMethod { Name = "Thing.Done" };
            three.Add("alpha", 'a'); three.Add("beta", 'e'); three.Add("gamma", 'a');
            var alone = new Checks.PatchedMethod { Name = "Alone.Here" };
            alone.Add("gamma", 'a');
            DoctorContext x = New();
            Checks.CodePatches(x, true, new List<Checks.PatchedMethod> { hurt, tick, three, alone });
            string page = x.R.PlainText(sapi.World);
            t.Check("patches: how many mods change the game's code, and in how many places", page.Contains("3 mod(s) change the game's code in 4 place(s)") && page.Contains("alpha (3)") && page.Contains("gamma (2)"), First(page, "mod(s) change"));
            t.Check("patches: a place two mods change is named, with the mods and what each does there",
                page.Contains("alpha and beta change the same 2 place(s) in the game's code") && page.Contains("EntityPlayer.OnHurt: alpha (runs before it), beta (runs after it)")
                && page.Contains("BlockEntityChest.OnTick: alpha (rewrites it), beta (rewrites it, runs after it)"), page.Replace("\n", " / "));
            t.Check("patches: three mods at one place are named too", page.Contains("alpha, beta and gamma change the same 1 place(s) in the game's code")
                && page.Contains("Thing.Done: alpha (runs after it), beta (steps in when it ends or fails), gamma (runs after it)"), First(page, "Thing.Done"));
            t.Check("patches: two mods rewriting the same piece of code is a warning, the rest is to know", x.R.Lines.Any(l => l.Text.StartsWith(DoctorReport.WarnMark, StringComparison.Ordinal) && l.Text.Contains("alpha and beta"))
                && x.R.Lines.Any(l => l.Text.StartsWith(DoctorReport.InfoMark, StringComparison.Ordinal) && l.Text.Contains("alpha, beta and gamma")) && x.R.Warnings == 1);
            DoctorContext none = New();
            none.ListAll = true;
            Checks.CodePatches(none, false, new List<Checks.PatchedMethod> { alone });
            t.Check("patches: when no two mods meet, the check-up says so", none.R.PlainText(sapi.World).Contains("No two mods change the same place in the game's code."), none.R.PlainText(sapi.World).Replace("\n", " / "));
        }

        /// <summary>The two example mods from the repository's example folder, when the test run loaded them: the API as a real mod uses it.</summary>
        static void Examples(Run_ t, VintageDoctorSystem doctor, ICoreServerAPI sapi, string folder)
        {
            Evidence ev = doctor.EvidenceNow(false);
            if (!ev.Mods.Has("doctorexample") || !ev.Mods.Has("doctorexamplejson")) { t.Skip("the example mods", "not loaded in this run"); return; }
            DoctorReport exampleHalf = doctor.BuildPage(ev, null, "en", Pages.Overview, "");
            string overview = exampleHalf.PlainText(sapi.World);
            string byMod = doctor.CheckUp(exampleHalf, ev, null, "en").PlainText(sapi.World);
            string own = Under(byMod, "Doctor Example 1");
            t.Check("example: on the check-up the mod has a section of its own: its cards without its name in front, the buttons for its two pages, and one for everything about it",
                own.Split('\n').Any(l => l.Length > 2 && l.Substring(2).StartsWith("3 example pumps are jammed", StringComparison.Ordinal)) && own.Contains("(/doctor example-tanks)") && own.Contains("(/doctor example-pipes)")
                && own.Contains("Everything about Doctor Example (/doctor mod doctorexample)") && !Under(byMod, "The game (").Contains("example-tanks"), First(own, "pumps") + " / " + First(own, "example-tanks"));
            t.Check("example: a mod's check, registered without the doctor being a requirement", overview.Contains("3 example pumps are jammed") && overview.Contains("/doctor fix doctorexample:unjam all"));
            t.Check("example: the check can read the log through the doctor", overview.Contains("message(s) from this mod in the log"));
            t.Check("example: a finding over the event bus, and an incident", overview.Contains("2 example tanks are half empty") && overview.Contains("The example mod started"));
            t.Check("example: a known problem and a clash from a JSON file, with no code", overview.Contains("An example pump jammed") && overview.Contains("Doctor Example (JSON) and Doctor Example don't get on")
                && overview.Contains("/doctor fix doctorexamplejson:pump-jammed/fix"));
            string modPage = doctor.BuildPage(ev, null, "en", Checks.TopicMod, "doctorexample").PlainText(sapi.World);
            File.WriteAllText(Path.Combine(folder, "page-mod-doctorexample.txt"), modPage);
            t.Check("example: the mod's own page has its check, its event-bus finding and its log lines", modPage.Contains("example pumps are jammed") && modPage.Contains("example tanks are half empty") && modPage.Contains("pump 3 jammed"));
            string jsonPage = doctor.BuildPage(ev, null, "en", Checks.TopicMod, "doctorexamplejson").PlainText(sapi.World);
            File.WriteAllText(Path.Combine(folder, "page-mod-doctorexamplejson.txt"), jsonPage);
            t.Check("example: a recommended mod that isn't installed shows on the mod's page", jsonPage.Contains("works better with doctorexamplemissing"));
            string topic = doctor.BuildPage(ev, null, "en", "example-pumps", "").PlainText(sapi.World);
            t.Check("example: the mod's own page of the doctor", topic.Contains("My example pumps aren't pumping") && topic.Contains("jammed pumps"));
            t.Check("example: the mod's fix runs", doctor.RunFix("doctorexample:unjam", "all", null, "en", ev) == "Restarted 3 example pumps");
            t.Check("example: after the fix the check passes", doctor.BuildPage(ev, null, "en", Pages.Overview, "").PlainText(sapi.World).Contains("All example pumps are running"));
            t.Check("example: the fix over the event bus runs", doctor.RunFix("bus:doctorexample:fill", "", null, "en", ev) == "Filled 2 example tanks");
            // The fix the JSON file describes: its config file moved aside.
            string config = Path.Combine(Vintagestory.API.Config.GamePaths.ModConfig, "doctorexample.json");
            File.WriteAllText(config, "{ }");
            string reset = doctor.RunFix("doctorexamplejson:pump-jammed/fix", "", null, "en", ev);
            t.Check("example: the fix described in JSON runs", !reset.StartsWith(DoctorReport.WarnMark) && !File.Exists(config), reset);
            string tanks = doctor.BuildPage(ev, null, "en", "example-tanks", "").PlainText(sapi.World);
            t.Check("example: a whole page written by the mod over the event bus", tanks.Contains("== The example tanks ==") && tanks.Contains("This page came at once")
                && tanks.Contains("/doctor fix bus:doctorexample:fill") && tanks.Contains("The same page, slowly (/doctor example-tanks slow)"), tanks.Replace("\n", " / "));
            string copper = doctor.BuildPage(ev, null, "en", Checks.TopicItem, "game:ingot-copper").PlainText(sapi.World);
            File.WriteAllText(Path.Combine(folder, "page-item-with-examples.txt"), copper);
            t.Check("example: the mod's cards on an item's page, over the event bus and from code", copper.Contains("Example pumps are made of this") && copper.Contains("The example tanks (/doctor example-tanks)")
                && copper.Contains("(game:ingot-copper) for its pipes") && copper.Contains("The example pumps (/doctor example-pumps)"), First(copper, "Example pumps") + " / " + First(copper, "for its pipes"));
            string pipes = doctor.BuildPage(ev, null, "en", "example-pipes", "").PlainText(sapi.World);
            t.Check("example: a page in code with its own heading and three buttons on a line", pipes.Contains("== The example pipes ==") && !pipes.Contains("My example pipes leak")
                && pipes.Contains("The pumps (/doctor example-pumps) The tanks (/doctor example-tanks) Restart the pumps (/doctor fix doctorexample:unjam all)"), pipes.Replace("\n", " / "));
            // A real place two mods patch: the example mod's patch on SharedPlace, and one made here.
            try
            {
                new HarmonyLib.Harmony("vintagedoctor.selftest").Patch(typeof(SelfTest).GetMethod(nameof(SharedPlace)), prefix: new HarmonyLib.HarmonyMethod(typeof(SelfTest).GetMethod(nameof(BeforeSharedPlace))));
                string patches = doctor.BuildPage(ev, null, "en", Checks.TopicPatches, "").PlainText(sapi.World);
                File.WriteAllText(Path.Combine(folder, "page-patches-shared.txt"), patches);
                t.Check("example: a place in the code that two real mods patch is named, with both mods and what each does there",
                    patches.Contains("Doctor Example and Vintage Doctor change the same 1 place(s) in the game's code") && patches.Contains("SelfTest.SharedPlace: Doctor Example (runs after it), Vintage Doctor (runs before it)"),
                    First(patches, "SharedPlace") + " / " + First(patches, "change the same"));
                string its = doctor.BuildPage(ev, null, "en", Checks.TopicMod, "doctorexample").PlainText(sapi.World);
                t.Check("example: the mod's own page says which of its places another mod is at", its.Contains("SelfTest.SharedPlace: runs after it; also changed by Vintage Doctor (runs before it)"), First(its, "SharedPlace"));
                string upNow = doctor.CheckUp(doctor.BuildPage(ev, null, "en", Pages.Overview, ""), ev, null, "en").PlainText(sapi.World);
                t.Check("example: on the check-up that card is under both mods, not the game's", Under(upNow, "Doctor Example 1").Contains("change the same 1 place(s)") && Under(upNow, "Vintage Doctor " + doctor.DoctorVersion).Contains("change the same 1 place(s)")
                    && !Under(upNow, "The game (").Contains("change the same 1 place(s)"), First(Under(upNow, "Doctor Example 1"), "change the same") + " / " + First(Under(upNow, "Vintage Doctor " + doctor.DoctorVersion), "change the same"));
            }
            catch (Exception e) { t.Check("example: a place in the code that two real mods patch", false, e.GetType().Name + ": " + e.Message); }
            // The API's own button for the log (API 3): there when the log is on the player's computer.
            var sp = new DoctorContext { Doctor = doctor, Api = sapi, Side = EnumAppSide.Server, LanguageCode = "en", Topic = Pages.Overview, Ev = ev, SameProcess = true };
            Pages.Build(sp);
            int said = sp.R.Lines.FindIndex(l => l.Text.Contains("kind(s) of messages from the example mod this session"));
            t.Check("example: a mod's check asks for a button that opens the log", said >= 0 && said + 1 < sp.R.Lines.Count && sp.R.Lines[said + 1].Text.Contains("{{folder|logs|server-main.log|Open server-main.log}}"),
                said < 0 ? "no such card" : sp.R.Lines[Math.Min(said + 1, sp.R.Lines.Count - 1)].Text);
            string api = doctor.BuildPage(ev, null, "en", Pages.Api, "").PlainText(sapi.World);
            File.WriteAllText(Path.Combine(folder, "page-api-with-examples.txt"), api);
            t.Check("example: the page for mod authors lists what was handed over", api.Contains("doctorexample:pumps") && api.Contains("doctorexamplejson:config/vintagedoctor.json"));
        }

        static void RealFixes(Run_ t, VintageDoctorSystem doctor, ICoreServerAPI sapi)
        {
            // A broken config file in the real ModConfig folder: found, then moved aside by the fix.
            string name = "doctor-selftest-broken.json";
            string path = Path.Combine(Vintagestory.API.Config.GamePaths.ModConfig, name);
            File.WriteAllText(path, "{ \"half\": ");
            Evidence ev = doctor.EvidenceNow(true);
            string configs = doctor.BuildPage(ev, null, "en", Checks.TopicFiles, "").PlainText(sapi.World);
            t.Check("real fix: a broken config file is found", configs.Contains(name) && configs.Contains("/doctor fix doctor:reset-config " + name), null);
            string outcome = doctor.RunFix("doctor:reset-config", name, null, "en", ev);
            t.Check("real fix: it is moved aside", !outcome.StartsWith(DoctorReport.WarnMark) && !File.Exists(path) && Directory.Exists(Path.Combine(doctor.DataFolder, "backups")), outcome);
            t.Check("real fix: a path outside ModConfig is refused", doctor.RunFix("doctor:reset-config", "../serverconfig.json", null, "en", ev).StartsWith(DoctorReport.WarnMark));
            t.Check("real fix: the doctor's own config is refused", doctor.RunFix("doctor:reset-config", VintageDoctorSystem.ConfigFile, null, "en", ev).StartsWith(DoctorReport.WarnMark));
            string items = doctor.RunFix("doctor:remove-items", "", null, "en", ev);
            t.Check("real fix: removing dropped items runs", !items.StartsWith(DoctorReport.WarnMark), items);
            t.Check("real fix: a loaded mod's file can't be moved by a made-up argument", doctor.RunFix("doctor:move-file", "9/whatever.zip", null, "en", ev).StartsWith(DoctorReport.WarnMark));
            // Switching a mod off on a dedicated server goes through the server's own config.
            ModEntry victim = ev.Mods.UserMods.FirstOrDefault(m => m.Id == "doctorexamplejson") ?? ev.Mods.UserMods.FirstOrDefault(m => m.Id != DoctorApi.ModId && !m.Id.StartsWith("doctorexample"));
            if (victim != null && sapi.Server.IsDedicated)
            {
                string off = doctor.RunFix("doctor:disable-mod", victim.Id, null, "en", ev);
                t.Check("real fix: switching a mod off on a dedicated server", !off.StartsWith(DoctorReport.WarnMark), off);
            }
            else t.Skip("real fix: switching a mod off", "no other mod loaded, or not a dedicated server");
            t.Check("real fix: the doctor won't switch itself off", doctor.RunFix("doctor:disable-mod", DoctorApi.ModId, null, "en", ev).StartsWith(DoctorReport.WarnMark));
            doctor.ForgetEvidence();
        }
    }
}
