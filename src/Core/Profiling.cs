using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Vintagestory.API.Common;

namespace VintageDoctor
{
    /// <summary>How long the game takes between ticks (server) or frames (client), measured all the time at almost no cost.</summary>
    public sealed class TickWatch
    {
        readonly Stopwatch watch = Stopwatch.StartNew();
        long last = -1;
        readonly float[] recent = new float[1800];
        int index, count;
        readonly Queue<(long at, float ms)> slow = new Queue<(long, float)>();
        public const float SlowMs = 250;
        public long Total;

        public void Tick()
        {
            long now = watch.ElapsedMilliseconds;
            if (last >= 0)
            {
                float ms = now - last;
                // A paused game (single player with a window open) is not a slow game.
                if (ms < 15000)
                {
                    recent[index] = ms;
                    index = (index + 1) % recent.Length;
                    if (count < recent.Length) count++;
                    Total++;
                    if (ms >= SlowMs)
                    {
                        slow.Enqueue((now, ms));
                        while (slow.Count > 400) slow.Dequeue();
                    }
                }
            }
            last = now;
        }

        /// <summary>Average, 95th percentile and worst of the last samples (about a minute's worth), in milliseconds.</summary>
        public (double avg, double p95, double max, int samples) Recent()
        {
            if (count == 0) return (0, 0, 0, 0);
            var copy = new float[count];
            Array.Copy(recent, copy, count);
            Array.Sort(copy);
            double sum = 0;
            foreach (float f in copy) sum += f;
            return (sum / count, copy[Math.Min(count - 1, (int)(count * 0.95))], copy[count - 1], count);
        }

        /// <summary>Slow ticks (a quarter of a second or more) in the last so-many minutes, and the worst of them.</summary>
        public (int count, double worst) SlowIn(int minutes)
        {
            long since = watch.ElapsedMilliseconds - minutes * 60000L;
            int n = 0; double worst = 0;
            foreach (var s in slow) if (s.at >= since) { n++; if (s.ms > worst) worst = s.ms; }
            return (n, worst);
        }

        public double MinutesRunning => watch.ElapsedMilliseconds / 60000.0;
    }

    /// <summary>
    /// Where the game's time goes, measured with the game's own tick profiler (the one behind /debug logticks): the
    /// doctor switches it on for a few seconds, adds up every tick's breakdown, and switches it back. The profiler
    /// names each tick handler by the class that registered it, which is how time is traced to mods.
    /// </summary>
    public sealed class TickProfile
    {
        public bool Running;
        public DateTime StartedAt, FinishedAt;
        public int Seconds, Ticks;
        public string Error;
        /// <summary>Path in the profiler's tree ("all/tickentities/entityAgent-ticking") → stopwatch ticks and calls, summed over the run.</summary>
        public readonly Dictionary<string, (long ticks, long calls)> ByPath = new Dictionary<string, (long, long)>(StringComparer.Ordinal);
        bool wasEnabled;
        ProfileEntryRange lastRoot;
        long endAt;
        FrameProfilerUtil profiler;
        /// <summary>Called once when the run is over (on the main thread).</summary>
        public Action<TickProfile> OnFinished;

        public bool HasResult => !Running && Ticks > 0;
        public int SecondsLeft => Running ? Math.Max(0, (int)((endAt - Environment.TickCount64) / 1000)) : 0;

        public bool Start(IWorldAccessor world, int seconds)
        {
            if (Running) return false;
            try
            {
                profiler = world.FrameProfiler;
                if (profiler == null) { Error = "no profiler"; return false; }
                ByPath.Clear();
                Ticks = 0;
                Error = null;
                Seconds = Math.Max(5, Math.Min(120, seconds));
                wasEnabled = profiler.Enabled;
                profiler.Enabled = true;
                lastRoot = profiler.PrevRootEntry;
                StartedAt = DateTime.Now;
                endAt = Environment.TickCount64 + Seconds * 1000L;
                Running = true;
                return true;
            }
            catch (Exception e) { Error = e.Message; Running = false; return false; }
        }

