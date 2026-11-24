using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace VintageDoctor
{
    /// <summary>One kind of error or warning from a log: the same message repeated is one of these with a count.</summary>
    public sealed class LogIssue
    {
        public string Level;                 // Error, Warning or Fatal
        public string Message;               // the first such line, without time and level
        public List<string> Stack = new List<string>();
        public int Count;
        public string FirstTime, LastTime;
        public string Mod;                   // the mod it was traced to (null: unknown or the game)
        public string File;                  // a mod file that isn't a loaded mod ("broken.zip"), when the line names one
        public string Signature;
        public bool Startup;                 // written before the game finished starting
        public API.KnownProblem Known;       // a message the doctor (or a mod) knows how to explain
        public bool IsException => Message.IndexOf("Exception", StringComparison.Ordinal) >= 0 || Stack.Count > 0;
        public bool Bad => Level != "Warning";

        public double PerMinute
        {
            get
            {
                if (Count < 2 || !LogScan.TryTime(FirstTime, out DateTime a) || !LogScan.TryTime(LastTime, out DateTime b)) return 0;
                double minutes = Math.Max(1.0 / 6, (b - a).TotalMinutes);
                return Count / minutes;
            }
        }
    }

    /// <summary>A mod the game left out because something it needs isn't there.</summary>
    public sealed class SkippedMod
    {
        public string Mod;
        /// <summary>What it needs ("vsimgui@1.2.0") and why that failed ("Missing", "Version mismatch (has 1.1.0)"...).</summary>
        public List<(string dep, string why)> Needs = new List<(string, string)>();
    }

    /// <summary>A mod (or a file in the Mods folder) that threw while loading or starting.</summary>
    public sealed class StartFailure
    {
        public string Mod;                   // mod id, or the file name when the mod never got an id
        public bool IsFile;
        public string What;                  // the game's own line ("An exception was thrown when trying to start the mod:")
        public string Error;                 // the exception's message
        public List<string> Stack = new List<string>();
        public List<string> Systems = new List<string>();  // mod systems the game said it could not start
    }

    /// <summary>
    /// What a main log (server-main.log or client-main.log) says about this session: the errors and warnings, grouped
    /// and traced to mods, and what the game reported while loading mods and starting.
    /// </summary>
    public sealed class LogScan
    {
        public string File, Error;
        public bool Found;
        public long Bytes;
        public int Lines;
        /// <summary>The file was too big to read whole: the start and the end were read, the middle left out.</summary>
        public bool Truncated;
        public string GameVersion, Os, Cpu, Ram, Framework, GpuVendor, GpuRenderer, GpuVersion, FirstTime, LastTime;
        public readonly List<LogIssue> Issues = new List<LogIssue>();
        public int ErrorLines, WarningLines;
        public readonly List<SkippedMod> Skipped = new List<SkippedMod>();
        public readonly List<string> Blocked = new List<string>();
        public readonly List<string> DuplicateIds = new List<string>();
        public readonly List<StartFailure> StartFailures = new List<StartFailure>();
        public readonly Dictionary<string, int> FailedPatches = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> FailedPatchExample = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, int> Unresolved = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> UnresolvedExample = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>
        /// Blocks and items that name code the game doesn't have ("has defined a block class X, no such class registered",
        /// "Collectible behavior X for item Y not found"). The kind is block, item, entity (a block's data code) or behavior.
        /// </summary>
        public readonly List<(string kind, string code, string className)> MissingClasses = new List<(string, string, string)>();
        /// <summary>Blocks and items the game refused to add ("Failed registering item X"), most often because the name was taken.</summary>
        public readonly List<(string kind, string code)> FailedRegistrations = new List<(string, string)>();
        /// <summary>Files of a mod (blocks, items, recipes...) the game could not read and skipped, by the mod (asset domain) they are in.</summary>
        public readonly Dictionary<string, List<string>> BrokenTypes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        /// <summary>JSON patches for files that aren't there, by the mod that has the patch: the files it wanted to change.</summary>
        public readonly Dictionary<string, List<string>> MissingTargets = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        /// <summary>Mods marked as content mods that have code in them, which the game leaves out ("Is a Content mod, but .cs or .dll files were found").</summary>
        public readonly List<string> IgnoredCode = new List<string>();
        public readonly List<string> Phases = new List<string>();
        public readonly List<string> ModPaths = new List<string>();
        public int ModsFound = -1, ModsDisabled;
        public string CyclicDependencies;
        /// <summary>"Server overloaded. A tick took Nms": when and how long.</summary>
        public readonly List<(string time, long ms)> Overloads = new List<(string, long)>();
        public bool CompileFailed;
        /// <summary>
        /// The game's errors about formatting tags in a text it showed ("Found closing tag char > but no tag was
        /// opened at 12..."), each time with when. Not counted among the errors: <see cref="TextTags"/> finds whose they are.
        /// </summary>
        public readonly List<(string time, string message)> TagErrors = new List<(string, string)>();

        public IEnumerable<LogIssue> For(string modId) => Issues.Where(i => string.Equals(i.Mod, modId, StringComparison.OrdinalIgnoreCase));

        public const string TimeFormat = "d.M.yyyy HH:mm:ss";
        public static bool TryTime(string s, out DateTime t) => DateTime.TryParseExact(s ?? "", TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out t);

        static readonly Regex LineStart = new Regex(@"^(\d{1,2}\.\d{1,2}\.\d{4} \d{2}:\d{2}:\d{2}) \[([A-Za-z ]+)\] ?(.*)$", RegexOptions.Compiled);
        static readonly Regex Prefix = new Regex(@"^\[([^\]\s]{2,80})\]\s+(.*)$", RegexOptions.Compiled | RegexOptions.Singleline);
        static readonly Regex DepLine = new Regex(@"^(\S+) - (.+)$", RegexOptions.Compiled);
        static readonly Regex PatchFail = new Regex(@"^Patch \d+ \(target: ([^)]*)\) in ([a-z0-9_.-]+):(\S*)", RegexOptions.Compiled);
        static readonly Regex PatchNoFile = new Regex(@"^Patch \d+ in ([a-z0-9_.-]+):(\S+?): File (\S+?) not found", RegexOptions.Compiled);
        static readonly Regex NoClass = new Regex(@"^(Block|Item) with code (\S+) has defined an? (block entity|block|item) class (\S+?),", RegexOptions.Compiled);
        static readonly Regex NoBehavior = new Regex(@"^(?:Block or Collectible|Block entity|Block|Collectible) behavior (\S+) for (?:block|item) (\S+) not found", RegexOptions.Compiled);
        static readonly Regex NotRegistered = new Regex(@"^Failed registering (item|block) (\S+?):?$", RegexOptions.Compiled);
        static readonly Regex TypeNotLoaded = new Regex(@"^(Item|Block|Entity) type ([a-z0-9_.-]+):(\S+?) could not be loaded", RegexOptions.Compiled);
        static readonly Regex SyntaxError = new Regex(@"^Syntax error in json file '([a-z0-9_.-]+):([^']+)'", RegexOptions.Compiled);
        static readonly Regex Unresolvable = new Regex(@"^Failed resolving .* with code ([a-z0-9_.-]+):(\S+)", RegexOptions.Compiled);
        static readonly Regex DuplicateId = new Regex(@"^Multiple mods share the mod ID '([^']+)' \((.*)\)\. Will only load the highest version one - v(.+)\.$", RegexOptions.Compiled);
        static readonly Regex Overload = new Regex(@"^Server overloaded\. A tick took (\d+)ms", RegexOptions.Compiled);
        static readonly Regex FoundMods = new Regex(@"^Found (\d+) mods \((\d+) disabled\)", RegexOptions.Compiled);
        static readonly Regex AssetPath = new Regex(@"\b([a-z0-9_-]+):(?:blocktypes|itemtypes|entities|recipes|patches|shapes|textures|worldgen|config|lang|sounds|dialog|music|shaders|worldproperties)/", RegexOptions.Compiled);
        static readonly Regex Code = new Regex(@"\b([a-z][a-z0-9_-]{1,40}):[a-z0-9][a-z0-9_/.*-]*", RegexOptions.Compiled);
        static readonly Regex NamePrefix = new Regex(@"^([A-Za-z][A-Za-z0-9 _'.-]{2,40}?)\s*:\s", RegexOptions.Compiled);
        static readonly Regex Numbers = new Regex(@"\d+(?:[.,]\d+)*", RegexOptions.Compiled);
        static readonly Regex Quoted = new Regex(@"'[^']{1,200}'|""[^""]{1,200}""", RegexOptions.Compiled);
        static readonly Regex CodeTail = new Regex(@"\b([a-z][a-z0-9_-]{1,40}):[a-z0-9][a-z0-9_/.*-]*", RegexOptions.Compiled);

        const long Whole = 12 * 1024 * 1024, Part = 5 * 1024 * 1024;

        sealed class Entry { public string Time, Level, Text; public List<string> More; public bool Startup; }

        /// <summary>Reads and understands a main log. Never throws: <see cref="Error"/> says what went wrong.</summary>
        public static LogScan Read(string file, ModSnapshot mods)
        {
            var scan = new LogScan { File = file };
            try
            {
                if (!System.IO.File.Exists(file)) { scan.Error = "nofile"; return scan; }
                List<string> lines = ReadLines(file, scan);
                scan.Found = true;
                scan.Parse(lines, mods);
            }
            catch (Exception e) { scan.Error = e.GetType().Name + ": " + e.Message; }
            return scan;
        }

        /// <summary>The same from text already in memory (the self-test's fixtures).</summary>
        public static LogScan FromText(string text, ModSnapshot mods)
        {
            var scan = new LogScan { File = "(text)", Found = true };
            try { scan.Parse((text ?? "").Replace("\r\n", "\n").Split('\n').ToList(), mods); }
            catch (Exception e) { scan.Error = e.GetType().Name + ": " + e.Message; }
            return scan;
        }

        /// <summary>
        /// After very many lines the game carries on in a new file (server-main-2.log, -3.log...): the newest of those
        /// holds what is happening now, the first file what happened at start-up. Null when there is none.
        /// </summary>
        static string LatestSplit(string file)
        {
            try
            {
                string dir = Path.GetDirectoryName(file), stem = Path.GetFileNameWithoutExtension(file);
                string latest = null;
                int best = -1;
                foreach (string f in Directory.GetFiles(dir, stem + "-*.log"))
                {
                    string tail = Path.GetFileNameWithoutExtension(f).Substring(stem.Length + 1);
                    if (int.TryParse(tail, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > best) { best = n; latest = f; }
                }
                return latest;
            }
            catch { return null; }
        }

        static void ReadTail(string file, long bytes, List<string> lines)
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            bool cut = fs.Length > bytes;
            if (cut) fs.Seek(fs.Length - bytes, SeekOrigin.Begin);
            using var reader = new StreamReader(fs);
            if (cut) reader.ReadLine(); // a partial line
            string l;
            while ((l = reader.ReadLine()) != null) lines.Add(l);
        }

        static List<string> ReadLines(string file, LogScan scan)
        {
            var lines = new List<string>();
            string split = LatestSplit(file);
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            scan.Bytes = fs.Length;
            if (split != null)
            {
                // The start of the session from the first file, the present from the newest.
                scan.Truncated = true;
                using (var head = new StreamReader(fs, System.Text.Encoding.UTF8, true, 65536, leaveOpen: true))
                {
                    long read = 0;
                    string l;
                    while (read < Part && (l = head.ReadLine()) != null) { lines.Add(l); read += l.Length + 1; }
                }
                try { scan.Bytes += new FileInfo(split).Length; ReadTail(split, Part, lines); } catch { }
                return lines;
            }
            if (fs.Length <= Whole)
            {
                using var all = new StreamReader(fs);
                string l;
                while ((l = all.ReadLine()) != null) lines.Add(l);
                return lines;
            }
            // A long session: the start (what happened while loading) and the end (what is happening now).
            scan.Truncated = true;
            using (var head = new StreamReader(fs, System.Text.Encoding.UTF8, true, 65536, leaveOpen: true))
            {
                long read = 0;
                string l;
                while (read < Part && (l = head.ReadLine()) != null) { lines.Add(l); read += l.Length + 1; }
            }
            fs.Seek(fs.Length - Part, SeekOrigin.Begin);
            using var tail = new StreamReader(fs);
            tail.ReadLine(); // a partial line
            string t;
            while ((t = tail.ReadLine()) != null) lines.Add(t);
            return lines;
        }

        void Parse(List<string> lines, ModSnapshot mods)
        {
            Lines = lines.Count;
            // Only this session: the log can hold an earlier run when the game kept the file.
            int from = lines.FindLastIndex(l => l.EndsWith("logger started.", StringComparison.Ordinal));
            if (from < 0) from = 0;

            var entries = new List<Entry>();
            Entry cur = null;
            bool startup = true;
            for (int i = from; i < lines.Count; i++)
            {
                string line = lines[i];
                Match m = line.Length > 20 && char.IsDigit(line[0]) ? LineStart.Match(line) : Match.Empty;
                if (!m.Success)
                {
                    if (cur != null && line.Length > 0 && (cur.More ??= new List<string>()).Count < 60) cur.More.Add(line);
                    continue;
                }
                cur = new Entry { Time = m.Groups[1].Value, Level = m.Groups[2].Value, Text = m.Groups[3].Value, Startup = startup };
                FirstTime ??= cur.Time;
                LastTime = cur.Time;
                if (cur.Level == "Notification" || cur.Level == "Event")
                {
                    Header(cur.Text, ref startup);
                    cur = null; // nothing hangs off these
                    continue;
                }
                if (cur.Level != "Error" && cur.Level != "Warning" && cur.Level != "Fatal") { cur = null; continue; }
                entries.Add(cur);
            }
            Understand(entries, mods);
        }

        /// <summary>The facts the game writes at the top of a log and as it starts.</summary>
        void Header(string text, ref bool startup)
        {
            if (text.StartsWith("Game Version: ", StringComparison.Ordinal)) GameVersion = text.Substring(14).Trim();
            else if (text.StartsWith("Operating System: ", StringComparison.Ordinal)) Os = text.Substring(18).Trim();
            else if (text.StartsWith("CPU: ", StringComparison.Ordinal)) Cpu = text.Substring(5).Trim();
            else if (text.StartsWith("Available RAM: ", StringComparison.Ordinal)) Ram = text.Substring(15).Trim();
            else if (text.StartsWith("C# Framework: ", StringComparison.Ordinal)) Framework = text.Substring(14).Trim();
            else if (text.StartsWith("Graphics Card Vendor: ", StringComparison.Ordinal)) GpuVendor = text.Substring(22).Trim();
            else if (text.StartsWith("Graphics Card Renderer: ", StringComparison.Ordinal)) GpuRenderer = text.Substring(24).Trim();
            else if (text.StartsWith("Graphics Card Version: ", StringComparison.Ordinal)) GpuVersion = text.Substring(23).Trim();
            else if (text.StartsWith("Entering runphase ", StringComparison.Ordinal))
            {
                string phase = text.Substring(18).Trim();
                Phases.Add(phase);
                if (phase == "RunGame") startup = false;
            }
            else if (text.StartsWith("Started ", StringComparison.Ordinal) && text.Contains(" systems on Client")) startup = false;
            else if (text.StartsWith("Found ", StringComparison.Ordinal))
            {
                Match m = FoundMods.Match(text);
                if (m.Success) { ModsFound = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); ModsDisabled = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture); }
            }
            else if (text.StartsWith("    ", StringComparison.Ordinal) && Phases.Count > 0 && ModsFound < 0 && ModPaths.Count < 12) ModPaths.Add(text.Trim());
        }

        void Understand(List<Entry> entries, ModSnapshot mods)
        {
            var bySignature = new Dictionary<string, LogIssue>(StringComparer.Ordinal);
            SkippedMod depMod = null;
            StartFailure pending = null;   // a "… when trying to start the mod:" line waiting for its exception
            bool blockedList = false;

            foreach (Entry e in entries)
            {
                string text = e.Text;
                string tag = null;
                Match p = Prefix.Match(text);
                if (p.Success) { tag = p.Groups[1].Value; text = p.Groups[2].Value; }
                if (e.Level == "Warning") WarningLines++; else ErrorLines++;

                // ---- what the game says while it loads mods
                if (blockedList)
                {
                    if (e.Level == "Warning" && e.Text.StartsWith("  ", StringComparison.Ordinal)) { Blocked.Add(e.Text.Trim()); continue; }
                    blockedList = false;
                }
                if (tag != null && depMod != null && tag == depMod.Mod)
                {
                    Match d = DepLine.Match(text.Trim());
                    if (d.Success) { depMod.Needs.Add((d.Groups[1].Value, d.Groups[2].Value.Trim())); continue; }
                }
                depMod = null;
                if (tag != null && text.StartsWith("Could not resolve some dependencies", StringComparison.Ordinal))
                {
                    depMod = Skipped.FirstOrDefault(s => s.Mod == tag);
                    if (depMod == null) Skipped.Add(depMod = new SkippedMod { Mod = tag });
                    continue;
                }
                if (text.StartsWith("The following mods where blocked from loading", StringComparison.Ordinal)) { blockedList = true; continue; }
                if (text.StartsWith("Possible cyclic dependencies between mods: ", StringComparison.Ordinal)) CyclicDependencies = text.Substring(43).Trim();
                if (text.StartsWith("One or more source code mods failed to compile", StringComparison.Ordinal)) CompileFailed = true;
                Match dup = DuplicateId.Match(text);
                if (dup.Success) { DuplicateIds.Add(dup.Groups[1].Value + ": " + dup.Groups[2].Value + ", loaded: v" + dup.Groups[3].Value); continue; }

                Match pf = PatchFail.Match(text);
                if (pf.Success)
                {
                    string domain = pf.Groups[2].Value;
                    Bump(FailedPatches, domain);
                    if (!FailedPatchExample.ContainsKey(domain)) FailedPatchExample[domain] = Shorten(text, 260);
                    continue;
                }
                Match nf = PatchNoFile.Match(text);
                if (nf.Success)
                {
                    string domain = nf.Groups[1].Value;
                    if (!MissingTargets.TryGetValue(domain, out List<string> targets)) MissingTargets[domain] = targets = new List<string>();
                    string target = nf.Groups[3].Value.TrimEnd('.', ',');
                    if (!targets.Contains(target) && targets.Count < 200) targets.Add(target);
                    // A patch for a mod that isn't installed is how mods support each other, and harmless; one for a
                    // file of the game or of an installed mod that isn't there has stopped working.
                    int colon = target.IndexOf(':');
                    string targetDomain = colon > 0 ? target.Substring(0, colon) : "game";
                    if (ModSnapshot.IsGameMod(targetDomain) || (mods != null && mods.Has(targetDomain)))
                    {
                        Bump(FailedPatches, domain);
                        if (!FailedPatchExample.ContainsKey(domain)) FailedPatchExample[domain] = Shorten(text, 260);
                    }
                    continue;
                }
                Match nc = NoClass.Match(text);
                if (nc.Success)
                {
                    string kind = nc.Groups[3].Value == "block entity" ? "entity" : nc.Groups[3].Value;
                    if (MissingClasses.Count < 2000 && !MissingClasses.Contains((kind, nc.Groups[2].Value, nc.Groups[4].Value))) MissingClasses.Add((kind, nc.Groups[2].Value, nc.Groups[4].Value));
                    // No "continue": it counts as one of the mod's errors too.
                }
                Match nb = NoBehavior.Match(text);
                if (nb.Success)
                {
                    var lacking = ("behavior", nb.Groups[2].Value, nb.Groups[1].Value);
                    if (MissingClasses.Count < 2000 && !MissingClasses.Contains(lacking)) MissingClasses.Add(lacking);
                }
                Match fr = NotRegistered.Match(text.TrimEnd());
                if (fr.Success && FailedRegistrations.Count < 2000 && !FailedRegistrations.Contains((fr.Groups[1].Value, fr.Groups[2].Value)))
                    FailedRegistrations.Add((fr.Groups[1].Value, fr.Groups[2].Value));
                // 1.22 says "Syntax error in json file 'mod:itemtypes/x.json': ..."; newer versions "Item type mod:itemtypes/x.json could not be loaded".
                Match tn = TypeNotLoaded.Match(text);
                Match se = tn.Success ? Match.Empty : SyntaxError.Match(text);
                if (tn.Success || se.Success)
                {
                    string domain = tn.Success ? tn.Groups[2].Value : se.Groups[1].Value, file = tn.Success ? tn.Groups[3].Value.TrimEnd('.', ',') : se.Groups[2].Value;
                    if (!BrokenTypes.TryGetValue(domain, out List<string> broken)) BrokenTypes[domain] = broken = new List<string>();
                    if (!broken.Contains(file) && broken.Count < 200) broken.Add(file);
                    // No "continue": the error itself, with what was wrong in the file, belongs in the log's list too.
                }
                if (tag != null && text.StartsWith("Is a ", StringComparison.Ordinal) && text.Contains(" mod, but .cs or .dll files were found"))
                {
                    if (!IgnoredCode.Contains(tag)) IgnoredCode.Add(tag);
                    continue;
                }
                Match un = Unresolvable.Match(text);
                if (un.Success)
                {
                    string domain = un.Groups[1].Value;
                    Bump(Unresolved, domain);
                    if (!UnresolvedExample.ContainsKey(domain)) UnresolvedExample[domain] = domain + ":" + un.Groups[2].Value.TrimEnd(':', ',', '.');
                    continue;
                }
                // The line after "Failed resolving …" says the same about the whole recipe: one count is enough.
                if (text.Contains("contains an ingredient that cannot be resolved")) continue;
                if (tag == null && TextTags.IsTagError(text)) { TagErrors.Add((e.Time, text.Trim())); continue; }
                Match ov = Overload.Match(text);
                if (ov.Success) { if (long.TryParse(ov.Groups[1].Value, out long ms)) Overloads.Add((e.Time, ms)); continue; }

                // ---- mods that threw while loading or starting
                if (tag != null && IsStartFailureLine(text))
                {
                    pending = new StartFailure { Mod = tag, What = text.Trim(), IsFile = mods == null || !mods.Has(tag) };
                    if (text.StartsWith("Missing modinfo", StringComparison.Ordinal) || text.StartsWith("Missing ModInfoAttribute", StringComparison.Ordinal)
                        || text.StartsWith("File '", StringComparison.Ordinal)) { pending.Error = text.Trim(); StartFailures.Add(pending); pending = null; }
                    continue;
                }
                if (pending != null && tag == pending.Mod && text.StartsWith("Exception: ", StringComparison.Ordinal))
                {
                    pending.Error = FirstLine(text.Substring(11));
                    AddStack(pending.Stack, text, e.More);
                    StartFailures.Add(pending);
                    pending = null;
                    continue;
                }
                if (pending != null) { StartFailures.Add(pending); pending = null; }
                if (text.StartsWith("Failed to start system ", StringComparison.Ordinal) || text.StartsWith("Failed to run mod phase ", StringComparison.Ordinal))
                {
                    StartFailure last = StartFailures.LastOrDefault();
                    if (last != null && last.Systems.Count < 8) last.Systems.Add(text.Substring(text.LastIndexOf(' ') + 1).Trim());
                    continue;
                }

                // ---- everything else: an error or a warning of some kind
                var stack = new List<string>();
                AddStack(stack, text, e.More);
                string first = FirstLine(text);
                string signature = e.Level[0] + "|" + (tag ?? "") + "|" + Normalise(first) + "|" + (stack.Count > 0 ? Normalise(stack[0]) : "");
                if (!bySignature.TryGetValue(signature, out LogIssue issue))
                {
                    issue = new LogIssue { Level = e.Level, Message = (tag != null ? "[" + tag + "] " : "") + first, Signature = signature, FirstTime = e.Time, Startup = e.Startup };
                    issue.Stack.AddRange(stack.Take(14));
                    Attribute(issue, tag, first, mods);
                    bySignature[signature] = issue;
                    Issues.Add(issue);
                }
                issue.Count++;
                issue.LastTime = e.Time;
            }
            if (pending != null) StartFailures.Add(pending);
        }

        static bool IsStartFailureLine(string text)
        {
            return text.StartsWith("An exception was thrown when trying to start the mod", StringComparison.Ordinal)
                || text.StartsWith("An exception was thrown when trying to load assembly", StringComparison.Ordinal)
                || text.StartsWith("An exception was thrown trying to to load the ModInfo", StringComparison.Ordinal)
                || text.StartsWith("An exception was thrown when trying to extract the mod archive", StringComparison.Ordinal)
                || text.StartsWith("Exception thrown when trying to create an instance of ModSystem", StringComparison.Ordinal)
                || text.StartsWith("Exception thrown when attempting to retrieve all types of the assembly", StringComparison.Ordinal)
                || text.StartsWith("Missing modinfo.json", StringComparison.Ordinal)
                || text.StartsWith("Missing ModInfoAttribute", StringComparison.Ordinal)
                || (text.StartsWith("File '", StringComparison.Ordinal) && text.Contains("Won't load this mod"));
        }

        /// <summary>The stack trace lines of an entry: after the first line of its text, and the lines that followed it in the file.</summary>
        static void AddStack(List<string> stack, string text, List<string> more)
        {
            int nl = text.IndexOf('\n');
            if (nl >= 0) foreach (string s in text.Substring(nl + 1).Split('\n')) if (s.TrimStart().StartsWith("at ", StringComparison.Ordinal)) stack.Add(s.Trim());
            if (more != null) foreach (string s in more) if (s.TrimStart().StartsWith("at ", StringComparison.Ordinal) || s.Contains("--->")) stack.Add(s.Trim());
        }

        static string FirstLine(string text)
        {
            int nl = text.IndexOf('\n');
            return (nl >= 0 ? text.Substring(0, nl) : text).Trim();
        }

        public static string Shorten(string s, int max) => s == null ? "" : s.Length <= max ? s : s.Substring(0, max - 1) + "…";

        /// <summary>A message with what varies taken out, so "… for x:mug-red" and "… for x:mug-blue" count as one.</summary>
        public static string Normalise(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length > 400) s = s.Substring(0, 400);
            s = Quoted.Replace(s, "'*'");
            s = CodeTail.Replace(s, m => m.Groups[1].Value + ":*");
            s = Numbers.Replace(s, "#");
            return s.Length > 180 ? s.Substring(0, 180) : s;
        }

        static void Bump(Dictionary<string, int> d, string key) => d[key] = d.TryGetValue(key, out int n) ? n + 1 : 1;

        /// <summary>Works out which mod a line is about: its [tag], a patch or asset path, the stack trace, a "Name:" in front, or a code.</summary>
        static void Attribute(LogIssue issue, string tag, string text, ModSnapshot mods)
        {
            if (tag != null)
            {
                if (mods == null || mods.Has(tag)) { if (!ModSnapshot.IsGameMod(tag)) issue.Mod = mods?.Get(tag)?.Id ?? tag; if (issue.Mod != null) return; }
                else
                {
                    string byName = mods.ByLooseName(tag);
                    if (byName != null && !ModSnapshot.IsGameMod(byName)) { issue.Mod = byName; return; }
                    if (tag.IndexOf('.') > 0) { issue.File = tag; return; }
                    // A tag the game doesn't know as a loaded mod: a mod it skipped, or a mod's own made-up tag.
                    if (Regex.IsMatch(tag, "^[a-z0-9_-]+$")) { issue.Mod = tag; return; }
                }
            }
            if (mods == null) return;
            string fromStack = mods.ModOfStack(issue.Stack);
            if (fromStack != null) { issue.Mod = fromStack; return; }
            Match a = AssetPath.Match(text);
            if (a.Success && !ModSnapshot.IsGameMod(a.Groups[1].Value)) { issue.Mod = a.Groups[1].Value; return; }
            Match n = NamePrefix.Match(text);
            if (n.Success)
            {
                string byName = mods.ByLooseName(n.Groups[1].Value);
                if (byName != null && !ModSnapshot.IsGameMod(byName)) { issue.Mod = byName; return; }
            }
            foreach (Match c in Code.Matches(text))
            {
                string domain = c.Groups[1].Value;
                if (!ModSnapshot.IsGameMod(domain) && mods.Has(domain)) { issue.Mod = mods.Get(domain).Id; return; }
            }
        }
    }
}
