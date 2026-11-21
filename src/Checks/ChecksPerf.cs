using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace VintageDoctor
{
    public static partial class Checks
    {
        public const string TopicPerf = "perf";

        /// <summary>Is the server keeping up? Tick times, what's loaded, memory, and the breakdown of a measured run.</summary>
        public static void ServerPerformance(DoctorContext x, bool full)
        {
            var sapi = x.Api as ICoreServerAPI;
            if (sapi == null) return;
            if (full) x.Section(x.T("sec-perf-server"));
            TickWatch watch = x.Doctor.Ticks;
            var (avg, p95, max, samples) = watch.Recent();
            float target = 33.3f;
            try { target = Math.Max(1, sapi.Server.Config.TickTime); } catch { }
            var (slowCount, slowWorst) = watch.SlowIn(10);
            var overloads = x.Ev.Log?.Overloads ?? new List<(string time, long ms)>();
            int recentOverloads = 0;
            long worstOverload = 0;
            DateTime cutoff = DateTime.Now.AddMinutes(-10);
            foreach (var o in overloads)
            {
                if (o.ms > worstOverload) worstOverload = o.ms;
                if (LogScan.TryTime(o.time, out DateTime t) && t >= cutoff) recentOverloads++;
            }

            if (samples < 30) x.Info(x.T("perf-tick-early"));
            else if (slowCount >= 5 || recentOverloads >= 3 || avg > target * 1.6)
            {
                x.Warn(x.T("perf-tick-slow", N(avg), N(1000.0 / Math.Max(1, avg), "0"), slowCount, N(Math.Max(slowWorst, 0), "0")));
                x.Explain(x.T("perf-tick-slow-why", N(target, "0")));
                x.Advice(x.T("perf-tick-slow-advice"));
            }
            else x.Ok(x.T("perf-tick-ok", N(avg), N(1000.0 / Math.Max(1, avg), "0")));
            x.Detail(x.T("perf-tick-detail", N(avg), N(p95), N(max, "0"), samples));
            if (overloads.Count > 0) x.Detail(x.T("perf-overloads", overloads.Count, worstOverload, recentOverloads));
            ProfileButtons(x);

            Entities(x, sapi, full);
            if (!x.SameProcess) Memory(x, full);
            if (full || x.Doctor.Profile.HasResult || x.Doctor.Profile.Running) ProfileResult(x, full);
        }

        static void ProfileButtons(DoctorContext x)
        {
            if (x.Compact) return;
            TickProfile p = x.Doctor.Profile;
            if (p.Running) x.Detail(x.T("perf-profile-running", p.SecondsLeft));
            else x.Buttons(x.FixToken("doctor:profile", "20", x.T("btn-profile", 20)), p.HasResult ? DoctorReport.Open(TopicPerf, "", x.T("btn-perf")) : null);
        }

        static void Entities(DoctorContext x, ICoreServerAPI sapi, bool full)
        {
            int total = 0, items = 0, players = 0;
            var byType = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                foreach (Entity e in sapi.World.LoadedEntities.Values)
                {
                    if (e == null) continue;
                    total++;
                    if (e is EntityItem) { items++; continue; }
                    if (e is EntityPlayer) { players++; continue; }
                    string code = e.Code == null ? "?" : e.Code.Domain + ":" + e.Code.Path.Split('-')[0];
                    byType[code] = byType.TryGetValue(code, out int n) ? n + 1 : 1;
                }
            }
            catch (Exception ex) { x.Info(x.T("perf-entities-error", ex.Message)); return; }
            int chunks = 0;
            try { chunks = sapi.WorldManager.AllLoadedChunks.Count; } catch { }
            string top = string.Join(", ", byType.OrderByDescending(kv => kv.Value).Take(full ? 14 : 5).Select(kv => kv.Key + " " + kv.Value));
            bool manyItems = items >= 400, many = total - items >= 3000;
            if (manyItems || many)
            {
                x.Warn(many ? x.T("perf-entities-many", total, items) : x.T("perf-items-many", items));
                x.Explain(x.T("perf-entities-why"));
                if (manyItems) x.FixButton("doctor:remove-items", "", x.T("btn-remove-items", items));
            }
            else x.Ok(x.T("perf-entities-ok", total, items, chunks, players));
            if (top.Length > 0) x.Detail(x.T("perf-entities-top", top));
            x.Detail(x.T("perf-chunks", chunks, players));
        }

        /// <summary>The program's memory: how much it holds, and how much the computer has.</summary>
        public static void Memory(DoctorContext x, bool full)
        {
            try
            {
                long working = Process.GetCurrentProcess().WorkingSet64;
                long managed = GC.GetTotalMemory(false);
                long available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                double share = available > 0 ? working * 100.0 / available : 0;
                if (share >= 85)
                {
                    x.Warn(x.T("perf-memory-high", Size(working), Size(available), N(share, "0")));
                    x.Explain(x.T("perf-memory-why"));
                    x.Advice(x.Side == EnumAppSide.Client ? x.T("perf-memory-advice-client") : x.T("perf-memory-advice-server"));
                }
                else x.Ok(x.T("perf-memory-ok", Size(working), Size(available)));
                x.Detail(x.T("perf-memory-detail", Size(managed), GC.CollectionCount(2), Environment.ProcessorCount));
            }
            catch (Exception e) { x.Info(x.T("perf-memory-error", e.Message)); }
        }

        /// <summary>The measured run: where a tick's time went, and which mods the tick handlers belong to.</summary>
        public static void ProfileResult(DoctorContext x, bool full)
        {
            TickProfile p = x.Doctor.Profile;
            if (full) x.Section(x.Side == EnumAppSide.Client ? x.T("sec-profile-client") : x.T("sec-profile"));
            if (p.Running) { x.Info(x.T("perf-profile-running", p.SecondsLeft)); return; }
            if (!p.HasResult)
            {
                x.Info(x.T("perf-profile-none"));
                x.Explain(x.T("perf-profile-what"));
                if (p.Error != null) x.Detail(p.Error);
                x.FixButton("doctor:profile", "20", x.T("btn-profile", 20));
                return;
            }
            var (all, work) = p.PerTick();
            var leaves = p.Leaves();
            x.Info(x.T("perf-profile-title", p.Seconds, p.Ticks, N(work, "0.00"), Time(p.FinishedAt)));
            x.Explain(x.Side == EnumAppSide.Client ? x.T("perf-profile-explain-client") : x.T("perf-profile-explain"));
            int shown = 0;
            foreach (var leaf in leaves)
            {
                if (leaf.ms < 0.02 || shown >= (full ? 22 : 8)) break;
                shown++;
                x.Detail(N(leaf.ms, "0.00") + " ms  " + N(work > 0 ? leaf.ms * 100 / work : 0, "0") + "%  " + ProfileName(x, leaf.code) + (leaf.calls >= 1.5 ? "  (" + N(leaf.calls, "0") + "×)" : ""));
            }
            // The same again, added up by the mod each class belongs to.
            var byMod = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var leaf in leaves)
            {
                string type = TickProfile.TypeIn(leaf.code);
                string mod = type == null ? null : x.Ev.Mods?.ModOfType(type);
                if (mod == null) continue;
                byMod[mod] = byMod.TryGetValue(mod, out double v) ? v + leaf.ms : leaf.ms;
            }
            if (byMod.Count > 0)
            {
                var top = byMod.OrderByDescending(kv => kv.Value).Take(full ? 15 : 5).ToList();
                bool heavy = top[0].Value >= Math.Max(4, work * 0.3);
                string title = x.T("perf-profile-mods", string.Join(", ", top.Take(4).Select(kv => x.ModName(kv.Key) + " " + N(kv.Value, "0.00") + " ms")));
                if (heavy) x.Warn(title); else x.Info(title);
                foreach (var kv in top) x.Detail(N(kv.Value, "0.00") + " ms  " + N(work > 0 ? kv.Value * 100 / work : 0, "0") + "%  " + x.ModName(kv.Key) + " " + DoctorReport.Open("mod", kv.Key, x.T("btn-modpage-short")));
                x.Explain(x.T("perf-profile-mods-why"));
            }
            x.FixButton("doctor:profile", "20", x.T("btn-profile-again", 20));
        }

        /// <summary>A profiler entry's name in words where the doctor knows it, with the mod a named class belongs to.</summary>
        static string ProfileName(DoctorContext x, string code)
        {
            string type = TickProfile.TypeIn(code);
            if (type != null)
            {
                string mod = x.Ev.Mods?.ModOfType(type);
                // "MySystem+<>c__DisplayClass12_0" is a lambda inside MySystem.
                string plain = type;
                int made = plain.IndexOf("+<", StringComparison.Ordinal);
                if (made > 0) plain = plain.Substring(0, made);
                string shortType = plain.Substring(plain.LastIndexOf('.') + 1);
                string kind = code.StartsWith("gmle", StringComparison.Ordinal) || code.StartsWith("gmlb", StringComparison.Ordinal) ? x.T("prof-ticking")
                    : code.StartsWith("rendGui", StringComparison.Ordinal) ? x.T("prof-gui")
                    : code.StartsWith("dc", StringComparison.Ordinal) || code.StartsWith("sdc", StringComparison.Ordinal) ? x.T("prof-callback") : "";
                return (kind.Length > 0 ? kind + " " : "") + shortType + (mod != null ? " [" + x.ModName(mod) + "]" : "");
            }
            switch (code)
            {
                case "entityAgent-ticking": return x.T("prof-creatures");
                case "entityPlayer-ticking": return x.T("prof-players");
                case "entity-tick-droppeditems": return x.T("prof-items");
                case "tickentities": return x.T("prof-entities");
                case "spawningentities": return x.T("prof-spawning");
                case "physicsmanager-servertick": return x.T("prof-physics");
                case "ai-pathfinding": return x.T("prof-pathfinding");
                case "mtasks": return x.T("prof-mtasks");
                case "timers-updated": return x.T("prof-timers");
                default: return code;
            }
        }
    }
}
