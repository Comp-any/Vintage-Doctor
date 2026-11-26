using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using VintageDoctor.API;

namespace VintageDoctor
{
    /// <summary>
    /// What the doctor's own fix buttons do. Nothing is ever deleted: files go to ModData/vintagedoctor/backups, from
    /// where they can be put back by hand.
    /// </summary>
    public static class BuiltInFixes
    {
        public static void Register(VintageDoctorSystem doctor, ICoreAPI api)
        {
            void Add(string id, System.Func<DoctorFixContext, string> apply) => doctor.RegisterFix(new DoctorFix(DoctorApi.ModId, id, ctx => apply((DoctorFixContext)ctx)));
            Add("doctor:reset-config", ResetConfig);
            Add("doctor:repack", Repack);
            Add("doctor:strip-libraries", StripLibraries);
            Add("doctor:move-file", MoveFile);
            Add("doctor:move-duplicates", MoveDuplicates);
            Add("doctor:unwrap", Unwrap);
            Add("doctor:disable-mod", DisableMod);
            Add("doctor:profile", Profile);
            Add("doctor:netmeasure", NetMeasure);
            if (api.Side == EnumAppSide.Server) Add("doctor:remove-items", RemoveItems);
            else
            {
                Add("doctor:setting", Setting);
                Add("doctor:clear-chat", ClearChat);
            }
        }

        /// <summary>Clears every chat tab with the game's own .clearchat, which empties the lines and draws the chat again.</summary>
        static string ClearChat(DoctorFixContext x)
        {
            if (!(x.Api is Vintagestory.API.Client.ICoreClientAPI capi)) return x.Failed(x.T("fix-clear-chat-failed"));
            capi.TriggerChatMessage(".clearchat");
            return x.T("fix-clear-chat-done");
        }

        static string Backups(DoctorFixContext x)
        {
            return Path.Combine(x.Doctor.DataFolder, "backups", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture));
        }

