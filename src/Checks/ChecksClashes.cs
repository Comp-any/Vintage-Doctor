using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VintageDoctor
{
    /// <summary>
    /// The ways mods get in each other's way, or in their own: blocks and items whose code is missing, files the game
    /// couldn't read, a mod whose things never made it into the game, code the game leaves out, patches for files that
    /// aren't there, libraries brought along that the game (or another mod) already has, two mods replacing the same
    /// file, a mod replacing the game's files outright, and recipes with the same ingredients that make different things.
    /// </summary>
    public static partial class Checks
    {
        public const string TopicClashes = "clashes";

        /// <summary>
        /// Everything on this page (the check-up shows only what was found). What the world's content says (blocks,
        /// items, recipes, patches) is the same on both sides, so the server says it when there is one; what is read
        /// from the Mods folder is said by whichever side owns the folder (in single player, the player's game).
        /// </summary>
        public static void Clashes(DoctorContext x, bool full)
        {
            bool server = x.Side == EnumAppSide.Server;
            bool worldParts = server || !x.Doctor.ServerAnswers;
            bool folderParts = !(server && x.SameProcess);
            // Only where this side's mods are the ones that fill the world: a player on someone else's server has mods the server doesn't.
            bool ownsWorld = server || x.SameProcess;
            if (!worldParts && !folderParts) return;
            if (full) x.Section(x.T("sec-clashes"));
            int before = x.Problems + x.Warnings;
            if (worldParts)
            {
                Guarded(x, "missing code", () => MissingClasses(x, full));
                Guarded(x, "not added", () => NotAdded(x, full));
                Guarded(x, "unreadable files", () => UnreadableTypes(x, full));
                Guarded(x, "patch targets", () => PatchTargets(x, full));
            }
            if (folderParts)
            {
                Guarded(x, "ignored code", () => IgnoredCode(x, full));
                if (ownsWorld) Guarded(x, "content not loaded", () => ContentNotLoaded(x, full));
                Guarded(x, "bundled libraries", () => BundledLibraries(x, full));
                Guarded(x, "replaced files", () => ReplacedFiles(x, full));
                Guarded(x, "old mods", () => OldCodeMods(x, full));
            }
            if (full && worldParts) Guarded(x, "recipes", () => RecipeShadows(x, null, true));
            if (!full && x.Problems + x.Warnings > before) x.LinkButton(TopicClashes, "", x.T("btn-clashes"));
        }

        /// <summary>The loaded mods' files, one per mod id (a spare older copy in the folder doesn't count).</summary>
        static List<ModFile> LoadedFiles(DoctorContext x)
        {
            FolderScan scan = x.Ev.Folder;
            if (scan == null) return new List<ModFile>();
            return scan.Files.Where(f => f.Loaded && !string.IsNullOrEmpty(f.ModId) && !ModSnapshot.IsGameMod(f.ModId)).GroupBy(f => f.ModId, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        }

        /// <summary>Whether this session has anything bad to say about a mod: it failed to start, wrote errors, or is named in the last crash.</summary>
        static bool HasErrors(DoctorContext x, string modId)
        {
            if (string.IsNullOrEmpty(modId)) return false;
            LogScan log = x.Ev.Log;
            if (log != null)
            {
                if (log.StartFailures.Any(f => string.Equals(f.Mod, modId, StringComparison.OrdinalIgnoreCase))) return true;
                if (log.For(modId).Any(i => i.Bad && (i.Known == null || i.Known.Level > API.DoctorLevel.Info))) return true;
            }
            return x.Ev.Crash?.Latest?.Culprits.Contains(modId) ?? false;
        }

        static string DomainOf(string code)
        {
            int colon = code == null ? -1 : code.IndexOf(':');
            return colon > 0 ? code.Substring(0, colon) : "game";
        }

        static string OwnerName(DoctorContext x, string domain) => ModSnapshot.IsGameMod(domain) ? x.T("the-game") : x.ModName(domain);

        // ---------------------------------------------------------------- blocks and items whose code is missing

        /// <summary>
        /// A block or item is a file that says what it is, and often names a piece of code that makes it do things
        /// ("class", and for blocks that keep data, "entityClass"). When no loaded mod has that code - the mod's code
        /// failed to start, or it needs a library mod that isn't installed - the game says so in the log, makes a plain
        /// block or item instead and carries on: the thing exists but does nothing.
        /// </summary>
        public static void MissingClasses(DoctorContext x, bool full, string onlyMod = null)
        {
            var byMod = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var names = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var things = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void Found(string code, string kind, string className)
            {
                if (!seen.Add(kind + "|" + code + "|" + className)) return;
                string domain = DomainOf(code);
                if (!byMod.TryGetValue(domain, out List<string> list))
                {
                    byMod[domain] = list = new List<string>();
                    names[domain] = new List<string>();
                    things[domain] = new HashSet<string>(StringComparer.Ordinal);
                }
                string what = kind == "entity" ? x.T("missing-entityclass") : kind == "behavior" ? x.T("missing-behavior") : x.T("missing-class");
                list.Add(code + " (" + what + " " + className + ")");
                things[domain].Add(code);
                if (!names[domain].Contains(className)) names[domain].Add(className);
            }

            // What the game said while loading (the only place a missing "entityClass" shows: the game drops it).
            LogScan log = x.Ev.Log;
            if (log != null) foreach (var m in log.MissingClasses) Found(m.code, m.kind, m.className);

            // And what is in the game now.
            var known = new Dictionary<string, bool>(StringComparer.Ordinal);
            bool Registered(string key, System.Func<Type> lookup)
            {
                if (known.TryGetValue(key, out bool ok)) return ok;
                try { ok = lookup() != null; } catch { ok = true; } // can't tell: say nothing
                known[key] = ok;
                return ok;
            }
            IWorldAccessor world = x.Api.World;
            foreach (Block b in world.Blocks)
            {
                if (b?.Code == null || b.IsMissing || b.Id == 0) continue;
                string cls = b.Class;
                if (!string.IsNullOrEmpty(cls) && !Registered("b:" + cls, () => x.Api.ClassRegistry.GetBlockClass(cls))) Found(b.Code.ToString(), "block", cls);
            }
            foreach (Item i in world.Items)
            {
                if (i?.Code == null || i.IsMissing || i.Id == 0) continue;
                string cls = i.Class;
                if (!string.IsNullOrEmpty(cls) && !Registered("i:" + cls, () => x.Api.ClassRegistry.GetItemClass(cls))) Found(i.Code.ToString(), "item", cls);
            }

            int shown = full ? 30 : 5;
            foreach (var kv in byMod.OrderByDescending(kv => kv.Value.Count))
            {
                if (onlyMod != null && !string.Equals(kv.Key, onlyMod, StringComparison.OrdinalIgnoreCase)) continue;
                bool startFailed = log != null && log.StartFailures.Any(f => string.Equals(f.Mod, kv.Key, StringComparison.OrdinalIgnoreCase));
                x.Problem(x.T("missing-title", OwnerName(x, kv.Key), things[kv.Key].Count, string.Join(", ", names[kv.Key].Take(8)) + (names[kv.Key].Count > 8 ? ", ..." : "")));
                foreach (string line in kv.Value.Take(shown)) x.Detail(line);
                if (kv.Value.Count > shown) x.Detail(x.T("more-lines", kv.Value.Count - shown));
                x.Explain(startFailed ? x.T("missing-why-startfailed", x.ModName(kv.Key)) : x.T("missing-why"));
                x.Advice(x.T("missing-advice"));
                if (onlyMod == null && x.Ev.Mods.Has(kv.Key)) ModButtons(x, kv.Key, false);
            }
            if (byMod.Count == 0 && full && onlyMod == null) x.Ok(x.T("missing-none"));
        }

        // ---------------------------------------------------------------- blocks and items the game refused to add

        /// <summary>Blocks and items the game refused while loading, most often because another one of the same name was already there.</summary>
        public static void NotAdded(DoctorContext x, bool full, string onlyMod = null)
        {
            LogScan log = x.Ev.Log;
            if (log == null || log.FailedRegistrations.Count == 0) return;
            int shown = full ? 20 : 4;
            foreach (var g in log.FailedRegistrations.GroupBy(r => DomainOf(r.code)).OrderByDescending(g => g.Count()))
            {
                if (onlyMod != null && !string.Equals(g.Key, onlyMod, StringComparison.OrdinalIgnoreCase)) continue;
                var codes = g.Select(r => r.code).Distinct().ToList();
                x.Warn(x.T("notadded-title", OwnerName(x, g.Key), codes.Count));
                foreach (string code in codes.Take(shown)) x.Detail(code);
                if (codes.Count > shown) x.Detail(x.T("more-lines", codes.Count - shown));
                x.Explain(x.T("notadded-why"));
                if (onlyMod == null && x.Ev.Mods.Has(g.Key)) ModButtons(x, g.Key, false);
            }
        }

        // ---------------------------------------------------------------- block and item files the game couldn't read

        /// <summary>Block, item and creature files with a mistake in them: the game skips the file, so what it describes isn't in the game.</summary>
        public static void UnreadableTypes(DoctorContext x, bool full, string onlyMod = null)
        {
            LogScan log = x.Ev.Log;
            if (log == null || log.BrokenTypes.Count == 0) return;
            int shown = full ? 20 : 4;
            foreach (var kv in log.BrokenTypes.OrderByDescending(kv => kv.Value.Count))
            {
                if (onlyMod != null && !string.Equals(kv.Key, onlyMod, StringComparison.OrdinalIgnoreCase)) continue;
                x.Warn(x.T("types-broken", OwnerName(x, kv.Key), kv.Value.Count));
                foreach (string file in kv.Value.Take(shown)) x.Detail(kv.Key + ":" + file);
                if (kv.Value.Count > shown) x.Detail(x.T("more-lines", kv.Value.Count - shown));
                x.Explain(x.T("types-broken-why"));
                if (onlyMod == null && x.Ev.Mods.Has(kv.Key)) ModButtons(x, kv.Key, false);
            }
        }

        // ---------------------------------------------------------------- patches for files that aren't there

        /// <summary>
        /// A mod's patches for files that don't exist. For a mod that isn't installed that's how mods support each
        /// other, and harmless; for the game's files or an installed mod's, the file was renamed or removed in an
        /// update and the patch has stopped doing anything.
        /// </summary>
        public static void PatchTargets(DoctorContext x, bool full, string onlyMod = null)
        {
            LogScan log = x.Ev.Log;
            if (log == null) return;
            int found = 0, shown = full ? 20 : 4;
            foreach (var kv in log.MissingTargets.OrderByDescending(kv => kv.Value.Count))
            {
                if (onlyMod != null && !string.Equals(kv.Key, onlyMod, StringComparison.OrdinalIgnoreCase)) continue;
                var real = kv.Value.Where(t => ModSnapshot.IsGameMod(DomainOf(t)) || x.Ev.Mods.Has(DomainOf(t))).ToList();
                var optional = kv.Value.Where(t => !real.Contains(t)).ToList();
                if (real.Count > 0)
                {
                    found++;
                    x.Warn(x.T("targets-missing", x.ModName(kv.Key), real.Count));
                    foreach (string t in real.Take(shown)) x.Detail(t);
                    if (real.Count > shown) x.Detail(x.T("more-lines", real.Count - shown));
                    x.Explain(x.T("targets-missing-why"));
                    if (onlyMod == null && x.Ev.Mods.Has(kv.Key)) ModButtons(x, kv.Key, false);
                }
                if (optional.Count > 0 && full)
                {
                    found++;
                    x.Info(x.T("targets-optional", x.ModName(kv.Key), optional.Count, string.Join(", ", optional.Select(DomainOf).Distinct().Take(10))));
                    x.Explain(x.T("targets-optional-why"));
                }
            }
            if (found == 0 && full && onlyMod == null) x.Ok(x.T("targets-none"));
        }

        // ---------------------------------------------------------------- code the game leaves out

        /// <summary>A mod that has code in it but calls itself a content mod: the game only runs code from mods of type "code".</summary>
        public static void IgnoredCode(DoctorContext x, bool full)
        {
            foreach (ModFile f in LoadedFiles(x))
            {
                if (f.Dlls.Count == 0 || string.IsNullOrEmpty(f.Type) || string.Equals(f.Type, "code", StringComparison.OrdinalIgnoreCase)) continue;
                x.Warn(x.T("ignoredcode-title", x.ModName(f.ModId), f.Type.ToLowerInvariant(), string.Join(", ", f.Dlls.Take(6))));
                x.Explain(x.T("ignoredcode-why"));
                x.Advice(x.T("ignoredcode-advice"));
                ModButtons(x, f.ModId, false);
            }
        }

        // ---------------------------------------------------------------- a mod whose things aren't in the game

        /// <summary>A mod that has block and item files, none of which made it into the game.</summary>
        public static void ContentNotLoaded(DoctorContext x, bool full)
        {
            List<ModFile> files = LoadedFiles(x);
            if (files.Count == 0) return;
            IWorldAccessor world = x.Api.World;
            var inGame = new HashSet<string>(StringComparer.Ordinal);
            foreach (Block b in world.Blocks) if (b?.Code != null && !b.IsMissing) inGame.Add(b.Code.Domain);
            foreach (Item i in world.Items) if (i?.Code != null && !i.IsMissing) inGame.Add(i.Code.Domain);
            int found = 0;
            foreach (ModFile f in files)
            {
                // Block and item files in the mod's own domain (what it adds to the game's domain shows up as the game's).
                int typeFiles = f.OwnData.Count(p => p.StartsWith("blocktypes/", StringComparison.Ordinal) || p.StartsWith("itemtypes/", StringComparison.Ordinal));
                if (typeFiles == 0 || inGame.Contains(f.ModId.ToLowerInvariant())) continue;
                found++;
                bool slashes = f.Backslashes && Path.DirectorySeparatorChar != '\\';
                x.Warn(x.T("notloaded-title", x.ModName(f.ModId), typeFiles));
                x.Explain(slashes ? x.T("folder-backslash-why") : x.T("notloaded-why"));
                if (slashes && x.Ev.Folder != null) x.FixButton("doctor:repack", FileArg(x.Ev.Folder, f), x.T("btn-repack", f.Name));
                ModButtons(x, f.ModId, false);
            }
            if (found == 0 && full) x.Ok(x.T("notloaded-none"));
        }

        // ---------------------------------------------------------------- libraries brought along

        /// <summary>The code libraries the game itself comes with ("0Harmony.dll", "Newtonsoft.Json.dll"...), by file name.</summary>
        public static HashSet<string> GameLibraries()
        {
            var game = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "VSEssentials.dll", "VSSurvivalMod.dll", "VSCreativeMod.dll" };
            try
            {
                foreach (string dir in new[] { GamePaths.Binaries, Path.Combine(GamePaths.Binaries, "Lib") })
                    if (Directory.Exists(dir)) foreach (string dll in Directory.GetFiles(dir, "*.dll")) game.Add(Path.GetFileName(dll));
            }
            catch { }
            return game;
        }

        /// <summary>
        /// A mod that carries its own copy of a library the game already has (Harmony, Newtonsoft.Json, protobuf...),
        /// and the same library in two mods: only one copy can be loaded, and whichever mod wanted the other one fails
        /// with "method not found" or "could not load file or assembly". A warning when the mod has errors, a note
        /// (on the full page) when it doesn't.
        /// </summary>
        public static void BundledLibraries(DoctorContext x, bool full)
        {
            FolderScan scan = x.Ev.Folder;
            if (scan == null) return;
            HashSet<string> game = GameLibraries();
            List<ModFile> files = LoadedFiles(x);
            int found = 0;
            foreach (ModFile f in files)
            {
                var own = f.Dlls.Where(d => game.Contains(d)).ToList();
                if (own.Count == 0) continue;
                bool errors = HasErrors(x, f.ModId);
                if (!errors && !full) continue;
                found++;
                string title = x.T("bundled-title", x.ModName(f.ModId), string.Join(", ", own));
                if (errors) x.Warn(title + " " + x.T("and-has-errors")); else x.Info(title);
                x.Explain(x.T("bundled-why"));
                if (f.Kind == "zip") x.FixButton("doctor:strip-libraries", FileArg(scan, f), x.T("btn-strip", f.Name));
                else x.Advice(x.T("bundled-advice"));
                ModButtons(x, f.ModId, false);
            }
            var shared = files.SelectMany(f => f.Dlls.Where(d => !game.Contains(d)).Select(d => (dll: d, mod: f.ModId)))
                .GroupBy(t => t.dll, StringComparer.OrdinalIgnoreCase).Where(g => g.Select(t => t.mod).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1).ToList();
            foreach (var g in shared.Take(full ? 30 : 4))
            {
                var mods = g.Select(t => t.mod).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                bool errors = mods.Any(m => HasErrors(x, m));
                if (!errors && !full) continue;
                found++;
                string title = x.T("shared-dll-title", g.Key, string.Join(", ", mods.Select(x.ModName)));
                if (errors) x.Warn(title + " " + x.T("and-has-errors")); else x.Info(title);
                x.Explain(x.T("shared-dll-why"));
            }
            if (found == 0 && full) x.Ok(x.T("bundled-none"));
        }

        // ---------------------------------------------------------------- files replaced outright

        static bool GameHas(string path)
        {
            try
            {
                string root = GamePaths.AssetsPath;
                if (string.IsNullOrEmpty(root)) return false;
                string rel = path.Replace('/', Path.DirectorySeparatorChar);
                return File.Exists(Path.Combine(root, "survival", rel)) || File.Exists(Path.Combine(root, "game", rel)) || File.Exists(Path.Combine(root, "creative", rel));
            }
            catch { return false; }
        }

        /// <summary>Files where the wrong copy breaks things rather than looks: data (JSON) and shaders. The rest (textures, shapes, sounds) is looks.</summary>
        static bool Matters(string asset)
        {
            if (asset.EndsWith(".json", StringComparison.Ordinal)) return !asset.Contains(":shapes/") && !asset.Contains(":textures/") && !asset.Contains(":sounds/");
            return asset.Contains(":shaders/") || asset.Contains(":shaderincludes/");
        }

        /// <summary>The game's own files a mod replaces whole (it has a file of the same name in the game's folder).</summary>
        static List<string> GameFilesReplaced(ModFile f)
        {
            return f.ForeignAssets.Where(a => a.StartsWith("game:", StringComparison.Ordinal) && Matters(a) && GameHas(a.Substring(5))).ToList();
        }

        /// <summary>
        /// Files that more than one mod replaces (only one copy wins, and nothing says so), and mods that replace the
        /// game's own files outright instead of patching them (what the game's updates and other mods' patches
        /// changed in those files is lost).
        /// </summary>
        public static void ReplacedFiles(DoctorContext x, bool full)
        {
            List<ModFile> files = LoadedFiles(x);
            if (files.Count == 0) return;
            var byId = new Dictionary<string, ModFile>(StringComparer.OrdinalIgnoreCase);
            foreach (ModFile f in files) byId[f.ModId] = f;
            var owners = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (ModFile f in files)
                foreach (string asset in f.ForeignAssets)
                {
                    if (!owners.TryGetValue(asset, out List<string> list)) owners[asset] = list = new List<string>();
                    if (!list.Contains(f.ModId)) list.Add(f.ModId);
                }
            // A file one mod puts into another mod's folder, where that mod has the same file: a replacement too.
            foreach (var kv in owners)
            {
                string domain = DomainOf(kv.Key), path = kv.Key.Substring(kv.Key.IndexOf(':') + 1);
                if (byId.TryGetValue(domain, out ModFile owner) && owner.OwnData.Contains(path) && !kv.Value.Contains(owner.ModId)) kv.Value.Add(owner.ModId);
            }
            var clashes = owners.Where(kv => kv.Value.Count > 1).OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
            var dataClashes = clashes.Where(kv => Matters(kv.Key)).ToList();
            int found = 0, shown = full ? 40 : 5;
            if (dataClashes.Count > 0)
            {
                found++;
                x.Warn(x.T("replaced-clash", dataClashes.Count));
                foreach (var kv in dataClashes.Take(shown)) x.Detail(kv.Key + " ← " + string.Join(", ", kv.Value.Select(x.ModName)));
                if (dataClashes.Count > shown) x.Detail(x.T("more-lines", dataClashes.Count - shown));
                x.Explain(x.T("replaced-clash-why"));
                x.Advice(x.T("replaced-clash-advice"));
            }
            var lookClashes = clashes.Where(kv => !Matters(kv.Key)).ToList();
            if (lookClashes.Count > 0 && full)
            {
                found++;
                x.Info(x.T("replaced-look", lookClashes.Count));
                foreach (var kv in lookClashes.Take(20)) x.Detail(kv.Key + " ← " + string.Join(", ", kv.Value.Select(x.ModName)));
                if (lookClashes.Count > 20) x.Detail(x.T("more-lines", lookClashes.Count - 20));
                x.Explain(x.T("replaced-look-why"));
            }

            // The game's own files replaced whole: a warning when the mod was made for an older game.
            string game = GameVersion.ShortGameVersion;
            Versions.MajorMinor(game, out int gameMajor, out int gameMinor);
            var old = new List<(ModFile f, List<string> replaced)>();
            foreach (ModFile f in files)
            {
                List<string> replaced = GameFilesReplaced(f);
                if (replaced.Count == 0) continue;
                bool isOld = Versions.MajorMinor(f.GameDep, out int major, out int minor) && (major < gameMajor || (major == gameMajor && minor < gameMinor));
                if (isOld) { old.Add((f, replaced)); continue; }
                if (!full) continue;
                found++;
                x.Info(x.T("replaced-game", x.ModName(f.ModId), replaced.Count));
                foreach (string a in replaced.Take(25)) x.Detail(a);
                if (replaced.Count > 25) x.Detail(x.T("more-lines", replaced.Count - 25));
                x.Explain(x.T("replaced-game-why"));
            }
            if (old.Count > 0)
            {
                found++;
                int each = full ? 12 : 2;
                x.Warn(x.T("replaced-game-old", old.Count, old.Sum(o => o.replaced.Count), game));
                foreach (var o in old.Take(full ? 20 : 4))
                {
                    x.Detail(x.T("replaced-game-old-line", x.ModName(o.f.ModId), o.f.GameDep, o.replaced.Count));
                    foreach (string a in o.replaced.Take(each)) x.Detail(a, 2);
                    if (o.replaced.Count > each) x.Detail(x.T("more-lines", o.replaced.Count - each), 2);
                }
                x.Explain(x.T("replaced-game-old-why"));
                x.Advice(x.T("replaced-game-old-advice"));
                x.Buttons(old.Take(4).Select(o => DoctorReport.Open(TopicMod, o.f.ModId, x.T("btn-modpage", x.ModName(o.f.ModId)))).ToArray());
            }
            if (found == 0 && full) x.Ok(x.T("replaced-none"));
        }

        // ---------------------------------------------------------------- code mods made for an older game, with errors now

        /// <summary>Mods with code that were made for an older version of the game and have errors now: the ones to update first.</summary>
        public static void OldCodeMods(DoctorContext x, bool full)
        {
            string game = GameVersion.ShortGameVersion;
            if (!Versions.MajorMinor(game, out int gameMajor, out int gameMinor)) return;
            var old = new List<(ModEntry mod, string made, bool errors)>();
            foreach (ModEntry m in x.Ev.Mods.UserMods)
            {
                if (m.Assemblies.Count == 0) continue;
                string dep = null;
                foreach (var d in m.Deps) if (d.id == "game") dep = d.version;
                if (!Versions.MajorMinor(dep, out int major, out int minor)) continue;
                if (major > gameMajor || (major == gameMajor && minor >= gameMinor)) continue;
                old.Add((m, dep, HasErrors(x, m.Id)));
            }
            var broken = old.Where(o => o.errors).ToList();
            if (broken.Count > 0)
            {
                x.Warn(x.T("oldmods-broken", broken.Count, game, string.Join(", ", broken.Take(8).Select(o => o.mod.Name + " (" + o.made + ")"))));
                x.Explain(x.T("oldmods-why"));
                x.Advice(x.T("oldmods-advice"));
                x.Buttons(broken.Take(4).Select(o => DoctorReport.Open(TopicMod, o.mod.Id, x.T("btn-modpage", o.mod.Name))).ToArray());
            }
            var fine = old.Where(o => !o.errors).ToList();
            if (full && fine.Count > 0)
            {
                x.Info(x.T("oldmods-fine", fine.Count, game));
                x.Detail(string.Join(", ", fine.Take(40).Select(o => o.mod.Name + " (" + o.made + ")")));
                x.Explain(x.T("oldmods-fine-why"));
            }
        }

        // ---------------------------------------------------------------- recipes with the same ingredients

        static string RecipeSignature(GridRecipe r)
        {
            var cells = new List<string>();
            foreach (CraftingRecipeIngredient ing in r.ResolvedIngredients ?? Array.Empty<CraftingRecipeIngredient>())
            {
                if (ing == null) { cells.Add("_"); continue; }
                string variants = ing.AllowedVariants == null ? "" : string.Join(",", ing.AllowedVariants);
                string attrs = ing.Attributes?.ToString() ?? "";
                string code = ing.Code?.ToString() ?? "?";
                // An ingredient matched by tags has no code: its own description tells two of them apart.
                if (code == "*:*") code = ing.ToString() ?? code;
                cells.Add(ing.Type + ":" + code + "*" + ing.Quantity + (ing.IsTool ? "T" : "") + "[" + variants + "]" + attrs);
            }
            if (r.Shapeless) cells.Sort(StringComparer.Ordinal);
            return (r.Shapeless ? "S" : r.Width + "x" + r.Height) + "|" + (r.RequiresTrait ?? "") + "|" + string.Join("|", cells);
        }

        static string OutputOf(GridRecipe r)
        {
            CollectibleObject c = r.Output?.ResolvedItemStack?.Collectible;
            return c != null ? DoctorReport.Ref(c) : r.Output?.Code?.ToString() ?? "?";
        }

        /// <summary>
        /// Crafting-grid recipes with exactly the same ingredients in the same places that make different things: the
        /// grid can only offer one of them, so the others can never be crafted. With <paramref name="about"/>, only
        /// those that involve that block or item (for the "why can't I craft it" page).
        /// </summary>
        public static int RecipeShadows(DoctorContext x, CollectibleObject about, bool full)
        {
            List<GridRecipe> recipes;
            try { recipes = x.Api.World.GridRecipes.Where(r => r != null && r.Enabled && r.Output != null && r.ResolvedIngredients != null).ToList(); }
            catch (Exception e) { if (full && about == null) x.Info(x.T("craft-error", e.Message)); return 0; }
            var groups = new Dictionary<string, List<GridRecipe>>(StringComparer.Ordinal);
            foreach (GridRecipe r in recipes)
            {
                string sig;
                try { sig = RecipeSignature(r); } catch { continue; }
                if (!groups.TryGetValue(sig, out List<GridRecipe> list)) groups[sig] = list = new List<GridRecipe>();
                list.Add(r);
            }
            var shadows = new List<List<GridRecipe>>();
            foreach (List<GridRecipe> g in groups.Values)
            {
                if (g.Count < 2) continue;
                if (g.Select(r => (r.Output.Code?.ToString() ?? "?") + "*" + r.Output.Quantity).Distinct().Count() < 2) continue;
                // The game's own recipes among themselves are the game's business; a mod has to be in it.
                if (about == null && g.All(r => r.Name == null || ModSnapshot.IsGameMod(r.Name.Domain))) continue;
                if (about != null && !g.Any(r => r.Output.ResolvedItemStack?.Collectible == about)) continue;
                shadows.Add(g);
            }
            if (shadows.Count == 0)
            {
                if (full && about == null) x.Ok(x.T("shadow-none", recipes.Count));
                return 0;
            }
            int shown = full ? 30 : 5;
            x.Warn(about == null ? x.T("shadow-title", shadows.Count) : x.T("shadow-title-item"));
            foreach (List<GridRecipe> g in shadows.Take(shown))
            {
                x.Detail(Ingredients(g[0]) + " →");
                foreach (GridRecipe r in g.Take(6))
                    x.Detail(OutputOf(r) + (r.Output.Quantity > 1 ? " ×" + r.Output.Quantity : "") + "  (" + (r.Name == null ? "?" : OwnerName(x, r.Name.Domain) + ", " + r.Name.Path) + ")", 2);
            }
            if (shadows.Count > shown) x.Detail(x.T("more-lines", shadows.Count - shown));
            x.Explain(x.T("shadow-why"));
            x.Advice(x.T("shadow-advice"));
            return shadows.Count;
        }

        // ---------------------------------------------------------------- things without a name

        /// <summary>
        /// Blocks and items from a mod that show as their code ("item-widget-iron") instead of a name: the mod's
        /// language file didn't load, or it has no English text for them. Only things that are in the creative
        /// inventory are counted (the rest aren't meant to be seen).
        /// </summary>
        public static void MissingNames(DoctorContext x)
        {
            x.Section(x.T("sec-names"));
            var total = new Dictionary<string, int>(StringComparer.Ordinal);
            var missing = new Dictionary<string, int>(StringComparer.Ordinal);
            var example = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (CollectibleObject c in x.Api.World.Collectibles)
            {
                if (c?.Code == null || c.IsMissing || ModSnapshot.IsGameMod(c.Code.Domain)) continue;
                if (c.CreativeInventoryTabs == null || c.CreativeInventoryTabs.Length == 0) continue;
                string domain = c.Code.Domain;
                total[domain] = total.TryGetValue(domain, out int n) ? n + 1 : 1;
                string name;
                try { name = c.GetHeldItemName(new ItemStack(c)); } catch { continue; }
                // Without a text the game shows the key it looked for.
                string key = (c is Block ? "block-" : "item-") + c.Code.Path;
                if (name == null || !name.EndsWith(key, StringComparison.Ordinal)) continue;
                missing[domain] = missing.TryGetValue(domain, out int m) ? m + 1 : 1;
                if (!example.ContainsKey(domain)) example[domain] = name;
            }
            int found = 0;
            foreach (var kv in missing.OrderByDescending(kv => kv.Value))
            {
                int all = total[kv.Key];
                if (kv.Value < 3 || kv.Value * 4 < all) continue;
                found++;
                x.Warn(x.T("names-missing", x.ModName(kv.Key), kv.Value, all, example[kv.Key]));
                x.Explain(x.T("names-why"));
                if (x.Ev.Mods.Has(kv.Key)) ModButtons(x, kv.Key, false);
            }
            if (found == 0) x.Ok(x.T("names-none"));
        }

        /// <summary>
        /// On a mod's own page: its blocks and items whose code is missing, its files the game couldn't read, its
        /// patches for files that aren't there, what it puts into the game's and other mods' folders, and the libraries it brings.
        /// </summary>
        static void ModFiles(DoctorContext x, ModEntry m)
        {
            if (x.Side == EnumAppSide.Server || !x.Doctor.ServerAnswers)
            {
                MissingClasses(x, true, m.Id);
                NotAdded(x, true, m.Id);
                UnreadableTypes(x, true, m.Id);
                PatchTargets(x, true, m.Id);
            }
            // The mod's file is the same seen from either side: in single player the player's game says it.
            if (x.Side == EnumAppSide.Server && x.SameProcess) return;
            ModFile f = x.Ev.Folder?.Find(m.Id);
            if (f == null) return;
            if (f.Dlls.Count > 0 && !string.IsNullOrEmpty(f.Type) && !string.Equals(f.Type, "code", StringComparison.OrdinalIgnoreCase))
            {
                x.Warn(x.T("ignoredcode-title", m.Name, f.Type.ToLowerInvariant(), string.Join(", ", f.Dlls.Take(6))));
                x.Explain(x.T("ignoredcode-why"));
                x.Advice(x.T("ignoredcode-advice"));
            }
            if (f.ForeignAssets.Count > 0)
            {
                List<string> replaced = GameFilesReplaced(f);
                var others = f.ForeignAssets.Where(a => !a.StartsWith("game:", StringComparison.Ordinal)).ToList();
                if (replaced.Count > 0 || others.Count > 0)
                {
                    x.Info(x.T("mod-foreign", replaced.Count, others.Count));
                    foreach (string a in replaced.Take(8)) x.Detail(a);
                    foreach (string a in others.Take(6)) x.Detail(a);
                    x.Explain(x.T("replaced-game-why"));
                }
            }
            if (f.Dlls.Count > 1) x.Info(x.T("mod-dlls", f.Dlls.Count, string.Join(", ", f.Dlls.Take(12))));
        }
    }
}
