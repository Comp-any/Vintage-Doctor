using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace VintageDoctor
{
    public static partial class Checks
    {
        public const string TopicNetwork = "network";

        /// <summary>Bytes a second, averaged over a minute, above which a player's connection is likely to fall behind.</summary>
        public const double NetHighPerPlayer = 1.5 * 1024 * 1024;

        /// <summary>Whether this much traffic is a lot: over the limit for each player for a whole minute.</summary>
        public static bool NetHigh(double bytesPerSecond, int players, int seconds) => seconds >= 30 && bytesPerSecond > NetHighPerPlayer * Math.Max(1, players);

        /// <summary>
        /// The network traffic (1.9.0): what this side sends and receives now, graphs of the last minute and the last ten,
        /// and what a measured run found the traffic made of, by kind of packet and by mod.
        /// </summary>
        public static void Network(DoctorContext x, bool full)
        {
            NetWatch n = x.Doctor.Net;
            if (n == null) return;
            bool server = x.Side == EnumAppSide.Server;
            if (full) x.Section(x.T("sec-net"));
            if (!server && n.InProcess)
            {
                // Single player: the measured run of the world's side says what it is made of; this side has nothing to count.
                if (full) x.Info(x.T("net-single"));
                return;
            }
            if (n.Error != null) { x.Info(x.T("net-error", n.Error)); return; }
            var (sent, received, peakSent, peakReceived, seconds) = n.Rate(60);
            int players = 0;
            if (server) try { players = x.Api.World.AllOnlinePlayers.Length; } catch { }
            if (seconds < 10) x.Info(x.T("net-early", seconds));
            else if (server)
            {
                if (NetHigh(sent, players, seconds))
                {
                    x.Warn(x.T("net-high-server", DoctorReport.Rate(sent), seconds, DoctorReport.Rate(sent / Math.Max(1, players)), players));
                    x.Explain(x.T("net-high-why"));
                    x.Advice(x.T("net-high-advice"));
                }
                else x.Ok(x.T("net-ok-server", DoctorReport.Rate(sent), DoctorReport.Rate(received), seconds, players));
                x.Detail(x.T("net-totals-server", Size(n.TotalSent), Size(n.TotalReceived), DoctorReport.Rate(peakSent), DoctorReport.Rate(peakReceived)));
            }
            else
            {
                if (NetHigh(received, 1, seconds))
                {
                    x.Warn(x.T("net-high-client", DoctorReport.Rate(received), seconds));
                    x.Explain(x.T("net-high-why"));
                    x.Advice(x.T("net-high-advice"));
                }
                else x.Ok(x.T("net-ok-client", DoctorReport.Rate(received), seconds));
                x.Detail(x.T("net-totals-client", Size(n.TotalReceived), DoctorReport.Rate(peakReceived)));
            }
            if (!full)
            {
                if (!x.Compact) x.Buttons(DoctorReport.Open(TopicNetwork, "", x.T("btn-network")));
                return;
            }
            NetButtons(x);

            // The graphs: the last minute a second at a time, the last ten minutes in 10-second steps.
            List<NetWatch.Second> all = n.Seconds();
            List<NetWatch.Second> minute = all.Skip(Math.Max(0, all.Count - 60)).ToList();
            if (server)
            {
                x.Line(DoctorReport.Graph(x.T("net-graph-minute-server"), "bytes", NetWatch.Points(minute, s => s.Sent, 60), NetWatch.Points(minute, s => s.Received, 60)));
                if (all.Count > 90) x.Line(DoctorReport.Graph(x.T("net-graph-ten-server"), "bytes", NetWatch.Points(all, s => s.Sent, 60), NetWatch.Points(all, s => s.Received, 60)));
            }
            else
            {
                x.Line(DoctorReport.Graph(x.T("net-graph-minute-client"), "bytes", NetWatch.Points(minute, s => s.Received, 60)));
                if (all.Count > 90) x.Line(DoctorReport.Graph(x.T("net-graph-ten-client"), "bytes", NetWatch.Points(all, s => s.Received, 60)));
            }
            NetMeasured(x, n);
        }

        static void NetButtons(DoctorContext x)
        {
            NetWatch n = x.Doctor.Net;
            if (x.Compact || n == null) return;
            // The server's button measures the player's game too (the window starts both); the player's game alone in single player has nothing to count.
            if (n.Running || (x.Side == EnumAppSide.Client && x.Doctor.ServerAnswers)) return;
            x.Buttons(x.FixToken("doctor:netmeasure", "20", n.HasResult ? x.T("btn-netmeasure-again", 20) : x.T("btn-netmeasure", 20)));
        }

        /// <summary>What a measured run found: the traffic by kind of packet, and the mods' messages by channel.</summary>
        static void NetMeasured(DoctorContext x, NetWatch n)
        {
            bool server = x.Side == EnumAppSide.Server;
            x.Section(x.T("sec-net-measure"));
            if (n.Running) { x.Info(x.T("net-measure-running", n.SecondsLeft)); return; }
            if (!n.HasResult)
            {
                x.Info(x.T("net-measure-none"));
                x.Explain(x.T("net-measure-what", 20));
                if (n.MeasureError != null) x.Detail(n.MeasureError);
                return;
            }
            double secs = Math.Max(1, (n.FinishedAt - n.StartedAt).TotalSeconds);
            long total = server ? n.MeasuredSent : n.MeasuredReceived;
            if (server) x.Info(x.T("net-measure-title-server", N(secs, "0"), Time(n.FinishedAt), Size(n.MeasuredSent), Size(n.MeasuredReceived)));
            else x.Info(x.T("net-measure-title-client", N(secs, "0"), Time(n.FinishedAt), Size(n.MeasuredReceived)));
            x.Explain(server ? x.T("net-measure-explain-server") : x.T("net-measure-explain-client"));
            if (n.MeasureError != null) x.Detail(x.T("net-measure-failed", n.MeasureError));
            var kinds = n.Result.Where(k => k.Channel == null).ToList();
            // Mods' messages as one row here, by channel below.
            var channels = n.Result.Where(k => k.Channel != null).ToList();
            long modBytes = channels.Sum(k => k.Bytes);
            if (modBytes > 0) kinds.Add(new NetWatch.Kind { Name = x.T("net-kind-mods"), Bytes = modBytes, Count = channels.Sum(k => k.Count) });
            // Block entities are part of the chunks' and block entities' packets already: listed apart, not counted twice in the shares.
            long sum = Math.Max(1, Math.Max(total, kinds.Where(k => !k.Name.StartsWith("block entity ", StringComparison.Ordinal)).Sum(k => k.Bytes)));
            int shown = 0;
            foreach (var k in kinds.OrderByDescending(k => k.Bytes))
            {
                if (k.Bytes <= 0 || shown >= 18) break;
                shown++;
                x.Detail(DoctorReport.Rate(k.Bytes / secs) + "  " + N(k.Bytes * 100.0 / sum, "0") + "%  " + KindName(x, k.Name) + (k.Count > 0 ? "  (" + k.Count + "×)" : ""));
            }
            if (shown == 0) x.Detail(x.T("net-measure-nothing"));

            if (!server) return;
            if (channels.Count == 0) { x.Info(x.T("net-mods-none")); return; }
            var top = channels.OrderByDescending(k => k.Bytes).ToList();
            string Who(NetWatch.Kind k) => k.Mod != null ? x.ModName(k.Mod) : k.Channel;
            string title = x.T("net-mods-title", string.Join(", ", top.Take(4).Select(k => Who(k) + " " + DoctorReport.Rate(k.Bytes / secs))));
            bool heavy = top[0].Bytes / secs > 64 * 1024 && top[0].Bytes > sum * 0.3;
            if (heavy)
            {
                x.Warn(title);
                x.Explain(x.T("net-mods-heavy-why", Who(top[0]), N(top[0].Bytes * 100.0 / sum, "0")));
            }
            else x.Info(title);
            foreach (var k in top.Take(20))
                x.Detail(DoctorReport.Rate(k.Bytes / secs) + "  " + N(k.Bytes * 100.0 / sum, "0") + "%  " + Who(k) + (k.Mod != null ? " [" + k.Channel + "]" : "") + "  (" + k.Count + "×)"
                    + (k.Mod != null ? " " + DoctorReport.Open("mod", k.Mod, x.T("btn-modpage-short")) : ""));
            x.Explain(x.T("net-mods-why"));
        }

        /// <summary>A kind of packet in words where the doctor knows it; the game's own name otherwise.</summary>
        static string KindName(DoctorContext x, string name)
        {
            switch (name)
            {
                case "ServerChunks": case "Chunks": return x.T("net-kind-chunks");
                case "MapRegion": case "MapChunk": return x.T("net-kind-map");
                case "Entities": case "EntitySpawn": case "EntityAttributes": case "EntityAttributeUpdate": case "EntityPacket": return x.T("net-kind-entities", name);
                case "BlockEntities": case "BlockEntityMessage": return x.T("net-kind-blockentities", name);
                case "BulkEntityAttributes": case "BulkEntityDebugAttributes": return x.T("net-kind-entities", name);
                case "SetBlock": case "SetBlocks": case "BulkSetBlocks": case "SetDecors": case "SetBlocksMinimal": case "SetBlocksNoRelight": case "ModifyBlocks": return x.T("net-kind-blocks", name);
                case "UDP entity positions": case "UDP positions": case "EntityPosition": case "BulkEntityPosition": return x.T("net-kind-positions", name);
                default:
                    if (name.StartsWith("block entity ", StringComparison.Ordinal)) return x.T("net-kind-be", name.Substring(13));
                    return name;
            }
        }
    }
}
