using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;

namespace VintageDoctor
{
    /// <summary>One loaded mod, as plain data (safe to read from a worker thread).</summary>
    public sealed class ModEntry
    {
        public string Id, Name, Version, Type, Side, SourcePath, FileName, SourceType, Website, Description, NetworkVersion;
        public List<string> Authors = new List<string>();
        public List<(string id, string version)> Deps = new List<(string, string)>();
        public bool RequiredOnClient, RequiredOnServer, Core;
        public List<Assembly> Assemblies = new List<Assembly>();
        public string Key => Id + "@" + Version;
    }

    /// <summary>
    /// The mods the game loaded on this side, and which mod each piece of code belongs to: a stack trace line or a
    /// Harmony patch is traced to its mod through the types in the mod's assemblies.
    /// </summary>
    public sealed class ModSnapshot
    {
        public readonly List<ModEntry> Mods = new List<ModEntry>();
        readonly Dictionary<string, ModEntry> byId = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> typeToMod = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<Assembly, string> assemblyToMod = new Dictionary<Assembly, string>();
        readonly Dictionary<string, string> assemblyNameToMod = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> looseName = new Dictionary<string, string>(StringComparer.Ordinal);
        bool typesIndexed;
        readonly object gate = new object();

        /// <summary>Ids of the mods that are the game itself.</summary>
        public static bool IsGameMod(string id) => id == "game" || id == "survival" || id == "creative" || id == "essentials";