        /// <summary>Moves a config file that can't be read out of the way: the mod writes a fresh one the next time it starts.</summary>
        static string ResetConfig(DoctorFixContext x)
        {
            string name = (x.Argument ?? "").Replace('\\', '/').Trim();
            if (name.Length == 0 || name.Contains("..") || Path.IsPathRooted(name) || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return x.Failed(x.T("fix-badfile", name));
            string root = Path.GetFullPath(GamePaths.ModConfig);
            string path = Path.GetFullPath(Path.Combine(root, name));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return x.Failed(x.T("fix-badfile", name));
            if (string.Equals(Path.GetFileName(path), VintageDoctorSystem.ConfigFile, StringComparison.OrdinalIgnoreCase)) return x.Failed(x.T("fix-ownconfig"));
            string dest = FolderScan.MoveAside(path, Backups(x));
            return x.T("fix-reset-config-done", name, Anonymise.Short(dest));
        }

        static ModFile FileOf(DoctorFixContext x, out string error)
        {
            error = null;
            ModFile f = Checks.FileFromArg(x.Ev?.Folder, x.Argument);
            if (f == null || (!File.Exists(f.Path) && !Directory.Exists(f.Path))) { error = x.Failed(x.T("fix-badfile", x.Argument)); return null; }
            return f;
        }

        /// <summary>Writes a mod zip again with "\" in its file names made "/" and a single outer folder taken away.</summary>
        static string Repack(DoctorFixContext x)
        {
            ModFile f = FileOf(x, out string error);
            if (f == null) return error;
            if (f.Kind != "zip") return x.Failed(x.T("fix-notzip", f.Name));
            int changed = FolderScan.Repack(f.Path, Backups(x), out string backup);
            return x.T("fix-repack-done", f.Name, changed, Anonymise.Short(backup));
        }

        /// <summary>
        /// Writes a mod zip again without the libraries it brought along that the game already has (its own copy of
        /// Harmony, Newtonsoft.Json...). The game uses its own copy either way; the mod's copy can only get in the way.
        /// </summary>
        static string StripLibraries(DoctorFixContext x)
        {
            ModFile f = FileOf(x, out string error);
            if (f == null) return error;
            if (f.Kind != "zip") return x.Failed(x.T("fix-notzip", f.Name));
            HashSet<string> game = Checks.GameLibraries();
            var drop = f.Dlls.Where(d => game.Contains(d)).ToList();
            if (drop.Count == 0) return x.Failed(x.T("fix-strip-none", f.Name));
            // The mod's own code must stay: with nothing but the game's libraries in it there would be no mod left.
            if (string.Equals(f.Type, "code", StringComparison.OrdinalIgnoreCase) && drop.Count == f.Dlls.Count) return x.Failed(x.T("fix-strip-all", f.Name));
            FolderScan.Repack(f.Path, Backups(x), out string backup, n => n.IndexOf('/') < 0 && game.Contains(n));
            return x.T("fix-strip-done", f.Name, string.Join(", ", drop), Anonymise.Short(backup));
        }

        static string MoveFile(DoctorFixContext x)
        {
            ModFile f = FileOf(x, out string error);
            if (f == null) return error;
            if (f.Loaded && f.ReadError == null && !f.NoModInfo) return x.Failed(x.T("fix-move-loaded", f.Name));
            string dest = FolderScan.MoveAside(f.Path, Backups(x));
            return x.T("fix-move-done", f.Name, Anonymise.Short(dest));
        }

        /// <summary>Of a mod installed more than once, keeps the newest copy and moves the others out of the Mods folder.</summary>
        static string MoveDuplicates(DoctorFixContext x)
        {
            var dup = x.Ev?.Folder?.Duplicates().FirstOrDefault(d => string.Equals(d.id, x.Argument, StringComparison.OrdinalIgnoreCase));
            if (dup == null || dup.Value.copies == null || dup.Value.copies.Count < 2) return x.Failed(x.T("fix-noduplicates", x.Argument));
            string backups = Backups(x);
            var moved = new List<string>();
            foreach (ModFile f in dup.Value.copies.Skip(1))
            {
                try { FolderScan.MoveAside(f.Path, backups); moved.Add(f.Name); }
                catch (Exception e) { return x.Failed(x.T("fix-move-failed", f.Name, e.Message)); }
            }
            return x.T("fix-duplicates-done", dup.Value.copies[0].Name, string.Join(", ", moved), Anonymise.Short(backups));
        }

        /// <summary>A zip that holds nothing but the real mod zip: takes the inner one out and puts the outer one aside.</summary>
        static string Unwrap(DoctorFixContext x)
        {
            ModFile f = FileOf(x, out string error);
            if (f == null) return error;
            if (f.InnerZip == null) return x.Failed(x.T("fix-notzip", f.Name));
            string innerName = Path.GetFileName(f.InnerZip.Replace('\\', '/'));
            string target = Path.Combine(f.Folder, innerName);
            if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(f.Path), StringComparison.OrdinalIgnoreCase)) target = Path.Combine(f.Folder, Path.GetFileNameWithoutExtension(innerName) + "-inner.zip");
            if (File.Exists(target)) return x.Failed(x.T("fix-exists", Path.GetFileName(target)));
            using (ZipArchive zip = ZipFile.OpenRead(f.Path))
            {
                ZipArchiveEntry entry = zip.Entries.First(e => e.FullName == f.InnerZip || e.FullName.Replace('\\', '/') == f.InnerZip);
                using Stream from = entry.Open();
                using FileStream to = File.Create(target);
                from.CopyTo(to);
            }
            ModFile inner = FolderScan.Look(target);
            if (inner == null || inner.Broken)
            {
                File.Delete(target);
                return x.Failed(x.T("fix-unwrap-bad", f.InnerZip));
            }
            string dest = FolderScan.MoveAside(f.Path, Backups(x));
            return x.T("fix-unwrap-done", Path.GetFileName(target), Anonymise.Short(dest));
        }

        /// <summary>
        /// Switches a mod off from the next start: on a player's computer the way the game's Mod Manager does (the
        /// "disabledMods" setting), on a dedicated server in the world's list of disabled mods.
        /// </summary>
        static string DisableMod(DoctorFixContext x)
        {
            ModEntry m = x.Ev?.Mods?.Get(x.Argument) ?? x.Doctor.Snapshot().Get(x.Argument);
            if (m == null) return x.Failed(x.T("fix-nomod", x.Argument));
            if (m.Core || m.Id == DoctorApi.ModId) return x.Failed(x.T("fix-disable-refused", m.Name));
            string key = m.Id + "@" + m.Version;
            if (x.Api is ICoreClientAPI capi)
            {
                List<string> list;
                try { list = capi.Settings.Strings["disabledMods"]; } catch { list = null; }
                list = list == null ? new List<string>() : new List<string>(list);
                if (!list.Contains(key)) list.Add(key);
                capi.Settings.Strings["disabledMods"] = list;
                return x.T("fix-disable-done-client", m.Name);
            }
            var sapi = x.Api as ICoreServerAPI;
            if (sapi == null) return x.Failed(x.T("fix-disable-manual", key));
            if (!sapi.Server.IsDedicated) return x.Failed(x.T("fix-disable-singleplayer", m.Name));
            // The server's list lives in its config (WorldConfig.DisabledMods), which the game's API doesn't hand out.
            try
            {
                object config = sapi.Server.Config;
                object world = config.GetType().GetProperty("WorldConfig", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(config);
                if (world == null) return x.Failed(x.T("fix-disable-manual", key));
                const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                FieldInfo field = world.GetType().GetField("DisabledMods", any);
                PropertyInfo prop = field == null ? world.GetType().GetProperty("DisabledMods", any) : null;
                var list = (field != null ? field.GetValue(world) : prop?.GetValue(world)) as IList;
                if (list == null)
                {
                    var fresh = new List<string>();
                    if (field != null && field.FieldType.IsAssignableFrom(typeof(List<string>))) field.SetValue(world, fresh);
                    else if (prop != null && prop.CanWrite && prop.PropertyType.IsAssignableFrom(typeof(List<string>))) prop.SetValue(world, fresh);
                    else return x.Failed(x.T("fix-disable-manual", key));
                    list = fresh;
                }
                if (!list.Contains(key)) list.Add(key);
                sapi.Server.MarkConfigDirty();
                return x.T("fix-disable-done-server", m.Name);
            }
            catch (Exception e) { return x.Failed(x.T("fix-disable-manual", key) + " (" + e.Message + ")"); }
        }

        /// <summary>Starts a measured run with the game's own tick profiler.</summary>
        static string Profile(DoctorFixContext x)
        {
            int.TryParse(x.Argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds);
            if (seconds <= 0) seconds = 20;
            TickProfile p = x.Doctor.Profile;
            if (p.Running) return x.T("fix-profile-running", p.SecondsLeft);
            if (!p.Start(x.Api.World, seconds)) return x.Failed(x.T("fix-profile-failed", p.Error ?? "?"));
            return x.T("fix-profile-started", p.Seconds);
        }

        /// <summary>Counts the network traffic by kind and by mod for a while (1.9.0).</summary>
        static string NetMeasure(DoctorFixContext x)
        {
            int.TryParse(x.Argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds);
            if (seconds <= 0) seconds = 20;
            NetWatch n = x.Doctor.Net;
            if (n == null) return x.Failed(x.T("fix-netmeasure-failed", "?"));
            if (n.Running) return x.T("fix-netmeasure-running", n.SecondsLeft);
            if (!n.Start(seconds)) return x.Failed(x.T("fix-netmeasure-failed", n.MeasureError ?? "?"));
            return x.T("fix-netmeasure-started", n.RunSeconds);
        }

        /// <summary>Removes the items lying on the ground in the loaded part of the world.</summary>
        static string RemoveItems(DoctorFixContext x)
        {
            var sapi = x.Api as ICoreServerAPI;
            if (sapi == null) return x.Failed("server only");
            int n = 0;
            foreach (Entity e in sapi.World.LoadedEntities.Values.ToList())
            {
                if (!(e is EntityItem) || !e.Alive) continue;
                e.Die(EnumDespawnReason.Expire);
                n++;
            }
            return x.T("fix-remove-items-done", n);
        }

        /// <summary>Changes one of the game's own settings on the player's computer: "int:viewDistance=256", "bool:bloom=false".</summary>
        static string Setting(DoctorFixContext x)
        {
            var capi = x.Api as ICoreClientAPI;
            string arg = x.Argument ?? "";
            int colon = arg.IndexOf(':'), eq = arg.IndexOf('=');
            if (capi == null || colon <= 0 || eq <= colon) return x.Failed(x.T("fix-setting-bad", arg));
            string type = arg.Substring(0, colon), key = arg.Substring(colon + 1, eq - colon - 1), value = arg.Substring(eq + 1);
            // Only the settings the doctor itself suggests: a button can't be made to change anything else.
            string[] allowed = { "viewDistance", "shadowMapQuality", "ssaoQuality", "godRays", "ssaa", "bloom", "glDebugMode", "occlusionculling" };
            if (!allowed.Contains(key)) return x.Failed(x.T("fix-setting-bad", arg));
            switch (type)
            {
                case "int":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) return x.Failed(x.T("fix-setting-bad", arg));
                    capi.Settings.Int[key] = i;
                    break;
                case "bool":
                    capi.Settings.Bool[key] = value == "true";
                    break;
                case "float":
                    if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) return x.Failed(x.T("fix-setting-bad", arg));
                    capi.Settings.Float[key] = f;
                    break;
                default: return x.Failed(x.T("fix-setting-bad", arg));
            }
            return x.T("fix-setting-done", key, value);
        }

        /// <summary>A fix a mod described in JSON (or the owner in the config): reset one config file, or run a command.</summary>
        public static string RunJsonFix(VintageDoctorSystem doctor, JsonFix fix, DoctorFixContext x)
        {
            switch (fix.Type)
            {
                case "resetconfig":
                    x.Argument = fix.File ?? "";
                    return ResetConfig(x);
                case "command":
                    {
                        string command = (fix.Command ?? "").Trim();
                        if (!doctor.Config.AllowCommandFixes) return x.Failed(x.T("fix-command-off"));
                        if (!command.StartsWith("/", StringComparison.Ordinal) || command.Length < 2) return x.Failed(x.T("fix-command-bad", command));
                        var sapi = x.Api as ICoreServerAPI;
                        if (sapi == null) return x.Failed(x.T("fix-command-server", command));
                        sapi.InjectConsole(command);
                        return x.T("fix-command-done", command);
                    }
                default: return x.Failed(x.T("fix-unknown", fix.Type ?? "?"));
            }
        }
    }
}