        /// <summary>Call once per tick (or frame) while running.</summary>
        public void Tick()
        {
            if (!Running) return;
            try
            {
                ProfileEntryRange root = profiler.PrevRootEntry;
                if (root != null && !ReferenceEquals(root, lastRoot))
                {
                    lastRoot = root;
                    Ticks++;
                    Add(root, "", 0);
                }
                if (Environment.TickCount64 >= endAt) Stop();
            }
            catch (Exception e) { Error = e.Message; Stop(); }
        }

        public void Stop()
        {
            if (!Running) return;
            Running = false;
            FinishedAt = DateTime.Now;
            try { if (profiler != null && !wasEnabled && !profiler.PrintSlowTicks) profiler.Enabled = false; } catch { }
            lastRoot = null;
            try { OnFinished?.Invoke(this); } catch { }
        }

        void Add(ProfileEntryRange entry, string path, int depth)
        {
            if (entry == null || depth > 12) return;
            string here = path.Length == 0 ? entry.Code : path + "/" + entry.Code;
            Bump(here, entry.ElapsedTicks, entry.CallCount);
            if (entry.Marks != null)
                foreach (var kv in entry.Marks) Bump(here + "/" + kv.Key, kv.Value.ElapsedTicks, kv.Value.CallCount);
            if (entry.ChildRanges != null)
                foreach (ProfileEntryRange child in entry.ChildRanges.Values) Add(child, here, depth + 1);
        }

        void Bump(string key, long ticks, long calls)
        {
            ByPath.TryGetValue(key, out var v);
            ByPath[key] = (v.ticks + ticks, v.calls + calls);
        }

        public static double Ms(long stopwatchTicks) => stopwatchTicks * 1000.0 / Stopwatch.Frequency;

        /// <summary>Total time of one whole tick, averaged over the run (milliseconds), with and without the time spent asleep.</summary>
        public (double all, double work) PerTick()
        {
            if (Ticks == 0) return (0, 0);
            long all = 0, sleep = 0;
            foreach (var kv in ByPath)
            {
                if (kv.Key.IndexOf('/') < 0) all += kv.Value.ticks;
                else if (kv.Key.EndsWith("/sleep", StringComparison.Ordinal)) sleep += kv.Value.ticks;
            }
            return (Ms(all) / Ticks, Ms(all - sleep) / Ticks);
        }

        /// <summary>The entries that are not themselves split further (leaves), biggest first: (name, ms per tick, calls per tick).</summary>
        public List<(string path, string code, double ms, double calls)> Leaves()
        {
            var parents = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in ByPath.Keys)
            {
                int slash = key.LastIndexOf('/');
                if (slash > 0) parents.Add(key.Substring(0, slash));
            }
            var list = new List<(string, string, double, double)>();
            foreach (var kv in ByPath)
            {
                if (parents.Contains(kv.Key)) continue;
                string code = kv.Key.Substring(kv.Key.LastIndexOf('/') + 1);
                if (code == "sleep" || code == "end") continue;
                list.Add((kv.Key, code, Ms(kv.Value.ticks) / Math.Max(1, Ticks), (double)kv.Value.calls / Math.Max(1, Ticks)));
            }
            list.Sort((a, b) => b.Item3.CompareTo(a.Item3));
            return list;
        }

        static readonly string[] HandlerPrefixes = { "gmle", "gmlb", "sdcb", "dce", "dcb", "rendGui", "initbebehavior-" };

        /// <summary>The class a profiler entry names, when it names one ("gmleMyMod.Systems.Pipes" → "MyMod.Systems.Pipes").</summary>
        public static string TypeIn(string code)
        {
            foreach (string p in HandlerPrefixes)
                if (code.StartsWith(p, StringComparison.Ordinal) && code.Length > p.Length) return code.Substring(p.Length);
            return code.IndexOf('.') > 0 && code.IndexOf(' ') < 0 ? code : null;
        }
    }
}