        /// <summary>Reads the loaded mods. Call on the main thread.</summary>
        public static ModSnapshot Take(ICoreAPI api)
        {
            var snap = new ModSnapshot();
            foreach (Mod mod in api.ModLoader.Mods)
            {
                ModInfo info = mod?.Info;
                if (info?.ModID == null) continue;
                var e = new ModEntry
                {
                    Id = info.ModID,
                    Name = string.IsNullOrEmpty(info.Name) ? info.ModID : info.Name,
                    Version = info.Version ?? "",
                    Type = info.Type.ToString(),
                    Side = info.Side.ToString(),
                    SourcePath = mod.SourcePath,
                    FileName = mod.FileName,
                    SourceType = mod.SourceType.ToString(),
                    Website = info.Website ?? "",
                    Description = info.Description ?? "",
                    NetworkVersion = info.NetworkVersion,
                    RequiredOnClient = info.RequiredOnClient,
                    RequiredOnServer = info.RequiredOnServer,
                    Core = info.CoreMod || IsGameMod(info.ModID)
                };
                try { if (info.Authors != null) e.Authors.AddRange(info.Authors); } catch { }
                try
                {
                    if (info.Dependencies != null)
                        foreach (ModDependency d in info.Dependencies) if (d?.ModID != null) e.Deps.Add((d.ModID, d.Version ?? ""));
                }
                catch { }
                try
                {
                    if (mod.Systems != null)
                        foreach (ModSystem s in mod.Systems)
                        {
                            Assembly a = s?.GetType().Assembly;
                            if (a != null && !e.Assemblies.Contains(a)) e.Assemblies.Add(a);
                        }
                }
                catch { }
                snap.Mods.Add(e);
                snap.byId[e.Id] = e;
                string loose = Loose(e.Name);
                if (loose.Length >= 4 && !snap.looseName.ContainsKey(loose)) snap.looseName[loose] = e.Id;
                loose = Loose(e.Id);
                if (loose.Length >= 3) snap.looseName[loose] = e.Id;
                if (e.Core) continue;
                foreach (Assembly a in e.Assemblies)
                {
                    snap.assemblyToMod[a] = e.Id;
                    string name = a.GetName().Name;
                    if (!string.IsNullOrEmpty(name)) snap.assemblyNameToMod[name] = e.Id;
                }
            }
            snap.Mods.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.Ordinal));
            return snap;
        }

        public ModEntry Get(string id) => id != null && byId.TryGetValue(id, out ModEntry e) ? e : null;
        public bool Has(string id) => id != null && byId.ContainsKey(id);
        public string NameOf(string id) => Get(id)?.Name ?? id ?? "?";
        public IEnumerable<ModEntry> UserMods => Mods.Where(m => !m.Core);

        /// <summary>Letters and digits only, lower case: "Carry On" and "carryon" compare equal.</summary>
        public static string Loose(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var chars = new char[s.Length];
            int n = 0;
            foreach (char ch in s) if (char.IsLetterOrDigit(ch)) chars[n++] = char.ToLowerInvariant(ch);
            return new string(chars, 0, n);
        }

        /// <summary>The mod whose name or id this is, written any way ("CarryOn", "Carry On", "carryon"); null if none.</summary>
        public string ByLooseName(string name)
        {
            string loose = Loose(name);
            return loose.Length >= 3 && looseName.TryGetValue(loose, out string id) ? id : null;
        }

        public string ModOfAssembly(Assembly a) => a != null && assemblyToMod.TryGetValue(a, out string id) ? id : null;

        /// <summary>Reads every mod assembly's type names once (slow the first time: do it off the main thread).</summary>
        public void IndexTypes()
        {
            lock (gate)
            {
                if (typesIndexed) return;
                foreach (ModEntry e in Mods)
                {
                    if (e.Core) continue;
                    foreach (Assembly a in e.Assemblies)
                    {
                        Type[] types;
                        try { types = a.GetTypes(); }
                        catch (ReflectionTypeLoadException ex) { types = ex.Types?.Where(t => t != null).ToArray() ?? Array.Empty<Type>(); }
                        catch { continue; }
                        foreach (Type t in types)
                        {
                            string full = t?.FullName;
                            if (string.IsNullOrEmpty(full)) continue;
                            // A stack trace writes a nested type with a dot.
                            typeToMod[full.Replace('+', '.')] = e.Id;
                        }
                    }
                }
                typesIndexed = true;
            }
        }

        /// <summary>
        /// The mod a stack trace line's code belongs to, or null for the game's own code:
        /// "   at CarryOn.Server.Logic.Drop(Args) in ..." → "carryon".
        /// </summary>
        public string ModOfFrame(string frame)
        {
            if (string.IsNullOrEmpty(frame)) return null;
            string s = frame.TrimStart();
            if (s.StartsWith("at ", StringComparison.Ordinal)) s = s.Substring(3);
            int paren = s.IndexOf('(');
            if (paren > 0) s = s.Substring(0, paren);
            s = s.Trim();
            if (s.Length == 0) return null;
            IndexTypes();
            // Method, then nested and generated types, come off the right until a known type is left.
            string name = s;
            for (int guard = 0; guard < 12; guard++)
            {
                int dot = name.LastIndexOf('.');
                if (dot <= 0) break;
                name = name.Substring(0, dot);
                string clean = name;
                int tick = clean.IndexOf('`');
                if (typeToMod.TryGetValue(clean, out string id)) return id;
                if (tick > 0 && typeToMod.TryGetValue(clean.Substring(0, tick), out id)) return id;
            }
            // The assembly's own name as the first part of the namespace ("CarryOn.").
            int first = s.IndexOf('.');
            if (first > 0 && assemblyNameToMod.TryGetValue(s.Substring(0, first), out string byAssembly)) return byAssembly;
            return null;
        }

        /// <summary>The mod a class belongs to, by its full name ("MyMod.Systems.Pipes"); null for the game's own classes.</summary>
        public string ModOfType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            IndexTypes();
            string name = typeName.Replace('+', '.').Trim();
            if (typeToMod.TryGetValue(name, out string id)) return id;
            int generic = name.IndexOf('[');
            if (generic > 0 && typeToMod.TryGetValue(name.Substring(0, generic), out id)) return id;
            int first = name.IndexOf('.');
            return first > 0 && assemblyNameToMod.TryGetValue(name.Substring(0, first), out string byAssembly) ? byAssembly : null;
        }

        /// <summary>The first mod named by a stack trace, top down; null when only the game's code is in it.</summary>
        public string ModOfStack(IEnumerable<string> stack)
        {
            if (stack == null) return null;
            foreach (string frame in stack)
            {
                string id = ModOfFrame(frame);
                if (id != null) return id;
            }
            return null;
        }
    }
}
