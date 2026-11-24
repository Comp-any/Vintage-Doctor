using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using VintageDoctor.API;

namespace VintageDoctor
{
    public static partial class Checks
    {
        public const string TopicMods = "mods", TopicMod = "mod", TopicChanges = "changes", TopicPatches = "patches";

        // ---------------------------------------------------------------- what other mods hand the doctor

        /// <summary>Runs the checks other mods registered (code, event bus) and shows what they reported while playing.</summary>
        public static void ModChecks(DoctorContext x, string onlyMod = null, bool heading = true)
        {
            // A check that names its pages only runs on those; one that doesn't is for the check-up and its mod's page.
            string page = onlyMod != null ? "mod" : "overview";
            var checks = x.Doctor.ChecksFor(onlyMod).Where(c => c.Pages == null || c.Pages.Contains(page)).ToList();
            var incidents = x.Doctor.IncidentsFor(onlyMod);
            var bus = x.Doctor.CollectFromEventBus(x, onlyMod);
            if (checks.Count == 0 && incidents.Count == 0 && bus.Count == 0) return;
            if (heading) x.Section(x.T("sec-modchecks"));
            RunChecks(x, checks);
            ShowBus(x, bus);
            foreach (Incident i in incidents)
            {
                x.Owner = i.Mod;
                x.Result(i.Level, i.Title + (i.Count > 1 ? " (×" + i.Count + ")" : ""));
                if (!string.IsNullOrEmpty(i.Details)) x.Detail(i.Details);
                x.Detail(x.T("incident-when", Time(i.Last)));
                x.Owner = null;
            }
        }

        /// <summary>
        /// On the pages about one thing (an item, the block looked at, why something can't be crafted): what other mods
        /// have to say about that thing. Checks registered for the page (<see cref="DoctorCheck.Pages"/>) and answers to
        /// "vintagedoctor:about" on the event bus. <see cref="DoctorContext.Subject"/> is the thing.
        /// </summary>
        public static void AboutChecks(DoctorContext x)
        {
            string page = x.Topic ?? "";
            var checks = x.Doctor.ChecksFor(null).Where(c => c.Pages != null && c.Pages.Contains(page)).ToList();
            var bus = x.Doctor.CollectFromEventBus(x, null, VintageDoctorSystem.AboutEvent);
            if (checks.Count == 0 && bus.Count == 0) return;
            x.Section(x.T("sec-about-mods"));
            RunChecks(x, checks);
            ShowBus(x, bus);
        }

        static void RunChecks(DoctorContext x, List<DoctorCheck> checks)
        {
            foreach (DoctorCheck check in checks)
            {
                x.Owner = check.ModId;
                try { check.Run?.Invoke(x); }
                catch (Exception e)
                {
                    x.Owner = null;
                    x.Warn(x.T("modcheck-failed", x.ModName(check.ModId), check.Id, e.GetType().Name + ": " + e.Message));
                    x.About(check.ModId);
                    x.Detail(x.T("modcheck-failed-why"));
                    // Under the mod's own tag, so the log's page counts it against that mod and not against the doctor.
                    try { x.Api.Logger.Warning("[{0}] Its check '{1}' for Vintage Doctor failed: {2}", check.ModId, check.Id, e); } catch { }
                }
                x.Owner = null;
            }
        }

        static void ShowBus(DoctorContext x, List<BusResult> bus)
        {
            foreach (BusResult r in bus)
            {
                x.Owner = r.Mod;
                x.Result(r.Level, r.Title);
                foreach (string d in r.Details) x.Detail(d);
                if (!string.IsNullOrEmpty(r.Advice)) x.Advice(r.Advice);
                if (!string.IsNullOrEmpty(r.FixId)) x.FixButton("bus:" + r.FixId, r.FixArg ?? "", string.IsNullOrEmpty(r.FixLabel) ? x.T("btn-fix") : r.FixLabel);
                if (!string.IsNullOrEmpty(r.Link)) x.LinkButton(r.Link, r.LinkArg ?? "", string.IsNullOrEmpty(r.LinkLabel) ? r.Link : r.LinkLabel);
                x.Owner = null;
            }
        }

