using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace VintageDoctor
{
    /// <summary>One crash from the game's crash log (client-crash.log or server-crash.log).</summary>
    public sealed class CrashEntry
    {
        public string GameVersion;
        /// <summary>When, as the game wrote it (in the computer's own date format).</summary>
        public string When;
        public DateTime? Time;
        /// <summary>Mods the game itself named as the cause ("id@version").</summary>
        public List<string> NamedMods = new List<string>();
        public List<string> LoadedMods = new List<string>();
        public List<string> HarmonyIds = new List<string>();
        /// <summary>"System.NullReferenceException" and its message.</summary>
        public string ExceptionType = "", Message = "";
        public List<string> Stack = new List<string>();
        /// <summary>The first mod whose code is in the stack trace, when the doctor could tell.</summary>
        public string TracedMod;

        /// <summary>The mod ids the crash points at: the game's own naming first, then the doctor's reading of the trace.</summary>
        public IEnumerable<string> Culprits
        {
            get
            {
                foreach (string m in NamedMods) yield return m.Split('@')[0];
                if (TracedMod != null && !NamedMods.Any(m => m.Split('@')[0] == TracedMod)) yield return TracedMod;
            }
        }

        public string VersionAtCrash(string modId)
        {
            foreach (string m in LoadedMods)
            {
                int at = m.IndexOf('@');
                if (at > 0 && m.Substring(0, at) == modId) return m.Substring(at + 1);
            }
            return null;
        }
    }

    /// <summary>The crash log on this side. The game adds to the end of the file at every crash and never empties it.</summary>
    public sealed class CrashScan
    {
        public string File, Error;
        public bool Found;
        public long Bytes;
        public DateTime LastWrite;
        /// <summary>Oldest first.</summary>
        public readonly List<CrashEntry> Entries = new List<CrashEntry>();
        public CrashEntry Latest => Entries.Count > 0 ? Entries[Entries.Count - 1] : null;

        static readonly Regex HeaderLine = new Regex(@"^(.*?): Critical error occurred(?: in the following mods?: (.*))?$", RegexOptions.Compiled);
        static readonly Regex ExceptionLine = new Regex(@"^([A-Za-z_][A-Za-z0-9_.`+]*(?:Exception|Error)[A-Za-z0-9_.`+]*)(?::\s*(.*))?$", RegexOptions.Compiled);

        public static CrashScan Read(string file, ModSnapshot mods)
        {
            var scan = new CrashScan { File = file };
            try
            {
                if (!System.IO.File.Exists(file)) return scan;
                var info = new FileInfo(file);
                scan.Found = true;
                scan.Bytes = info.Length;
                scan.LastWrite = info.LastWriteTime;
                string text;
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long start = Math.Max(0, fs.Length - 768 * 1024);
                    fs.Seek(start, SeekOrigin.Begin);
                    using var reader = new StreamReader(fs);
                    text = reader.ReadToEnd();
                }
                scan.Parse(text, mods);
            }
            catch (Exception e) { scan.Error = e.GetType().Name + ": " + e.Message; }
            return scan;
        }

        public static CrashScan FromText(string text, ModSnapshot mods)
        {
            var scan = new CrashScan { File = "(text)", Found = true, Bytes = text?.Length ?? 0, LastWrite = DateTime.Now };
            try { scan.Parse(text ?? "", mods); }
            catch (Exception e) { scan.Error = e.GetType().Name + ": " + e.Message; }
            return scan;
        }

        void Parse(string text, ModSnapshot mods)
        {
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            CrashEntry cur = null;
            bool inException = false;
            foreach (string raw in lines)
            {
                string line = raw.TrimEnd();
                if (line.StartsWith("Game Version: ", StringComparison.Ordinal))
                {
                    cur = new CrashEntry { GameVersion = line.Substring(14).Trim() };
                    Entries.Add(cur);
                    inException = false;
                    continue;
                }
                if (cur == null) continue;
                Match h = cur.When == null ? HeaderLine.Match(line) : Match.Empty;
                if (h.Success)
                {
                    cur.When = h.Groups[1].Value.Trim();
                    if (DateTime.TryParse(cur.When, CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime t) || DateTime.TryParse(cur.When, CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) cur.Time = t;
                    if (h.Groups[2].Success) cur.NamedMods.AddRange(SplitList(h.Groups[2].Value));
                    continue;
                }
                // A server's report has "Loaded Mods: " with nothing after it (the list is only kept by the player's game).
                if (line.StartsWith("Loaded Mods:", StringComparison.Ordinal)) { cur.LoadedMods.AddRange(SplitList(line.Substring(12))); continue; }
                if (line.StartsWith("Involved Harmony IDs:", StringComparison.Ordinal)) { cur.HarmonyIds.AddRange(SplitList(line.Substring(21))); continue; }
                if (line.StartsWith("Running on ", StringComparison.Ordinal) || line.StartsWith("Crash written to file", StringComparison.Ordinal)) continue;
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("at ", StringComparison.Ordinal))
                {
                    if (cur.Stack.Count < 40) cur.Stack.Add(trimmed);
                    continue;
                }
                if (!inException && trimmed.Length > 0)
                {
                    Match ex = ExceptionLine.Match(trimmed);
                    if (ex.Success)
                    {
                        cur.ExceptionType = ex.Groups[1].Value;
                        cur.Message = ex.Groups[2].Success ? ex.Groups[2].Value.Trim() : "";
                        inException = true;
                    }
                    else if (cur.When != null && cur.ExceptionType.Length == 0 && !trimmed.StartsWith("Failed to open crash reporter", StringComparison.Ordinal))
                    {
                        // A crash text that doesn't start with a type name: keep it as the message.
                        cur.Message = trimmed;
                        inException = true;
                    }
                }
            }
            // A header without anything after it isn't a crash worth showing.
            Entries.RemoveAll(e => e.When == null && e.ExceptionType.Length == 0 && e.Stack.Count == 0);
            if (mods != null)
                foreach (CrashEntry e in Entries)
                {
                    try { e.TracedMod = mods.ModOfStack(e.Stack); } catch { }
                }
        }

        static IEnumerable<string> SplitList(string s) => (s ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0 && x != "@");
    }
}
