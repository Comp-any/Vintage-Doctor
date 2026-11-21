using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vintagestory.API.Config;

namespace VintageDoctor
{
    public static partial class Checks
    {
        public const string TopicFiles = "files";

        /// <summary>The Mods folders: files the game can't load, mods installed twice, mods made for a newer game.</summary>
        public static void Folder(DoctorContext x, bool full, bool deep = false)
        {
            FolderScan scan = x.Ev.Folder;
            if (full) x.Section(x.T("sec-folder"));
            if (scan == null) { if (full) x.Info(x.T("folder-notscanned")); return; }
            int before = x.Problems + x.Warnings;
            bool windows = Path.DirectorySeparatorChar == '\\';

            foreach (ModFile f in scan.Files)
            {
                string name = f.Name;
                string arg = FileArg(scan, f);
                if (f.ReadError != null)
                {
                    x.Problem(x.T("folder-unreadable", name));
                    x.Detail(Anonymise.Line(f.ReadError));
                    x.Explain(x.T("folder-unreadable-why"));
                    x.Advice(x.T("folder-unreadable-advice"));
                    x.FixButton("doctor:move-file", arg, x.T("btn-move-aside", name));
                }
                else if (f.Kind == "archive")
                {
                    x.Warn(x.T("folder-archive", name));
                    x.Explain(x.T("folder-archive-why"));
                    x.Advice(x.T("folder-archive-advice"));
                }
                else if (f.Kind == "partial")
                {
                    x.Warn(x.T("folder-partial", name));
                    x.Advice(x.T("folder-partial-advice"));
                    x.FixButton("doctor:move-file", arg, x.T("btn-move-aside", name));
                }
                else if (f.InnerZip != null)
                {
                    x.Problem(x.T("folder-innerzip", name, f.InnerZip));
                    x.Explain(x.T("folder-innerzip-why"));
                    x.FixButton("doctor:unwrap", arg, x.T("btn-unwrap", f.InnerZip));
                }
                else if (f.NestedFolder != null)
                {
                    x.Problem(x.T("folder-nested", name, f.NestedFolder));
                    x.Explain(f.SourceCode ? x.T("folder-source-why") : x.T("folder-nested-why"));
                    if (f.Kind == "zip" && !f.SourceCode) x.FixButton("doctor:repack", arg, x.T("btn-repack", name));
                    else x.Advice(f.SourceCode ? x.T("folder-source-advice") : x.T("folder-nested-advice", f.NestedFolder));
                }
                else if (f.NoModInfo)
                {
                    x.Warn(f.SourceCode ? x.T("folder-source", name) : x.T("folder-nomodinfo", name));
                    x.Explain(f.SourceCode ? x.T("folder-source-why") : x.T("folder-nomodinfo-why"));
                    x.FixButton("doctor:move-file", arg, x.T("btn-move-aside", name));
                }
                else if (f.ModInfoError != null)
                {
                    x.Problem(x.T("folder-modinfo", name));
                    x.Detail(Anonymise.Line(f.ModInfoError));
                    x.Explain(x.T("folder-modinfo-why"));
                }
                else if (f.Backslashes && !windows)
                {
                    x.Problem(x.T("folder-backslash", name));
                    x.Explain(x.T("folder-backslash-why"));
                    x.FixButton("doctor:repack", arg, x.T("btn-repack", name));
                }
                else if (f.Backslashes && full)
                {
                    x.Info(x.T("folder-backslash-windows", name));
                    x.Explain(x.T("folder-backslash-why"));
                    x.FixButton("doctor:repack", arg, x.T("btn-repack", name));
                }
            }

            foreach (var (id, copies) in scan.Duplicates())
            {
                x.Warn(x.T("folder-duplicate", id, copies.Count, string.Join(", ", copies.Select(c => c.Version))));
                foreach (ModFile c in copies) x.Detail(c.Name + " (" + c.Version + (c.Loaded ? ", " + x.T("folder-loaded") : "") + ")");
                x.Explain(x.T("folder-duplicate-why"));
                x.FixButton("doctor:move-duplicates", id, x.T("btn-move-older", copies.Count - 1));
            }

            // Mods that ask for a newer game than the one running.
            string game = GameVersion.ShortGameVersion;
            var tooNew = scan.Files.Where(f => !string.IsNullOrEmpty(f.GameDep) && f.GameDep != "*" && f.ReadError == null && Versions.Compare(f.GameDep, game) > 0).ToList();
            if (tooNew.Count > 0)
            {
                x.Warn(x.T("folder-toonew", tooNew.Count, game));
                foreach (ModFile f in tooNew.Take(20)) x.Detail(x.T("folder-toonew-line", f.ModName ?? f.ModId ?? f.Name, f.Version, f.GameDep));
                x.Explain(x.T("folder-toonew-why"));
            }

            if (full)
            {
                string install = SafeFull(System.IO.Path.Combine(GamePaths.Binaries, "Mods"));
                var inInstall = scan.Files.Where(f => string.Equals(SafeFull(f.Folder), install, StringComparison.OrdinalIgnoreCase)).ToList();
                if (inInstall.Count > 0)
                {
                    x.Info(x.T("folder-install", inInstall.Count));
                    foreach (ModFile f in inInstall.Take(12)) x.Detail(f.Name);
                    x.Explain(x.T("folder-install-why"));
                }
            }

            if (deep)
            {
                int tested = 0;
                foreach (ModFile f in scan.Files.Where(f => f.Kind == "zip" && f.ReadError == null))
                {
                    tested++;
                    string error = FolderScan.TestZip(f.Path);
                    if (error == null) continue;
                    x.Problem(x.T("folder-damaged", f.Name));
                    x.Detail(Anonymise.Line(error));
                    x.Advice(x.T("folder-unreadable-advice"));
                }
                x.Line(x.T("folder-tested", tested));
            }

            foreach (string e in scan.Errors) x.Line(Anonymise.Line(e));
            if (x.Problems + x.Warnings == before)
            {
                x.Ok(x.T("folder-clean", scan.Files.Count, scan.Folders.Count));
                if (full) foreach (string folder in scan.Folders) x.Detail(Anonymise.Path(folder));
            }
            else if (!full) x.LinkButton(TopicFiles, "", x.T("btn-files"));
            if (full && !x.Compact) x.Buttons(x.FolderToken("mods", x.T("btn-open-mods")));
        }