        // ---------------------------------------------------------------- mods that say they clash

        public static void Conflicts(DoctorContext x, string onlyMod = null)
        {
            ModSnapshot mods = x.Ev.Mods;
            if (mods == null) return;
            foreach (var c in x.Doctor.Known.Conflicts)
            {
                if (!mods.Has(c.mod) || !mods.Has(c.other)) continue;
                if (onlyMod != null && c.mod != onlyMod && c.other != onlyMod) continue;
                x.Result(c.level, x.T("conflict-title", mods.NameOf(c.mod), mods.NameOf(c.other)));
                if (!string.IsNullOrEmpty(c.why)) x.Detail(x.Words(c.why));
                x.Explain(x.T("conflict-why", mods.NameOf(c.mod)));
                x.Buttons(DoctorReport.Open(TopicMod, c.mod, x.T("btn-modpage", mods.NameOf(c.mod))), DoctorReport.Open(TopicMod, c.other, x.T("btn-modpage", mods.NameOf(c.other))));
            }
            if (onlyMod == null) return;
            foreach (var r in x.Doctor.Known.Recommends)
            {
                if (r.mod != onlyMod || mods.Has(r.other)) continue;
                x.Info(x.T("recommend-title", mods.NameOf(r.mod), r.other));
                if (!string.IsNullOrEmpty(r.why)) x.Detail(x.Words(r.why));
                x.UrlButton(ModDbSearch(r.other), x.T("btn-find", r.other));
            }
        }

        // ---------------------------------------------------------------- what changed since last time

