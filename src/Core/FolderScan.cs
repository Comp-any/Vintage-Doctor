using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace VintageDoctor
{
    /// <summary>One file or folder in a Mods folder, and what's wrong with it if anything.</summary>
    public sealed class ModFile
    {
        public string Path, Name, Folder;
        public long Bytes;
        public DateTime Modified;
        /// <summary>zip, folder, dll, cs, archive (rar/7z...: the game can't read those), partial (a download that didn't finish).</summary>
        public string Kind;
        public string ModId, ModName, Version, Type, GameDep;
        public List<(string id, string version)> Deps = new List<(string, string)>();
        /// <summary>The zip can't be opened, or the folder can't be read.</summary>
        public string ReadError;
        /// <summary>modinfo.json is there but isn't valid.</summary>
        public string ModInfoError;
        public bool NoModInfo;
        /// <summary>modinfo.json sits one folder down ("MyMod/modinfo.json"): the game only looks at the top.</summary>
        public string NestedFolder;
        /// <summary>File names inside use "\" (packed by a Windows tool the wrong way): nothing loads from it on Linux and Mac.</summary>
        public bool Backslashes;
        /// <summary>Looks like the mod's source code (a project file, no built DLL), not a release.</summary>
        public bool SourceCode;
        /// <summary>The zip holds nothing but another zip.</summary>
        public string InnerZip;
        public int Entries;
        /// <summary>Every file in the zip was read to its end (see <see cref="ZipHealth"/>); false while that is still to come.</summary>
        public bool Tested;
        /// <summary>What went wrong reading the zip to its end: a download that was cut short or damaged. Null when it is whole.</summary>
        public string Damage;
        /// <summary>The game loaded this one.</summary>
        public bool Loaded;
        /// <summary>Code libraries at the top of the mod ("MyMod.dll", and whatever else it brings along).</summary>
        public List<string> Dlls = new List<string>();
        /// <summary>The asset domains it has files for (assets/&lt;domain&gt;/...).</summary>
        public HashSet<string> Domains = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>Files it puts into a domain that isn't its own ("game:recipes/grid/ingot.json"): replacing or adding to the game's or another mod's files. Lang files are left out (those are merged, not replaced).</summary>
        public List<string> ForeignAssets = new List<string>();
        /// <summary>The JSON files in its own domain ("recipes/grid/x.json"), so another mod replacing one of them can be told.</summary>
        public HashSet<string> OwnData = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>How many block, item and entity definition files it has, by domain.</summary>
        public Dictionary<string, int> TypeFiles = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>
        /// Code in a place the game doesn't accept: a .dll in a subfolder other than native/, or a .cs file outside
        /// src/. The game refuses to load a zip that has one ("is not in the mod's root folder. Won't load this mod").
        /// </summary>
        public List<string> Misplaced = new List<string>();
        const int MaxListed = 5000;

        /// <summary>Sorts one file of the mod ("assets/game/recipes/x.json", "MyMod.dll") into the lists above. Call once the mod id is known.</summary>
        public void Note(string name)
        {
            int first = name.IndexOf('/');
            bool dll = name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase), cs = name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
            if (first < 0)
            {
                if (dll) Dlls.Add(name);
                else if (cs && Misplaced.Count < 50) Misplaced.Add(name);
                return;
            }
            string top = name.Substring(0, first);
            if ((dll && top != "native" || cs && top != "src") && Misplaced.Count < 50) Misplaced.Add(name);
            if (!name.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)) return;
            int slash = name.IndexOf('/', 7);
            if (slash < 0 || slash == name.Length - 1) return;
            string domain = name.Substring(7, slash - 7).ToLowerInvariant();
            string path = name.Substring(slash + 1).ToLowerInvariant();
            if (path.EndsWith("/", StringComparison.Ordinal)) return;
            Domains.Add(domain);
            bool json = path.EndsWith(".json", StringComparison.Ordinal);
            if (json && (path.StartsWith("blocktypes/", StringComparison.Ordinal) || path.StartsWith("itemtypes/", StringComparison.Ordinal) || path.StartsWith("entities/", StringComparison.Ordinal)))
                TypeFiles[domain] = TypeFiles.TryGetValue(domain, out int n) ? n + 1 : 1;
            if (path.StartsWith("lang/", StringComparison.Ordinal)) return;
            bool own = string.Equals(domain, ModId, StringComparison.OrdinalIgnoreCase);
            if (own) { if (json && OwnData.Count < MaxListed) OwnData.Add(path); }
            else if (ForeignAssets.Count < MaxListed) ForeignAssets.Add(domain + ":" + path);
        }

        public bool Broken => ReadError != null || ModInfoError != null || NoModInfo || NestedFolder != null || InnerZip != null
            || Kind == "archive" || Kind == "partial" || (Backslashes && System.IO.Path.DirectorySeparatorChar != '\\') || (Kind == "zip" && Misplaced.Count > 0);
    }

    /// <summary>What is in the folders the game loads mods from.</summary>
    public sealed class FolderScan
    {
        public readonly List<string> Folders = new List<string>();
        public readonly List<ModFile> Files = new List<ModFile>();
        public readonly List<string> Errors = new List<string>();

        /// <summary>Mod ids found more than once, each with its copies, newest version first.</summary>
        public List<(string id, List<ModFile> copies)> Duplicates()
        {
            return Files.Where(f => !string.IsNullOrEmpty(f.ModId) && f.ReadError == null)
                .GroupBy(f => f.ModId, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => (id: g.Key, copies: g.OrderByDescending(f => f.Version ?? "", Comparer<string>.Create(Versions.Compare)).ThenByDescending(f => f.Modified).ToList()))
                .OrderBy(t => t.Item1, StringComparer.Ordinal).ToList();
        }

        public ModFile Find(string modId) => Files.FirstOrDefault(f => string.Equals(f.ModId, modId, StringComparison.OrdinalIgnoreCase) && f.Loaded)
            ?? Files.FirstOrDefault(f => string.Equals(f.ModId, modId, StringComparison.OrdinalIgnoreCase));

        static readonly string[] GameFiles = { "vssurvivalmod", "vscreativemod", "vsessentials" };

        /// <summary>Looks through the given folders (top level only, as the game does). Never throws.</summary>
        public static FolderScan Read(IEnumerable<string> folders, ModSnapshot mods)
        {
            var scan = new FolderScan();
            var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (mods != null) foreach (ModEntry m in mods.Mods) if (!string.IsNullOrEmpty(m.SourcePath)) loadedPaths.Add(Full(m.SourcePath));
            foreach (string folder in folders.Where(f => !string.IsNullOrEmpty(f)).Select(Full).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (!Directory.Exists(folder)) continue;
                    scan.Folders.Add(folder);
                    foreach (string path in Directory.GetFileSystemEntries(folder))
                    {
                        ModFile f = Look(path);
                        if (f == null) continue;
                        f.Folder = folder;
                        f.Loaded = loadedPaths.Contains(Full(path));
                        scan.Files.Add(f);
                    }
                }
                catch (Exception e) { scan.Errors.Add(folder + ": " + e.Message); }
            }
            return scan;
        }

        static string Full(string path)
        {
            try { return System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar); }
            catch { return path; }
        }

        /// <summary>One entry of a Mods folder; null for things that are neither a mod nor a mistake (a text file, say).</summary>
        public static ModFile Look(string path)
        {
            string name = System.IO.Path.GetFileName(path);
            string lower = name.ToLowerInvariant();
            var f = new ModFile { Path = path, Name = name };
            try
            {
                if (Directory.Exists(path))
                {
                    f.Kind = "folder";
                    f.Modified = Directory.GetLastWriteTime(path);
                    string info = Directory.GetFiles(path).FirstOrDefault(p => string.Equals(System.IO.Path.GetFileName(p), "modinfo.json", StringComparison.OrdinalIgnoreCase));
                    if (info != null)
                    {
                        ReadModInfo(f, File.ReadAllText(info));
                        NoteFolder(f, path);
                    }
                    else
                    {
                        // A folder the game will try to load as a mod and complain about; an empty or unrelated one is left alone.
                        bool modLike = Directory.Exists(System.IO.Path.Combine(path, "assets")) || Directory.GetFiles(path, "*.dll").Length > 0 || Directory.GetFiles(path, "*.cs").Length > 0;
                        string nested = Directory.GetDirectories(path).FirstOrDefault(d => File.Exists(System.IO.Path.Combine(d, "modinfo.json")));
                        if (nested != null) f.NestedFolder = System.IO.Path.GetFileName(nested);
                        else if (modLike) f.NoModInfo = true;
                        else return null;
                    }
                    return f;
                }
                var fi = new FileInfo(path);
                f.Bytes = fi.Length;
                f.Modified = fi.LastWriteTime;
                if (lower.EndsWith(".zip", StringComparison.Ordinal)) { f.Kind = "zip"; ReadZip(f); return f; }
                if (lower.EndsWith(".dll", StringComparison.Ordinal))
                {
                    if (GameFiles.Any(g => lower.StartsWith(g, StringComparison.Ordinal))) return null;
                    f.Kind = "dll";
                    return f;
                }
                if (lower.EndsWith(".cs", StringComparison.Ordinal)) { f.Kind = "cs"; return f; }
                if (lower.EndsWith(".rar", StringComparison.Ordinal) || lower.EndsWith(".7z", StringComparison.Ordinal) || lower.EndsWith(".tar", StringComparison.Ordinal)
                    || lower.EndsWith(".gz", StringComparison.Ordinal) || lower.EndsWith(".tgz", StringComparison.Ordinal)) { f.Kind = "archive"; return f; }
                if (lower.EndsWith(".crdownload", StringComparison.Ordinal) || lower.EndsWith(".part", StringComparison.Ordinal) || lower.EndsWith(".partial", StringComparison.Ordinal)
                    || lower.EndsWith(".download", StringComparison.Ordinal)) { f.Kind = "partial"; return f; }
                return null;
            }
            catch (Exception e)
            {
                f.Kind ??= "zip";
                f.ReadError = e.Message;
                return f;
            }
        }

        /// <summary>What a mod kept as a folder has in it: its DLLs and its asset files.</summary>
        static void NoteFolder(ModFile f, string path)
        {
            try
            {
                foreach (string dll in Directory.GetFiles(path, "*.dll")) f.Note(System.IO.Path.GetFileName(dll));
                string assets = System.IO.Path.Combine(path, "assets");
                if (!Directory.Exists(assets)) return;
                int seen = 0;
                foreach (string file in Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories))
                {
                    if (++seen > 20000) break;
                    f.Note("assets/" + System.IO.Path.GetRelativePath(assets, file).Replace('\\', '/'));
                }
            }
            catch { }
        }

        static void ReadZip(ModFile f)
        {
            try
            {
                using ZipArchive zip = ZipFile.OpenRead(f.Path);
                var names = new List<string>();
                ZipArchiveEntry infoEntry = null;
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    string n = e.FullName;
                    if (n.IndexOf('\\') >= 0) { f.Backslashes = true; n = n.Replace('\\', '/'); }
                    names.Add(n);
                    if (string.Equals(n, "modinfo.json", StringComparison.OrdinalIgnoreCase)) infoEntry = e;
                }
                f.Entries = names.Count;
                if (infoEntry != null)
                {
                    using var reader = new StreamReader(infoEntry.Open());
                    ReadModInfo(f, reader.ReadToEnd());
                }
                else
                {
                    var nested = names.Where(n => n.Count(ch => ch == '/') == 1 && n.EndsWith("/modinfo.json", StringComparison.OrdinalIgnoreCase)).ToList();
                    var files = names.Where(n => !n.EndsWith("/", StringComparison.Ordinal)).ToList();
                    if (nested.Count == 1)
                    {
                        f.NestedFolder = nested[0].Substring(0, nested[0].IndexOf('/'));
                        ZipArchiveEntry inner = zip.Entries.FirstOrDefault(e => string.Equals(e.FullName.Replace('\\', '/'), nested[0], StringComparison.OrdinalIgnoreCase));
                        if (inner != null) { using var reader = new StreamReader(inner.Open()); ReadModInfo(f, reader.ReadToEnd()); }
                    }
                    else if (files.Count == 1 && files[0].EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) f.InnerZip = files[0];
                    else f.NoModInfo = true;
                }
                // What it brings: counted from where the mod really starts (inside its outer folder, if it has one).
                string prefix = f.NestedFolder == null ? "" : f.NestedFolder + "/";
                foreach (string n in names)
                {
                    if (prefix.Length > 0 && !n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    f.Note(n.Substring(prefix.Length));
                }
                bool project = names.Any(n => n.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".sln", StringComparison.OrdinalIgnoreCase));
                bool built = names.Any(n => n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
                f.SourceCode = project && !built;
            }
            catch (Exception e) { f.ReadError = e.Message; }
        }

        /// <summary>Reads the parts of a modinfo.json the doctor uses (keys in any letter case, as the game accepts).</summary>
        public static void ReadModInfo(ModFile f, string json)
        {
            try
            {
                JObject o = JObject.Parse(json);
                string S(string key) => (string)o.GetValue(key, StringComparison.OrdinalIgnoreCase);
                f.ModName = S("name");
                f.ModId = S("modid");
                f.Version = S("version") ?? "";
                f.Type = S("type") ?? "";
                if (string.IsNullOrEmpty(f.ModId) && !string.IsNullOrEmpty(f.ModName))
                {
                    try { f.ModId = ModInfo.ToModID(f.ModName); } catch { }
                }
                if (o.GetValue("dependencies", StringComparison.OrdinalIgnoreCase) is JObject deps)
                    foreach (JProperty p in deps.Properties())
                    {
                        string v = p.Value?.Type == JTokenType.String ? (string)p.Value : "";
                        if (p.Name == "game") f.GameDep = v; else f.Deps.Add((p.Name, v ?? ""));
                    }
                if (string.IsNullOrEmpty(f.ModId)) f.ModInfoError = "no modid and no name";
            }
            catch (Exception e) { f.ModInfoError = e.Message; }
        }

        // ---------------------------------------------------------------- repairs

        /// <summary>
        /// Writes a mod zip again with what was wrong put right: "\" in file names turned into "/", and a single
        /// outer folder taken away so modinfo.json sits at the top. The original is moved to
        /// <paramref name="backupFolder"/> first. Returns the number of entries changed.
        /// </summary>
        public static int Repack(string zipPath, string backupFolder, out string backupPath, Predicate<string> drop = null)
        {
            ModFile before = Look(zipPath);
            if (before == null || before.Kind != "zip" || before.ReadError != null) throw new InvalidOperationException("not a zip the doctor can read");
            string strip = before.NestedFolder == null ? null : before.NestedFolder + "/";
            string temp = zipPath + ".doctor-tmp";
            int changed = 0;
            if (File.Exists(temp)) File.Delete(temp);
            using (ZipArchive src = ZipFile.OpenRead(zipPath))
            using (ZipArchive dst = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (ZipArchiveEntry e in src.Entries)
                {
                    string name = e.FullName.Replace('\\', '/');
                    if (strip != null)
                    {
                        if (!name.StartsWith(strip, StringComparison.OrdinalIgnoreCase)) continue; // only the mod's own folder is kept
                        name = name.Substring(strip.Length);
                    }
                    if (name != e.FullName) changed++;
                    if (name.Length == 0 || name.EndsWith("/", StringComparison.Ordinal) || !seen.Add(name)) continue;
                    if (drop != null && drop(name)) { changed++; continue; }
                    ZipArchiveEntry copy = dst.CreateEntry(name, CompressionLevel.Optimal);
                    copy.LastWriteTime = e.LastWriteTime;
                    using Stream from = e.Open();
                    using Stream to = copy.Open();
                    from.CopyTo(to);
                }
            }
            // The new file has to be a mod before the old one is touched.
            var check = new ModFile { Path = temp, Name = System.IO.Path.GetFileName(zipPath), Kind = "zip" };
            ReadZip(check);
            if (check.ReadError != null || check.NoModInfo || check.NestedFolder != null || check.Backslashes)
            {
                File.Delete(temp);
                throw new InvalidOperationException("the repacked file still isn't right (" + (check.ReadError ?? "modinfo.json not at the top") + "); nothing was changed");
            }
            backupPath = MoveAside(zipPath, backupFolder);
            File.Move(temp, zipPath);
            return changed;
        }

        /// <summary>Moves a file or folder out of the way into the doctor's backup folder (never deletes). Returns where it went.</summary>
        public static string MoveAside(string path, string backupFolder)
        {
            Directory.CreateDirectory(backupFolder);
            string name = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
            string dest = System.IO.Path.Combine(backupFolder, name);
            for (int i = 2; File.Exists(dest) || Directory.Exists(dest); i++)
                dest = System.IO.Path.Combine(backupFolder, System.IO.Path.GetFileNameWithoutExtension(name) + "-" + i + System.IO.Path.GetExtension(name));
            if (Directory.Exists(path)) MoveDirectory(path, dest);
            else
            {
                try { File.Move(path, dest); }
                catch (IOException)
                {
                    // Another drive, or the file is open (the game holds loaded zips on Windows): copy, then try to remove.
                    File.Copy(path, dest, true);
                    File.Delete(path);
                }
            }
            return dest;
        }

        static void MoveDirectory(string from, string to)
        {
            try { Directory.Move(from, to); }
            catch (IOException)
            {
                Directory.CreateDirectory(to);
                foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                {
                    string rel = System.IO.Path.GetRelativePath(from, file);
                    string dest = System.IO.Path.Combine(to, rel);
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest));
                    File.Copy(file, dest, true);
                }
                Directory.Delete(from, true);
            }
        }

        /// <summary>Reads every file in a zip to the end: a download that was cut short or damaged fails here. Null when fine.</summary>
        public static string TestZip(string path)
        {
            try
            {
                using ZipArchive zip = ZipFile.OpenRead(path);
                byte[] buffer = new byte[81920];
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    if (e.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                    using Stream s = e.Open();
                    while (s.Read(buffer, 0, buffer.Length) > 0) { }
                }
                return null;
            }
            catch (Exception e) { return e.Message; }
        }
    }

    /// <summary>
    /// Whether each zip in the Mods folders can be read to its end, which is what finds a download that was cut short
    /// or damaged inside. Reading them all takes a while with many mods, so a file is read once and remembered until
    /// it changes, and never on the game's main thread for longer than it is given.
    /// </summary>
    public static class ZipHealth
    {
        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> known = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        static int warming;

        static string Key(ModFile f) => f.Path + "|" + f.Bytes + "|" + f.Modified.Ticks;

        /// <summary>
        /// Marks each zip as whole or damaged (<see cref="ModFile.Tested"/>, <see cref="ModFile.Damage"/>). Files not
        /// read yet are read now, until <paramref name="budgetMs"/> is used up (no limit when negative); the ones
        /// left over are read in the background, so the next look has them.
        /// </summary>
        public static void Test(List<ModFile> files, int budgetMs)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            List<(string key, string path)> later = null;
            foreach (ModFile f in files)
            {
                if (f.Kind != "zip" || f.ReadError != null) continue;
                string key = Key(f);
                if (!known.TryGetValue(key, out string result))
                {
                    if (budgetMs >= 0 && watch.ElapsedMilliseconds >= budgetMs)
                    {
                        if (later == null) later = new List<(string, string)>();
                        later.Add((key, f.Path));
                        continue;
                    }
                    result = FolderScan.TestZip(f.Path) ?? "";
                    known[key] = result;
                }
                f.Tested = true;
                f.Damage = result.Length > 0 ? result : null;
            }
            if (later == null || Interlocked.CompareExchange(ref warming, 1, 0) != 0) return;
            Task.Run(() =>
            {
                try
                {
                    foreach (var (key, path) in later)
                    {
                        if (!known.ContainsKey(key)) known[key] = FolderScan.TestZip(path) ?? "";
                    }
                }
                catch { }
                finally { Interlocked.Exchange(ref warming, 0); }
            });
        }
    }

    /// <summary>Version numbers compared the way mods use them: 1.10.0 is newer than 1.9.2, and 1.0.0 newer than 1.0.0-rc.1.</summary>
    public static class Versions
    {
        public static int Compare(string a, string b)
        {
            Split(a, out int[] na, out string pa);
            Split(b, out int[] nb, out string pb);
            for (int i = 0; i < Math.Max(na.Length, nb.Length); i++)
            {
                int x = i < na.Length ? na[i] : 0, y = i < nb.Length ? nb[i] : 0;
                if (x != y) return x.CompareTo(y);
            }
            if (pa.Length == 0 && pb.Length == 0) return 0;
            if (pa.Length == 0) return 1;
            if (pb.Length == 0) return -1;
            return ComparePre(pa, pb);
        }

        static int ComparePre(string a, string b)
        {
            string[] xa = a.Split('.'), xb = b.Split('.');
            for (int i = 0; i < Math.Max(xa.Length, xb.Length); i++)
            {
                if (i >= xa.Length) return -1;
                if (i >= xb.Length) return 1;
                bool ia = int.TryParse(xa[i], out int va), ib = int.TryParse(xb[i], out int vb);
                int c = ia && ib ? va.CompareTo(vb) : string.CompareOrdinal(xa[i], xb[i]);
                if (c != 0) return c;
            }
            return 0;
        }

        static void Split(string v, out int[] numbers, out string pre)
        {
            v = (v ?? "").Trim().TrimStart('v', 'V');
            int plus = v.IndexOf('+');
            if (plus >= 0) v = v.Substring(0, plus);
            int dash = v.IndexOf('-');
            pre = dash >= 0 ? v.Substring(dash + 1) : "";
            if (dash >= 0) v = v.Substring(0, dash);
            numbers = v.Split('.').Select(p => int.TryParse(new string(p.TakeWhile(char.IsDigit).ToArray()), out int n) ? n : 0).ToArray();
        }

        /// <summary>Major and minor of a version ("1.22.7" → 1, 22). False when it isn't a version.</summary>
        public static bool MajorMinor(string v, out int major, out int minor)
        {
            major = minor = 0;
            if (string.IsNullOrWhiteSpace(v)) return false;
            string[] parts = v.Trim().TrimStart('v', 'V').Split('-')[0].Split('.');
            return parts.Length >= 2 && int.TryParse(parts[0], out major) && int.TryParse(parts[1], out minor);
        }
    }
}