        static string SafeFull(string path)
        {
            try { return Path.GetFullPath(path ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { return path ?? ""; }
        }

        /// <summary>A file in a Mods folder as a fix button's argument: the folder's number, then the name ("0/broken.zip").</summary>
        public static string FileArg(FolderScan scan, ModFile f) => scan.Folders.IndexOf(f.Folder) + "/" + f.Name;

        /// <summary>The file a fix button's argument names; null unless it is a top-level entry of a Mods folder.</summary>
        public static ModFile FileFromArg(FolderScan scan, string arg)
        {
            if (scan == null || string.IsNullOrEmpty(arg)) return null;
            int slash = arg.IndexOf('/');
            if (slash <= 0 || !int.TryParse(arg.Substring(0, slash), out int index) || index < 0 || index >= scan.Folders.Count) return null;
            string name = arg.Substring(slash + 1);
            return scan.Files.FirstOrDefault(f => f.Folder == scan.Folders[index] && f.Name == name);
        }

        /// <summary>Config files that can't be read: the mod they belong to falls back to defaults, or fails to start.</summary>
        public static void Configs(DoctorContext x, bool full)
        {
            ConfigScan scan = x.Ev.Configs;
            if (full) x.Section(x.T("sec-configs"));
            if (scan == null) { if (full) x.Info(x.T("folder-notscanned")); return; }
            if (scan.Error != null) { x.Info(x.T("configs-error", Anonymise.Line(scan.Error))); return; }
            var bad = scan.Files.Where(f => f.Bad).ToList();
            foreach (ConfigFile f in bad)
            {
                string owner = f.Mod != null ? x.T("configs-owner", x.ModName(f.Mod)) : "";
                x.Problem((f.Empty ? x.T("configs-empty", f.Name) : x.T("configs-broken", f.Name)) + owner);
                if (f.Error != null) x.Detail(Anonymise.Line(f.Error));
                x.Explain(f.Game ? x.T("configs-game-why") : f.Empty ? x.T("configs-empty-why") : x.T("configs-broken-why"));
                if (f.Game) x.Advice(x.T("configs-game-advice", f.Name));
                else
                {
                    x.Advice(x.T("configs-advice"));
                    x.FixButton("doctor:reset-config", f.Name, x.T("btn-reset-config", f.Name));
                }
            }
            if (bad.Count == 0)
            {
                x.Ok(x.T("configs-clean", scan.Files.Count));
                return;
            }
            if (full && !x.Compact) x.Buttons(x.FolderToken("modconfig", x.T("btn-open-modconfig")));
        }
    }
}