        public static void Changes(DoctorContext x, bool full)
        {
            if (full) x.Section(x.T("sec-changes"));
            SessionRecord prev = x.Doctor.PreviousSession;
            if (prev == null)
            {
                if (Listed(x, full)) { x.Info(x.T("changes-first")); x.Explain(x.T("changes-first-why")); }
                return;
            }
            var now = x.Ev.Mods.UserMods.ToDictionary(m => m.Id, m => m.Version, StringComparer.Ordinal);
            var added = now.Keys.Where(k => !prev.Mods.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var removed = prev.Mods.Keys.Where(k => !now.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var updated = now.Where(kv => prev.Mods.TryGetValue(kv.Key, out string v) && v != kv.Value).Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
            bool game = !string.IsNullOrEmpty(prev.GameVersion) && prev.GameVersion != GameVersion.ShortGameVersion;
            if (added.Count + removed.Count + updated.Count == 0 && !game)
            {
                if (Listed(x, full)) x.Ok(x.T("changes-none", prev.Time));
                return;
            }
            x.Info(x.T("changes-title", prev.Time, added.Count, updated.Count, removed.Count) + (game ? " " + x.T("changes-game", prev.GameVersion, GameVersion.ShortGameVersion) : ""));
            foreach (string id in updated.Take(full ? 60 : 8)) x.Detail(x.T("changes-updated", x.ModName(id), prev.Mods[id], now[id]));
            foreach (string id in added.Take(full ? 60 : 8)) x.Detail(x.T("changes-added", x.ModName(id), now[id]));
            foreach (string id in removed.Take(full ? 60 : 8)) x.Detail(x.T("changes-removed", id, prev.Mods[id]));
            x.Explain(x.T("changes-why"));
            // A changed mod that also has errors is the first place to look.
            var suspects = updated.Concat(added).Where(id => x.Ev.Log != null && (x.Ev.Log.For(id).Any(i => i.Bad) || x.Ev.Log.StartFailures.Any(f => f.Mod == id))).Take(4).ToList();
            if (suspects.Count > 0)
            {
                x.Detail(x.T("changes-suspects", string.Join(", ", suspects.Select(x.ModName))));
                x.Buttons(suspects.Select(id => DoctorReport.Open(TopicMod, id, x.T("btn-modpage", x.ModName(id)))).ToArray());
            }
            if (removed.Count > 0) x.Explain(x.T("changes-removed-why"));
        }

        // ---------------------------------------------------------------- every mod at a glance

        sealed class ModHealth
        {
            public ModEntry Mod;
            public int StartErrors, Errors, Warnings, FailedPatches, BadConfigs, Crashes;
            public bool Bad => StartErrors + BadConfigs > 0;
            public bool Any => Bad || Errors + Warnings + FailedPatches + Crashes > 0;
        }

        static List<ModHealth> Health(DoctorContext x)
        {
            var list = new List<ModHealth>();
            LogScan log = x.Ev.Log;
            foreach (ModEntry m in x.Ev.Mods.UserMods)
            {
                var h = new ModHealth { Mod = m };
                if (log != null)
                {
                    h.StartErrors = log.StartFailures.Count(f => f.Mod == m.Id);
                    foreach (LogIssue i in log.For(m.Id)) { if (i.Known != null && i.Known.Level <= DoctorLevel.Info) continue; if (i.Bad) h.Errors++; else h.Warnings++; }
                    log.FailedPatches.TryGetValue(m.Id, out h.FailedPatches);
                }
                if (x.Ev.Configs != null) h.BadConfigs = x.Ev.Configs.Files.Count(f => f.Bad && f.Mod == m.Id);
                if (x.Ev.Crash != null) h.Crashes = x.Ev.Crash.Entries.Count(c => c.Culprits.Contains(m.Id));
                list.Add(h);
            }
            return list;
        }

        public static void ModsPage(DoctorContext x)
        {
            x.Section(x.T("sec-mods"));
            List<ModHealth> all = Health(x);
            var flagged = all.Where(h => h.Any).OrderByDescending(h => h.StartErrors + h.BadConfigs).ThenByDescending(h => h.Errors + h.Crashes).ThenByDescending(h => h.Warnings).ToList();
            x.Line(x.T("mods-help", all.Count, flagged.Count), DoctorLineStyle.Note);
            foreach (ModHealth h in flagged)
            {
                var parts = new List<string>();
                if (h.StartErrors > 0) parts.Add(x.T("mods-starterrors", h.StartErrors));
                if (h.BadConfigs > 0) parts.Add(x.T("mods-badconfigs", h.BadConfigs));
                if (h.Crashes > 0) parts.Add(x.T("mods-crashes", h.Crashes));
                if (h.Errors > 0) parts.Add(x.T("mods-errors", h.Errors));
                if (h.FailedPatches > 0) parts.Add(x.T("mods-patches", h.FailedPatches));
                if (h.Warnings > 0) parts.Add(x.T("mods-warnings", h.Warnings));
                string title = h.Mod.Name + " " + h.Mod.Version + ": " + string.Join(", ", parts);
                if (h.Bad) x.Problem(title);
                else if (h.Errors + h.Crashes + h.FailedPatches > 0) x.Warn(title);
                else x.Info(title);
                ModButtons(x, h.Mod.Id, h.Bad || h.Errors + h.Crashes > 0);
            }
            var fine = all.Where(h => !h.Any).OrderBy(h => h.Mod.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (fine.Count > 0)
            {
                x.Ok(x.T("mods-fine", fine.Count));
                if (x.Compact) x.Detail(string.Join(", ", fine.Select(h => h.Mod.Id + " " + h.Mod.Version)));
                else
                    for (int i = 0; i < fine.Count; i += 5)
                        x.Buttons(fine.Skip(i).Take(5).Select(h => DoctorReport.Open(TopicMod, h.Mod.Id, h.Mod.Name + " " + h.Mod.Version)).ToArray());
            }
            LogScan log = x.Ev.Log;
            if (log != null && log.Skipped.Count > 0)
            {
                x.Warn(x.T("mods-skipped", log.Skipped.Count, string.Join(", ", log.Skipped.Select(s => s.Mod))));
                x.LinkButton(TopicStartup, "", x.T("btn-startup"));
            }
        }

        // ---------------------------------------------------------------- one mod

        public static void ModPage(DoctorContext x, string id)
        {
            ModSnapshot mods = x.Ev.Mods;
            id = (id ?? "").Trim();
            ModEntry m = mods.Get(id) ?? mods.Get(mods.ByLooseName(id));
            LogScan log = x.Ev.Log;
            if (m == null)
            {
                x.Section(x.T("sec-mod", id));
                SkippedMod skipped = log?.Skipped.FirstOrDefault(s => string.Equals(s.Mod, id, StringComparison.OrdinalIgnoreCase));
                ModFile file = x.Ev.Folder?.Find(id);
                if (skipped != null)
                {
                    x.Problem(x.T("mod-skipped", id, string.Join(", ", skipped.Needs.Select(n => Need(x, n)))));
                    x.Explain(x.T("start-skipped-why"));
                    x.Advice(x.T("start-skipped-advice"));
                    x.Buttons(skipped.Needs.Take(5).Select(n => DoctorReport.Url(ModDbSearch(n.dep.Split('@')[0]), x.T("btn-find", n.dep.Split('@')[0]))).ToArray());
                }
                else if (file != null) { x.Warn(x.T("mod-notloaded-file", id, file.Name)); x.Explain(x.T("mod-notloaded-file-why")); }
                else { x.Info(x.T("mod-unknown", id)); x.UrlButton(ModDbSearch(id), x.T("btn-find", id)); }
                x.LinkButton(TopicMods, "", x.T("btn-mods"));
                return;
            }
            x.Section(x.T("sec-mod", m.Name));

            // Who and what it is.
            x.Info(x.T("mod-identity", m.Name, m.Version, m.Authors.Count > 0 ? string.Join(", ", m.Authors) : "?"));
            x.Detail(x.T("mod-identity-detail", m.Id, m.Type, m.Side, m.FileName ?? "?"));
            if (!string.IsNullOrWhiteSpace(m.Description)) x.Explain(LogScan.Shorten(m.Description.Replace('\n', ' '), 260));
            if (!x.Compact)
                x.Buttons(m.Website.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? DoctorReport.Url(m.Website, x.T("btn-website")) : null,
                    DoctorReport.Url(ModDbSearch(m.Name), x.T("btn-find", m.Name)));
            if (m.Core) { x.Line(x.T("mod-core"), DoctorLineStyle.Note); return; }

            // What it needs, and what needs it.
            if (m.Deps.Count > 0)
            {
                var lines = new List<string>();
                bool missing = false;
                foreach (var (dep, version) in m.Deps)
                {
                    if (dep == "game") { lines.Add(x.T("mod-dep-game", version.Length == 0 ? "*" : version, GameVersion.ShortGameVersion)); continue; }
                    ModEntry d = mods.Get(dep);
                    if (d == null) { missing = true; lines.Add(x.T("mod-dep-missing", dep, version)); }
                    else lines.Add(x.T("mod-dep-ok", d.Name, version.Length == 0 ? "*" : version, d.Version));
                }
                if (missing) x.Warn(x.T("mod-deps-missing", m.Name)); else x.Ok(x.T("mod-deps-ok", m.Deps.Count));
                foreach (string l in lines) x.Detail(l);
            }
            var needers = mods.UserMods.Where(o => o.Deps.Any(d => d.id == m.Id)).Select(o => o.Name).ToList();
            if (needers.Count > 0) x.Line(x.T("mod-neededby", string.Join(", ", needers)), DoctorLineStyle.Note);

            // Start-up.
            if (log != null)
            {
                var failures = log.StartFailures.Where(f => f.Mod == m.Id).ToList();
                if (failures.Count > 0)
                {
                    x.Problem(x.T("start-failed", m.Name, LogScan.Shorten(failures[0].Error ?? failures[0].What, 160)));
                    foreach (StartFailure f in failures.Take(4))
                    {
                        x.Detail(f.What);
                        foreach (string frame in f.Stack.Take(8)) x.Detail(LogScan.Shorten(Anonymise.Line(frame), 220), 2);
                        if (f.Systems.Count > 0) x.Detail(x.T("start-systems", string.Join(", ", f.Systems)), 2);
                    }
                    x.Explain(Why(x, failures[0].Error, failures[0].Stack) ?? x.T("start-failed-why"));
                    x.Advice(x.T("start-failed-advice", m.Name));
                }
                if (log.FailedPatches.TryGetValue(m.Id, out int failed))
                {
                    x.Warn(x.T("mod-patches-failed", failed));
                    if (log.FailedPatchExample.TryGetValue(m.Id, out string ex)) x.Detail(ex);
                    x.Explain(x.T("start-patches-why"));
                }
                if (log.Unresolved.TryGetValue(m.Id, out int unresolved))
                {
                    x.Info(x.T("mod-unresolved", unresolved));
                    if (log.UnresolvedExample.TryGetValue(m.Id, out string ex)) x.Detail(ex);
                    x.Explain(x.T("start-unresolved-why"));
                }
            }

            // Crashes that name it.
            if (x.Ev.Crash != null)
            {
                var crashes = x.Ev.Crash.Entries.Where(c => c.Culprits.Contains(m.Id)).ToList();
                if (crashes.Count > 0)
                {
                    CrashEntry last = crashes[crashes.Count - 1];
                    string then = last.VersionAtCrash(m.Id);
                    x.Warn(x.T("mod-crashes", crashes.Count, last.When ?? "?", ShortType(last.ExceptionType)));
                    if (then != null && then != m.Version) x.Detail(x.T("crash-mod-updated", m.Name, then, m.Version));
                    else x.Detail(x.T("crash-mod-same", m.Name, m.Version));
                    x.LinkButton(TopicCrash, "", x.T("btn-crash"));
                }
            }

            // What it adds to the game, and what of the game's and other mods' it replaces.
            Content(x, m);
            Guarded(x, "mod files", () => ModFiles(x, m));
            // Its config files.
            if (x.Ev.Configs != null)
            {
                var files = x.Ev.Configs.Files.Where(f => f.Mod == m.Id).ToList();
                foreach (ConfigFile f in files.Where(f => f.Bad))
                {
                    x.Problem(f.Empty ? x.T("configs-empty", f.Name) : x.T("configs-broken", f.Name));
                    if (f.Error != null) x.Detail(Anonymise.Line(f.Error));
                    x.Explain(f.Empty ? x.T("configs-empty-why") : x.T("configs-broken-why"));
                    x.FixButton("doctor:reset-config", f.Name, x.T("btn-reset-config", f.Name));
                }
                var good = files.Where(f => !f.Bad).ToList();
                if (good.Count > 0)
                {
                    x.Ok(x.T("mod-configs", string.Join(", ", good.Select(f => f.Name))));
                    x.Explain(x.T("mod-configs-why"));
                    foreach (ConfigFile f in good.Take(3)) x.FixButton("doctor:reset-config", f.Name, x.T("btn-reset-config-default", f.Name));
                }
            }
            // Its code patches.
            HarmonyFor(x, m);
            Conflicts(x, m.Id);
            // Measured time, if a run was made.
            TickProfile p = x.Doctor.Profile;
            if (p.HasResult)
            {
                double ms = p.Leaves().Where(l => { string t = TickProfile.TypeIn(l.code); return t != null && mods.ModOfType(t) == m.Id; }).Sum(l => l.ms);
                if (ms > 0.005) x.Info(x.T("mod-profile", N(ms, "0.00"), N(p.PerTick().work, "0.00")));
            }
            // Its own checks.
            ModChecks(x, m.Id, heading: true);
            // Its lines in the log.
            if (log != null && log.For(m.Id).Any()) Log(x, true, m.Id);
            else if (log != null && log.Found) { x.Section(x.T("sec-log-mod", m.Name)); x.Ok(x.T("log-clean-mod", m.Name)); }

            if (!x.Compact)
            {
                x.Section(x.T("sec-next"));
                x.Line(string.Join(" ", new[]
                {
                    m.Id != DoctorApi.ModId ? x.PlayerFixToken("doctor:disable-mod", m.Id, x.T("btn-disable", m.Name)) : null,
                    DoctorReport.Open("bugreport", m.Id, x.T("btn-bugreport-mod", m.Name)),
                    DoctorReport.Open(TopicMods, "", x.T("btn-mods")),
                    DoctorReport.Open("overview", "", x.T("btn-home"))
                }.Where(t => t != null)));
            }
        }

        /// <summary>How many blocks, items and creatures carry the mod's id: for a content mod, none means its files didn't load.</summary>
        static void Content(DoctorContext x, ModEntry m)
        {
            try
            {
                IWorldAccessor world = x.Api.World;
                int blocks = world.Blocks.Count(b => b?.Code != null && b.Code.Domain == m.Id && !b.IsMissing);
                int items = world.Items.Count(i => i?.Code != null && i.Code.Domain == m.Id && !i.IsMissing);
                // Only on the server: the client's list of entity types must not be read before it is complete.
                int entities = x.Side == EnumAppSide.Server ? world.EntityTypes.Count(e => e?.Code != null && e.Code.Domain == m.Id) : 0;
                if (blocks + items + entities > 0) x.Ok(x.T("mod-content", blocks, items, entities));
                else if (m.Type == "Content" && m.Assemblies.Count == 0) { x.Info(x.T("mod-content-none")); x.Explain(x.T("mod-content-none-why")); }
            }
            catch (Exception e) { x.Line("content: " + e.Message, DoctorLineStyle.Note); }
        }

        // ---------------------------------------------------------------- code patches (Harmony) and JSON patches

        internal sealed class PatchedMethod
        {
            public string Name;
            public List<string> Owners = new List<string>(), Transpilers = new List<string>(), Prefixes = new List<string>();
            /// <summary>
            /// What each mod's patches do at this place, a letter each: 'b' runs before the game's code, 'a' after it,
            /// 'r' rewrites it, 'e' steps in when it ends or fails.
            /// </summary>
            public Dictionary<string, string> How = new Dictionary<string, string>(StringComparer.Ordinal);

            public void Add(string owner, char how)
            {
                if (!Owners.Contains(owner)) Owners.Add(owner);
                if (how == 'b' && !Prefixes.Contains(owner)) Prefixes.Add(owner);
                if (how == 'r' && !Transpilers.Contains(owner)) Transpilers.Add(owner);
                How[owner] = How.TryGetValue(owner, out string had) ? (had.IndexOf(how) >= 0 ? had : had + how) : how.ToString();
            }
        }

        /// <summary>Every game method a mod has patched, and whose patches they are (a patch's owner is traced to its mod by its code).</summary>
        static List<PatchedMethod> PatchedMethods(DoctorContext x)
        {
            var list = new List<PatchedMethod>();
            ModSnapshot mods = x.Ev.Mods;
            string Owner(Patch p)
            {
                string mod = null;
                try { mod = mods.ModOfAssembly(p.PatchMethod?.DeclaringType?.Assembly); } catch { }
                return mod ?? mods.ByLooseName(p.owner) ?? p.owner ?? "?";
            }
            foreach (MethodBase method in Harmony.GetAllPatchedMethods().ToList())
            {
                if (method?.DeclaringType == null) continue;
                Patches info = Harmony.GetPatchInfo(method);
                if (info == null) continue;
                var pm = new PatchedMethod { Name = method.DeclaringType.Name + "." + method.Name };
                foreach (Patch p in info.Prefixes) pm.Add(Owner(p), 'b');
                foreach (Patch p in info.Postfixes) pm.Add(Owner(p), 'a');
                foreach (Patch p in info.Transpilers) pm.Add(Owner(p), 'r');
                foreach (Patch p in info.Finalizers) pm.Add(Owner(p), 'e');
                if (pm.Owners.Count > 0) list.Add(pm);
            }
            return list;
        }

        /// <summary>"runs before it, runs after it": what a mod's patches do at a place.</summary>
        static string HowWords(DoctorContext x, PatchedMethod m, string owner)
        {
            m.How.TryGetValue(owner, out string how);
            var parts = new List<string>();
            foreach (char c in how ?? "") parts.Add(c == 'b' ? x.T("patch-before") : c == 'a' ? x.T("patch-after") : c == 'r' ? x.T("patch-rewrites") : x.T("patch-ends"));
            return string.Join(", ", parts);
        }

        /// <summary>"Carry On (runs before it)".</summary>
        static string WhoHow(DoctorContext x, PatchedMethod m, string owner)
        {
            string how = HowWords(x, m, owner);
            return x.ModName(owner) + (how.Length > 0 ? " (" + how + ")" : "");
        }

        /// <summary>"A", "A and B", "A, B and C".</summary>
        static string NameList(DoctorContext x, IList<string> names)
        {
            if (names.Count == 0) return "?";
            if (names.Count == 1) return names[0];
            return x.T("list-and", string.Join(", ", names.Take(names.Count - 1)), names[names.Count - 1]);
        }

        public static void CodePatches(DoctorContext x, bool full)
        {
            if (full) x.Section(x.T("sec-patches"));
            List<PatchedMethod> methods;
            try { methods = PatchedMethods(x); }
            catch (Exception e) { x.Info(x.T("patches-error", e.Message)); return; }
            CodePatches(x, full, methods);
        }

        /// <summary>
        /// The page from what was read: who changes the game's code and how much, and then, for the places more than
        /// one mod changes, which place it is, which mods, and what each does there.
        /// </summary>
        internal static void CodePatches(DoctorContext x, bool full, List<PatchedMethod> methods)
        {
            if (methods.Count == 0) { x.Ok(x.T("patches-none")); return; }
            var owners = methods.SelectMany(m => m.Owners).GroupBy(o => o).OrderByDescending(g => g.Count()).ToList();
            x.Info(x.T("patches-title", owners.Count, methods.Count));
            x.Detail(string.Join(", ", owners.Take(full ? 60 : 8).Select(g => x.ModName(g.Key) + " (" + g.Count() + ")")) + (owners.Count > (full ? 60 : 8) ? ", ..." : ""));
            x.Explain(x.T("patches-why"));
            if (!full) x.LinkButton(TopicPatches, "", x.T("btn-patches"));

            var shared = methods.Where(m => m.Owners.Count >= 2).ToList();
            if (shared.Count == 0) { if (Listed(x, full)) x.Ok(x.T("patches-shared-none")); return; }
            // One card for each set of mods that meet somewhere: the mods in its title, the places under it.
            var groups = shared.GroupBy(m => string.Join("|", m.Owners.OrderBy(o => o, StringComparer.Ordinal)))
                .OrderByDescending(g => g.Any(m => m.Transpilers.Count >= 2)).ThenByDescending(g => g.Count()).ToList();
            int most = full ? 40 : 12, places = full ? 40 : 8;
            foreach (var g in groups.Take(most))
            {
                List<string> who = g.First().Owners.OrderBy(o => x.ModName(o), StringComparer.OrdinalIgnoreCase).ToList();
                bool rewritten = g.Any(m => m.Transpilers.Count >= 2);
                string title = x.T("patches-shared", NameList(x, who.Select(x.ModName).ToList()), g.Count());
                if (rewritten) x.Warn(title); else x.Info(title);
                x.About(who.Where(o => x.Ev.Mods?.Get(o) != null).ToArray());
                foreach (PatchedMethod m in g.OrderByDescending(m => m.Transpilers.Count).ThenBy(m => m.Name, StringComparer.Ordinal).Take(places))
                    x.Detail(m.Name + ": " + string.Join(", ", who.Select(o => WhoHow(x, m, o))));
                if (g.Count() > places) x.Detail(x.T("more-lines", g.Count() - places));
                x.Explain(rewritten ? x.T("patches-rewritten-why") : x.T("patches-shared-why"));
            }
            if (groups.Count > most)
            {
                x.Info(x.T("patches-shared-more", groups.Count - most));
                if (!full) x.LinkButton(TopicPatches, "", x.T("btn-patches"));
            }
        }

        static void HarmonyFor(DoctorContext x, ModEntry m)
        {
            try
            {
                var mine = PatchedMethods(x).Where(pm => pm.Owners.Contains(m.Id)).ToList();
                if (mine.Count == 0) return;
                var shared = mine.Where(pm => pm.Owners.Count >= 2).OrderBy(pm => pm.Name, StringComparer.Ordinal).ToList();
                var alone = mine.Where(pm => pm.Owners.Count < 2).OrderBy(pm => pm.Name, StringComparer.Ordinal).ToList();
                x.Info(x.T("mod-harmony", mine.Count, shared.Count));
                // The shared ones first and all of them: which place, what this mod does there, and who else is at it.
                foreach (PatchedMethod pm in shared.Take(30))
                    x.Detail(pm.Name + ": " + HowWords(x, pm, m.Id) + "; " + x.T("mod-harmony-also", string.Join(", ", pm.Owners.Where(o => o != m.Id).Select(o => WhoHow(x, pm, o)))));
                foreach (PatchedMethod pm in alone.Take(12)) x.Detail(pm.Name + ": " + HowWords(x, pm, m.Id));
                int more = Math.Max(0, shared.Count - 30) + Math.Max(0, alone.Count - 12);
                if (more > 0) x.Detail(x.T("more-lines", more));
                x.Explain(shared.Count > 0 ? x.T("patches-shared-why") : x.T("patches-why"));
            }
            catch { }
        }

        /// <summary>JSON patches from different mods that change the same place in the same file: the last one wins.</summary>
        // What the mods' patch files change, read once: they don't change while the game runs, and the check-up
        // looks every time it is opened.
        static Dictionary<string, HashSet<string>> patchTargets;
        static int patchTotal, patchFiles;
        static readonly object patchGate = new object();

        public static void JsonPatchOverlaps(DoctorContext x)
        {
            if (!x.ListAll) x.Section(x.T("sec-jsonpatches"));
            Dictionary<string, HashSet<string>> targets;
            int total = 0, files = 0;
            lock (patchGate) { targets = patchTargets; total = patchTotal; files = patchFiles; }
            if (targets == null) try
            {
                targets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                foreach (IAsset asset in x.Api.Assets.GetMany("patches/", null, true))
                {
                    string domain = asset.Location.Domain;
                    if (ModSnapshot.IsGameMod(domain)) continue;
                    JToken tok;
                    try { tok = JToken.Parse(asset.ToText()); } catch { continue; }
                    files++;
                    foreach (JToken p in tok as JArray ?? new JArray(tok))
                    {
                        if (!(p is JObject po)) continue;
                        string op = ((string)po["op"] ?? "").ToLowerInvariant();
                        if (op != "replace" && op != "remove" && op != "move") continue;
                        total++;
                        string key = (string)po["file"] + " " + (string)po["path"];
                        if (!targets.TryGetValue(key, out HashSet<string> set)) targets[key] = set = new HashSet<string>(StringComparer.Ordinal);
                        set.Add(domain);
                    }
                }
                lock (patchGate) { patchTargets = targets; patchTotal = total; patchFiles = files; }
            }
            catch (Exception e) { x.Info(x.T("patches-error", e.Message)); return; }
            var clashes = targets.Where(kv => kv.Value.Count >= 2).OrderByDescending(kv => kv.Value.Count).ToList();
            if (clashes.Count == 0) { x.Ok(x.T("jsonpatches-none", total, files)); return; }
            x.Warn(x.T("jsonpatches-clash", clashes.Count));
            foreach (var kv in clashes.Take(40)) x.Detail(kv.Key + ": " + string.Join(", ", kv.Value.Select(x.ModName)));
            x.Explain(x.T("jsonpatches-why"));
        }
    }
}
