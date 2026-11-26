using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace VintageDoctor
{
    /// <summary>
    /// The game's network traffic, as the game itself counts it (1.9.0). Read from 1.22.7's code:
    /// <list type="bullet">
    /// <item>The server keeps totals since it started: <c>IServerAPI.TotalSentBytes</c> / <c>TotalReceivedBytes</c>
    /// (TCP), and <c>TotalSentBytesUdp</c> / <c>TotalReceivedBytesUdp</c> on its ServerAPI class. It also has a hidden
    /// count by kind of packet, <c>ServerMain.doNetBenchmark</c> with <c>packetBenchmark</c> (how many),
    /// <c>packetBenchmarkBytes</c> and <c>udpPacketBenchmark(Bytes)</c>, the kinds named by the constants of
    /// <c>Packet_ServerIdEnum</c>.</item>
    /// <item>The player's game counts what it receives: <c>SystemNetworkProcess.totalByteCount</c> and
    /// <c>totalUdpByteCount</c> (what its debug screen shows as "Network TCP/UDP"), and by kind with
    /// <c>doBenchmark</c> / <c>packetBenchmark</c> (bytes), the command .netbenchmark. It doesn't count what it sends.</item>
    /// <item>Mods' messages are all one kind of packet (CustomPacket, 55), told apart only by their channel. To say which
    /// mod sends how much, the doctor counts them itself while a measured run goes, with two small Harmony patches on
    /// the server's <c>ServerMain.SendArbitraryPacket</c> and <c>BroadcastArbitraryPacket</c> (the only ways a mod's
    /// channel sends), installed on the first run and doing nothing between runs.</item>
    /// </list>
    /// Everything is read by reflection: when the game changes, the doctor says it couldn't read it, and nothing breaks.
    /// </summary>
    public sealed class NetWatch
    {
        /// <summary>One second's traffic: bytes sent and received, TCP and UDP. The player's game only knows what it received.</summary>
        public struct Second
        {
            public long SentTcp, SentUdp, RecvTcp, RecvUdp;
            public int Players;
            public long Sent => SentTcp + SentUdp;
            public long Received => RecvTcp + RecvUdp;
        }

        /// <summary>The last ten minutes, a second each.</summary>
        public const int Keep = 600;
        readonly Queue<Second> seconds = new Queue<Second>();
        readonly object gate = new object();
        long lastSentTcp = -1, lastSentUdp, lastRecvTcp, lastRecvUdp, lastAt;
        public long TotalSent, TotalReceived;
        public string Error;
        /// <summary>The player's game in single player talks to its server inside the same program: nothing goes over a network.</summary>
        public bool InProcess;
        public readonly bool IsServer;
        readonly ICoreAPI api;

        public NetWatch(ICoreAPI api)
        {
            this.api = api;
            IsServer = api.Side == EnumAppSide.Server;
        }

        // ---------------------------------------------------------------- reading the game's own counters

        object clientSystem;
        static readonly BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static object Get(object o, string name)
        {
            if (o == null) return null;
            Type t = o.GetType();
            while (t != null)
            {
                FieldInfo f = t.GetField(name, All | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(o);
                PropertyInfo p = t.GetProperty(name, All | BindingFlags.DeclaredOnly);
                if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(o);
                t = t.BaseType;
            }
            return null;
        }

        static bool Set(object o, string name, object value)
        {
            Type t = o?.GetType();
            while (t != null)
            {
                FieldInfo f = t.GetField(name, All | BindingFlags.DeclaredOnly);
                if (f != null) { f.SetValue(o, value); return true; }
                t = t.BaseType;
            }
            return false;
        }

        static long Long(object v) => v == null ? 0 : Convert.ToInt64(v);

        /// <summary>The player's game's network system (SystemNetworkProcess), found among the game's systems.</summary>
        object ClientSystem()
        {
            if (clientSystem != null) return clientSystem;
            object game = api.World;
            Type t = game?.GetType();
            while (t != null && clientSystem == null)
            {
                foreach (FieldInfo f in t.GetFields(All | BindingFlags.DeclaredOnly))
                {
                    if (!typeof(IEnumerable).IsAssignableFrom(f.FieldType) || f.FieldType == typeof(string)) continue;
                    object v;
                    try { v = f.GetValue(game); } catch { continue; }
                    if (!(v is IEnumerable list) || v is IDictionary) continue;
                    try
                    {
                        foreach (object o in list)
                        {
                            if (o != null && o.GetType().Name == "SystemNetworkProcess") { clientSystem = o; break; }
                        }
                    }
                    catch { }
                    if (clientSystem != null) break;
                }
                t = t.BaseType;
            }
            return clientSystem;
        }

        /// <summary>Reads the totals and keeps the last second's difference. Call once a second.</summary>
        public void Sample()
        {
            try
            {
                long sentTcp = 0, sentUdp = 0, recvTcp, recvUdp;
                int players = 0;
                if (IsServer)
                {
                    var sapi = (ICoreServerAPI)api;
                    sentTcp = sapi.Server.TotalSentBytes;
                    recvTcp = sapi.Server.TotalReceivedBytes;
                    sentUdp = Long(Get(sapi.Server, "TotalSentBytesUdp"));
                    recvUdp = Long(Get(sapi.Server, "TotalReceivedBytesUdp"));
                    players = sapi.World.AllOnlinePlayers.Length;
                }
                else
                {
                    InProcess = (api as Vintagestory.API.Client.ICoreClientAPI)?.IsSinglePlayer == true;
                    object sys = ClientSystem();
                    if (sys == null) { Error = "the game's network counter (SystemNetworkProcess) wasn't found"; return; }
                    recvTcp = Long(Get(sys, "totalByteCount"));
                    recvUdp = Long(Get(sys, "totalUdpByteCount"));
                }
                Error = null;
                lock (gate)
                {
                    long now = Environment.TickCount64;
                    // A tick listener comes late when the game lags: the difference is shared out over the seconds it took.
                    double per = lastSentTcp >= 0 ? Math.Max(0.5, (now - lastAt) / 1000.0) : 1;
                    long D(long a, long b) => (long)(Math.Max(0, a - b) / per);
                    lastAt = now;
                    if (lastSentTcp >= 0)
                    {
                        // A counter that went down was restarted (an int that wrapped, a reconnect): that second counts nothing.
                        var s = new Second
                        {
                            SentTcp = D(sentTcp, lastSentTcp), SentUdp = D(sentUdp, lastSentUdp),
                            RecvTcp = D(recvTcp, lastRecvTcp), RecvUdp = D(recvUdp, lastRecvUdp), Players = players
                        };
                        seconds.Enqueue(s);
                        while (seconds.Count > Keep) seconds.Dequeue();
                    }
                    lastSentTcp = sentTcp; lastSentUdp = sentUdp; lastRecvTcp = recvTcp; lastRecvUdp = recvUdp;
                    TotalSent = sentTcp + sentUdp;
                    TotalReceived = recvTcp + recvUdp;
                }
            }
            catch (Exception e) { Error = e.Message; }
        }

        /// <summary>The seconds kept, oldest first.</summary>
        public List<Second> Seconds()
        {
            lock (gate) return seconds.ToList();
        }

        /// <summary>Average bytes a second over the last <paramref name="n"/> seconds, sent and received; and the busiest second's.</summary>
        public (double sent, double received, long peakSent, long peakReceived, int seconds) Rate(int n)
        {
            List<Second> all = Seconds();
            List<Second> last = all.Skip(Math.Max(0, all.Count - n)).ToList();
            if (last.Count == 0) return (0, 0, 0, 0, 0);
            return (last.Average(s => (double)s.Sent), last.Average(s => (double)s.Received), last.Max(s => s.Sent), last.Max(s => s.Received), last.Count);
        }

        /// <summary>The kept seconds put together into at most <paramref name="points"/> points (averages), for a graph.</summary>
        public static List<double> Points(List<Second> all, Func<Second, double> value, int points)
        {
            var list = new List<double>();
            if (all.Count == 0) return list;
            int per = Math.Max(1, (int)Math.Ceiling(all.Count / (double)points));
            for (int i = 0; i < all.Count; i += per) list.Add(all.Skip(i).Take(per).Average(value));
            return list;
        }

        // ---------------------------------------------------------------- a measured run: what the traffic is made of

        public sealed class Kind
        {
            public string Name;
            public long Count, Bytes;
            public bool Udp;
            /// <summary>For a mod's channel: the channel's name and the mod it most likely belongs to.</summary>
            public string Channel, Mod;
        }

        public bool Running;
        public int RunSeconds, Ticks;
        public DateTime StartedAt, FinishedAt;
        public string MeasureError;
        public readonly List<Kind> Result = new List<Kind>();
        public long MeasuredSent, MeasuredReceived;
        public Action<NetWatch> OnFinished;
        long endAt, sentAtStart, receivedAtStart;
        bool benchmarkWas;

        public int SecondsLeft => Running ? Math.Max(0, (int)((endAt - Environment.TickCount64) / 1000)) : 0;
        public bool HasResult => !Running && FinishedAt > StartedAt;
        /// <summary>How many of the server's send methods the counting by channel is patched into (2 when all is well).</summary>
        public static int ChannelPatches => ChannelCount.Patched;
        /// <summary>Whether the game's own count by kind is switched on now.</summary>
        public bool GameCounting => Get(BenchmarkHolder, BenchmarkFlag) is bool b && b;

        /// <summary>The game's own count by kind is switched on through this; the server's ServerMain or the player's SystemNetworkProcess.</summary>
        object BenchmarkHolder => IsServer ? (object)api.World : ClientSystem();
        string BenchmarkFlag => IsServer ? "doNetBenchmark" : "doBenchmark";

        public bool Start(int seconds)
        {
            if (Running) return false;
            try
            {
                object holder = BenchmarkHolder;
                if (holder == null) { MeasureError = "the game's network counter wasn't found"; return false; }
                benchmarkWas = Get(holder, BenchmarkFlag) is bool b && b;
                foreach (string name in new[] { "packetBenchmark", "packetBenchmarkBytes", "packetBenchmarkBlockEntitiesBytes", "udpPacketBenchmark", "udpPacketBenchmarkBytes" })
                    (Get(holder, name) as IDictionary)?.Clear();
                if (!Set(holder, BenchmarkFlag, true)) { MeasureError = "the game's switch for counting by kind (" + BenchmarkFlag + ") wasn't found"; return false; }
                if (IsServer) ChannelCount.Begin(api);
                RunSeconds = Math.Max(5, Math.Min(120, seconds));
                MeasureError = null;
                Sample();
                sentAtStart = TotalSent;
                receivedAtStart = TotalReceived;
                StartedAt = DateTime.Now;
                endAt = Environment.TickCount64 + RunSeconds * 1000L;
                Running = true;
                return true;
            }
            catch (Exception e) { MeasureError = e.Message; return false; }
        }

        /// <summary>Call often while running; ends the run when its time is up.</summary>
        public void Tick()
        {
            if (Running && Environment.TickCount64 >= endAt) Stop();
        }

        public void Stop()
        {
            if (!Running) return;
            Running = false;
            FinishedAt = DateTime.Now;
            Result.Clear();
            try
            {
                Sample();
                MeasuredSent = TotalSent - sentAtStart;
                MeasuredReceived = TotalReceived - receivedAtStart;
                object holder = BenchmarkHolder;
                Dictionary<int, string> names = PacketNames(holder);
                if (IsServer)
                {
                    var counts = Get(holder, "packetBenchmark") as IDictionary;
                    var bytes = Get(holder, "packetBenchmarkBytes") as IDictionary;
                    if (bytes != null) foreach (DictionaryEntry e in bytes)
                        {
                            int id = Convert.ToInt32(e.Key);
                            if (id == 55) continue;   // mods' messages: by channel, below
                            Result.Add(new Kind { Name = names.TryGetValue(id, out string n) ? n : "packet " + id, Bytes = Long(e.Value), Count = counts != null && counts.Contains(e.Key) ? Long(counts[e.Key]) : 0 });
                        }
                    var udpCounts = Get(holder, "udpPacketBenchmark") as IDictionary;
                    var udpBytes = Get(holder, "udpPacketBenchmarkBytes") as IDictionary;
                    if (udpBytes != null) foreach (DictionaryEntry e in udpBytes)
                            Result.Add(new Kind { Name = "UDP " + UdpName(Convert.ToInt32(e.Key)), Bytes = Long(e.Value), Count = udpCounts != null && udpCounts.Contains(e.Key) ? Long(udpCounts[e.Key]) : 0, Udp = true });
                    if (Get(holder, "packetBenchmarkBlockEntitiesBytes") is IDictionary be)
                        foreach (DictionaryEntry e in be) Result.Add(new Kind { Name = "block entity " + e.Key, Bytes = Long(e.Value) });
                    Result.AddRange(ChannelCount.End(api));
                }
                else
                {
                    // The player's game counts bytes received by kind (no number of packets).
                    if (Get(holder, "packetBenchmark") is IDictionary bytes) foreach (DictionaryEntry e in bytes)
                        {
                            int id = Convert.ToInt32(e.Key);
                            Result.Add(new Kind { Name = names.TryGetValue(id, out string n) ? n : "packet " + id, Bytes = Long(e.Value) });
                        }
                    if (Get(holder, "udpPacketBenchmark") is IDictionary udp) foreach (DictionaryEntry e in udp)
                            Result.Add(new Kind { Name = "UDP " + UdpName(Convert.ToInt32(e.Key)), Bytes = Long(e.Value), Udp = true });
                }
                if (!benchmarkWas) Set(holder, BenchmarkFlag, false);
            }
            catch (Exception e) { MeasureError = e.Message; }
            finally { if (IsServer) ChannelCount.Stop(); }
            Result.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
            try { OnFinished?.Invoke(this); } catch { }
        }

        /// <summary>The names of the kinds of packet, from the constants of the game's Packet_ServerIdEnum ("Chunks", "Entities"...).</summary>
        static Dictionary<int, string> PacketNames(object fromGame)
        {
            var names = new Dictionary<int, string>();
            try
            {
                Type e = fromGame?.GetType().Assembly.GetType("Packet_ServerIdEnum") ?? AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Packet_ServerIdEnum")).FirstOrDefault(t => t != null);
                if (e != null)
                    foreach (FieldInfo f in e.GetFields(BindingFlags.Public | BindingFlags.Static))
                        if (f.IsLiteral && f.FieldType == typeof(int)) names[(int)f.GetValue(null)] = f.Name;
            }
            catch { }
            return names;
        }

        static string UdpName(int id) => id switch { 1 => "positions", 2 => "entity positions", 3 => "player position", 4 => "mod message", _ => "packet " + id };

        // ---------------------------------------------------------------- mods' messages, by channel (server)

        /// <summary>
        /// Counts the server's mod messages by channel while a run goes. Two Harmony prefixes on ServerMain, installed
        /// once; outside a run they return at once.
        /// </summary>
        static class ChannelCount
        {
            static volatile bool counting;
            static bool patched;
            public static int Patched;
            static readonly Dictionary<int, (long count, long bytes)> byChannel = new Dictionary<int, (long, long)>();
            static FieldInfo idField, customField, channelField, dataField;
            static ICoreServerAPI sapi;

            public static void Begin(ICoreAPI api)
            {
                sapi = api as ICoreServerAPI;
                lock (byChannel) byChannel.Clear();
                if (!patched)
                {
                    try
                    {
                        Type main = api.World.GetType();
                        var harmony = new Harmony("vintagedoctor.netcount");
                        foreach (string name in new[] { "SendArbitraryPacket", "BroadcastArbitraryPacket" })
                        {
                            MethodInfo m = main.GetMethods(All).FirstOrDefault(x => x.Name == name && x.GetParameters().Length == 2 && x.GetParameters()[0].ParameterType.Name == "Packet_Server");
                            if (m == null) continue;
                            Patched++;
                            harmony.Patch(m, prefix: new HarmonyMethod(typeof(ChannelCount).GetMethod(name == "SendArbitraryPacket" ? nameof(OnSend) : nameof(OnBroadcast), BindingFlags.Static | BindingFlags.NonPublic)));
                        }
                    }
                    catch (Exception e) { api.Logger.Notification("[vintagedoctor] Counting mods' network messages isn't possible here: {0}", e.Message); }
                    patched = true;
                }
                counting = true;
            }

            public static void Stop() => counting = false;

            static void OnSend(object[] __args) { if (counting) Count(__args, false); }
            static void OnBroadcast(object[] __args) { if (counting) Count(__args, true); }

            static void Count(object[] args, bool broadcast)
            {
                try
                {
                    object packet = args[0];
                    if (packet == null) return;
                    Type t = packet.GetType();
                    idField ??= t.GetField("Id");
                    if (!(idField?.GetValue(packet) is int id) || id != 55) return;
                    customField ??= t.GetField("CustomPacket");
                    object custom = customField?.GetValue(packet);
                    if (custom == null) return;
                    channelField ??= custom.GetType().GetField("ChannelId");
                    dataField ??= custom.GetType().GetField("Data");
                    int channel = channelField?.GetValue(custom) is int c ? c : -1;
                    long size = (dataField?.GetValue(custom) as byte[])?.Length ?? 0;
                    int skip = (args.Length > 1 ? args[1] as Array : null)?.Length ?? 0;
                    long to = broadcast ? Math.Max(0, (sapi?.World.AllOnlinePlayers.Length ?? 0) - skip) : skip;
                    if (to == 0) return;
                    lock (byChannel)
                    {
                        byChannel.TryGetValue(channel, out var v);
                        byChannel[channel] = (v.count + to, v.bytes + (size + 8) * to);   // + the packet's own few bytes
                    }
                }
                catch { }
            }

            /// <summary>What was counted, by channel, with the channel's name (from the server's NetworkAPI) and its likely mod.</summary>
            public static List<Kind> End(ICoreAPI api)
            {
                counting = false;
                var names = new Dictionary<int, string>();
                try
                {
                    if (Get(api.Network, "channels") is IEnumerable channels)
                        foreach (object kv in channels)
                        {
                            object key = Get(kv, "Key"), value = Get(kv, "Value");
                            if (key is int id && value is INetworkChannel ch) names[id] = ch.ChannelName;
                        }
                }
                catch { }
                var list = new List<Kind>();
                lock (byChannel)
                    foreach (var kv in byChannel)
                    {
                        names.TryGetValue(kv.Key, out string name);
                        list.Add(new Kind { Name = "mod messages", Channel = name ?? "channel " + kv.Key, Count = kv.Value.count, Bytes = kv.Value.bytes, Mod = ModOfChannel(api, name) });
                    }
                return list;
            }
        }

        /// <summary>The mod a channel most likely belongs to: its name is the mod's id, starts with it, or holds its name.</summary>
        public static string ModOfChannel(ICoreAPI api, string channel)
        {
            if (string.IsNullOrEmpty(channel)) return null;
            string c = channel.ToLowerInvariant();
            Mod best = null;
            foreach (Mod m in api.ModLoader.Mods)
            {
                string id = m.Info?.ModID?.ToLowerInvariant();
                if (string.IsNullOrEmpty(id) || id == "game" || id == "survival" || id == "creative") continue;
                string name = (m.Info?.Name ?? "").Replace(" ", "").ToLowerInvariant();
                bool fits = c == id || c.StartsWith(id) || (id.Length >= 4 && c.Contains(id)) || (name.Length >= 4 && c.Replace("-", "").Replace("_", "").Contains(name));
                if (fits && (best == null || id.Length > best.Info.ModID.Length)) best = m;
            }
            return best?.Info?.ModID;
        }
    }
}
