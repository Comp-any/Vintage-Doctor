using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace VintageDoctor
{
    public partial class VintageDoctorSystem
    {
        public const string Hotkey = "vintagedoctor-open";

        ICoreClientAPI capi;
        IClientNetworkChannel clientChannel;
        GuiDialogDoctor dialog;
        /// <summary>The server has the doctor too and will answer with its half of a page.</summary>
        public bool ServerAnswers { get; private set; }
        /// <summary>The server's mods ("id@version") as its last answer gave them; null before the first.</summary>
        public string[] ServerMods { get; private set; }
        string[] serverTopics, serverTopicMods;

        /// <summary>One press of a button in the window: the page being put together from the game's half and the server's.</summary>
        sealed class PendingPage
        {
            public int Seq;
            public string Topic, Arg;
            public Evidence Ev;
            public DoctorReportPacket Server;
            public bool ServerDone, ServerMissing, TimedOut, Done;
            public string LocalOutcome;
            /// <summary>When to stop waiting for the server (the game's clock, ms). A server that says a mod is still working moves it.</summary>
            public long Deadline;
            /// <summary>What mods on this game sent along with the request.</summary>
            public ITreeAttribute Facts;
        }

        int requestSeq;
        PendingPage pending;

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            this.api = api;
            clientChannel = api.Network.GetChannel(Channel);
            clientChannel.SetMessageHandler<DoctorReportPacket>(OnServerReport);
            clientChannel.SetMessageHandler<DoctorOpenPacket>(p => OpenDoctor(p?.Topic ?? "", p?.Arg ?? ""));
            BeginSession();
            Net = new NetWatch(api);
            api.Event.RegisterGameTickListener(_ => { Ticks.Tick(); MemoryTrend.Tick(); if (Profile.Running) Profile.Tick(); if (Net.Running) Net.Tick(); }, 1);
            api.Event.RegisterGameTickListener(_ => Net.Sample(), 1000);
            Profile.OnFinished = _ => { if (dialog != null && dialog.IsOpened()) OpenDoctor(Checks.TopicPerf, ""); };
            // With the server measuring too, its answer opens the page; alone, this game does.
            Net.OnFinished = _ => { if (dialog != null && dialog.IsOpened() && !ServerAnswers) OpenDoctor(Checks.TopicNetwork, ""); };
            // While a measured run goes (this game's or the server's), the window counts it down.
            api.Event.RegisterGameTickListener(_ => ShowProfileCountdown(), 500);

            api.Input.RegisterHotKey(Hotkey, Lang.Get("vintagedoctor:hotkey"), GlKeys.D, HotkeyType.GUIOrOtherControls, ctrlPressed: true, shiftPressed: true);
            api.Input.SetHotKeyHandler(Hotkey, _ => ToggleDoctor());

            api.ChatCommands.Create("doctor")
                .WithDescription("Vintage Doctor: open the check-up (.doctor), one page (.doctor crash, perf, mods, mod [mod id], log, files...) or do a fix on this computer (.doctor fix [id] [argument])")
                .WithArgs(api.ChatCommands.Parsers.OptionalWord("page"), api.ChatCommands.Parsers.OptionalAll("what"))
                .HandleWith(OnClientCommand);

            // "/doctor" typed on a server that doesn't have the doctor still opens this game's half.
            api.Event.OnSendChatMessage += OnSendChat;
            // Tell the server this game has the doctor, so /doctor there can open the window here.
            api.Event.LevelFinalize += () =>
            {
                try
                {
                    if (api.Network.GetChannelState(Channel) == EnumChannelState.Connected) clientChannel.SendPacket(new DoctorRequestPacket { Topic = "hello", ClientVersion = DoctorVersion });
                }
                catch (Exception e) { api.Logger.Warning("[vintagedoctor] Could not greet the server: {0}", e.Message); }
            };
        }

        void OnSendChat(int groupId, ref string message, ref EnumHandling handled)
        {
            try
            {
                if (message == null || !message.StartsWith("/doctor", StringComparison.OrdinalIgnoreCase)) return;
                if (message.Length > 7 && message[7] != ' ') return;
                if (capi.Network.GetChannelState(Channel) == EnumChannelState.Connected) return; // the server's own /doctor handles it
                string[] parts = message.Substring(7).Trim().Split(new[] { ' ' }, 2);
                handled = EnumHandling.PreventDefault;
                string topic = parts[0], arg = parts.Length > 1 ? parts[1] : "";
                capi.Event.EnqueueMainThreadTask(() => OpenDoctor(topic, arg), "vintagedoctor-open");
            }
            catch { }
        }

        TextCommandResult OnClientCommand(TextCommandCallingArgs args)
        {
            string topic = (args[0] as string ?? "").Trim();
            string arg = (args[1] as string ?? "").Trim();
            if (topic.Equals("fix", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = arg.Split(new[] { ' ' }, 2);
                if (parts[0].Length == 0) return TextCommandResult.Error(ChatSafe(Lang.Get("vintagedoctor:cmd-fix-usage")));
                OpenDoctor(pending?.Topic ?? Pages.Overview, pending?.Arg ?? "", "c:" + parts[0], parts.Length > 1 ? parts[1] : "");
                return TextCommandResult.Success();
            }
            OpenDoctor(topic, arg);
            return TextCommandResult.Success();
        }

        bool ToggleDoctor()
        {
            if (capi?.World?.Player == null) return false;
            if (dialog != null && dialog.IsOpened()) { dialog.TryClose(); return true; }
            OpenDoctor(Pages.Overview, "");
            return true;
        }

        byte[] HeldStackBytes()
        {
            try { return capi?.World?.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack?.ToBytes(); }
            catch { return null; }
        }

        string ClientLang => Lang.CurrentLocale ?? "en";

        /// <summary>
        /// Opens a page: this game builds its half, the server (if it has the doctor) is asked for its half, and the
        /// window shows both. <paramref name="fix"/> is a fix to do first: "c:id" here, "s:id" on the server.
        /// </summary>
        public void OpenDoctor(string topic, string arg, string fix = null, string fixArg = null, bool fresh = false)
        {
            if (capi?.World?.Player == null) return;
            topic = Pages.Canonical(topic);
            arg = arg ?? "";
            var p = new PendingPage { Seq = ++requestSeq, Topic = topic, Arg = arg };
            pending = p;
            // The window opens at once, saying it is looking; the page fills in when both halves are there.
            if (dialog == null || !dialog.IsOpened())
            {
                var waiting = new DoctorReport { Title = Lang.Get("vintagedoctor:title"), Key = topic + "|" + arg };
                waiting.Add(Lang.Get("vintagedoctor:gui-scanning-long"), DoctorLineStyle.Note);
                Show(waiting.ToPacket(p.Seq));
            }
            dialog?.SetBusy(Lang.Get("vintagedoctor:gui-scanning"));

            bool serverFix = fix != null && fix.StartsWith("s:", StringComparison.Ordinal);
            bool clientFix = fix != null && fix.StartsWith("c:", StringComparison.Ordinal);
            try { ServerAnswers = capi.Network.GetChannelState(Channel) == EnumChannelState.Connected; } catch { ServerAnswers = false; }
            p.Facts = GatherClientFacts(topic, arg);

            if (ServerAnswers)
            {
                try
                {
                    var packet = new DoctorRequestPacket
                    {
                        Id = p.Seq, Topic = topic, Arg = arg, Fresh = fresh, HeldStack = HeldStackBytes(), SameProcess = capi.IsSinglePlayer, ClientVersion = DoctorVersion,
                        FixId = serverFix ? fix.Substring(2) : "", FixArg = serverFix ? fixArg ?? "" : "",
                        ClientFacts = FactsToBytes(p.Facts), ClientHasTopic = TopicFor(topic) != null
                    };
                    BlockPos looked = capi.World.Player.CurrentBlockSelection?.Position;
                    if (looked != null) { packet.HasLookedAt = true; packet.LookedAtX = looked.X; packet.LookedAtY = looked.Y; packet.LookedAtZ = looked.Z; packet.LookedAtDim = looked.dimension; }
                    clientChannel.SendPacket(packet);
                    // The server reads its logs off the main thread; a busy or crashed one may never answer.
                    int wait = serverFix ? 30000 : 15000;
                    p.Deadline = capi.ElapsedMilliseconds + wait;
                    capi.Event.RegisterCallback(_ => ServerTimeout(p), wait);
                }
                catch (Exception e)
                {
                    capi.Logger.Warning("[vintagedoctor] Could not ask the server: {0}", e.Message);
                    p.ServerDone = true;
                    p.ServerMissing = true;
                }
            }
            else
            {
                p.ServerDone = true;
                p.ServerMissing = true;
                if (serverFix) p.LocalOutcome = DoctorReport.WarnMark + " " + Lang.Get("vintagedoctor:fix-noserver");
            }

            if (clientFix)
            {
                // The fix may need what was read from the folders; then everything is read again.
                WithEvidence(false, ev =>
                {
                    RunFixThen(fix.Substring(2), fixArg, capi.World.Player, ClientLang, ev, p.Facts, outcome =>
                    {
                        p.LocalOutcome = outcome;
                        WithEvidence(true, after => { p.Ev = after; Finish(p); });
                    });
                });
            }
            else WithEvidence(fresh || serverFix, ev => { p.Ev = ev; Finish(p); });
        }

        /// <summary>When the server's measured run ends, by this game's clock (0: none running, as far as the last page said).</summary>
        long serverProfileEndsAt, serverNetEndsAt;

        /// <summary>
        /// The countdown in the window's status line while the profiler runs: on this game (its frames) or on the
        /// server (its ticks), each with its seconds left. The page opens by itself when a run is done.
        /// </summary>
        void ShowProfileCountdown()
        {
            if (dialog == null || !dialog.IsOpened()) return;
            int mine = Profile.Running ? Math.Max(1, Profile.SecondsLeft) : 0;
            long serverMs = serverProfileEndsAt - capi.ElapsedMilliseconds;
            int server = serverProfileEndsAt > 0 && serverMs > 0 ? (int)Math.Ceiling(serverMs / 1000.0) : 0;
            if (serverProfileEndsAt > 0 && serverMs <= -5000) serverProfileEndsAt = 0;
            string text = null;
            if (mine > 0 && server > 0) text = Lang.Get("vintagedoctor:gui-profiling-both", mine, server);
            else if (mine > 0) text = Lang.Get(capi.IsSinglePlayer ? "vintagedoctor:gui-profiling-game" : "vintagedoctor:gui-profiling-client", mine);
            else if (server > 0) text = Lang.Get(capi.IsSinglePlayer ? "vintagedoctor:gui-profiling-world" : "vintagedoctor:gui-profiling-server", server);
            if (text == null)
            {
                // The network's measured run, on either side.
                long netMs = serverNetEndsAt - capi.ElapsedMilliseconds;
                if (serverNetEndsAt > 0 && netMs <= -5000) serverNetEndsAt = 0;
                int net = Math.Max(Net != null && Net.Running ? Math.Max(1, Net.SecondsLeft) : 0, serverNetEndsAt > 0 && netMs > 0 ? (int)Math.Ceiling(netMs / 1000.0) : 0);
                if (net > 0) text = Lang.Get("vintagedoctor:gui-netmeasuring", net);
            }
            dialog.SetProfiling(text == null ? null : GameFont.Drawable(text));
        }

        /// <summary>The server hasn't answered in time. Unless it said a mod is still working: then the wait goes on until that time is up.</summary>
        void ServerTimeout(PendingPage p)
        {
            if (p.ServerDone || p != pending) return;
            long left = p.Deadline - capi.ElapsedMilliseconds;
            if (left > 200)
            {
                capi.Event.RegisterCallback(_ => ServerTimeout(p), (int)Math.Min(left, 60000));
                return;
            }
            p.TimedOut = true;
            p.ServerDone = true;
            Finish(p);
        }

        /// <summary>Opens the window here at a page (for other mods, through the API or the event bus).</summary>
        bool OpenHere(string topic, string arg)
        {
            if (capi?.World?.Player == null) return false;
            OpenDoctor(topic, arg);
            return true;
        }

        void OnServerReport(DoctorReportPacket packet)
        {
            PendingPage p = pending;
            if (packet == null || p == null || packet.Id != p.Seq || p.ServerDone) return;
            if (packet.Working)
            {
                // A mod on the server needs a while for its page or its fix: keep waiting, and say so.
                p.Deadline = Math.Max(p.Deadline, capi.ElapsedMilliseconds + (Math.Max(5, Math.Min(300, packet.WaitSeconds)) + 5) * 1000L);
                dialog?.SetBusy(Lang.Get("vintagedoctor:gui-working"));
                return;
            }
            p.Server = packet;
            p.ServerDone = true;
            if (packet.ServerMods != null) ServerMods = packet.ServerMods;
            if (packet.ServerTopics != null) { serverTopics = packet.ServerTopics; serverTopicMods = packet.ServerTopicMods; }
            serverProfileEndsAt = packet.ServerProfileLeft > 0 ? capi.ElapsedMilliseconds + packet.ServerProfileLeft * 1000L : 0;
            serverNetEndsAt = packet.ServerNetLeft > 0 ? capi.ElapsedMilliseconds + packet.ServerNetLeft * 1000L : 0;
            Finish(p);
        }

        /// <summary>Once this game's files are read and the server has answered (or won't): build the page and show it.</summary>
        void Finish(PendingPage p)
        {
            if (p != pending || p.Done || p.Ev == null || !p.ServerDone) return;
            p.Done = true;
            try
            {
                IClientPlayer player = capi.World.Player;
                ItemStack held = player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
                bool two = p.Server != null && !p.Server.Denied;
                // A page of a mod's that only the server has: this game has nothing to put beside it.
                bool serverHasTopic = two && p.Server.ServerTopics != null && p.Server.ServerTopics.Any(s => s != null && s.StartsWith(p.Topic + "|", StringComparison.Ordinal));
                BuildPageThen(p.Ev, player, ClientLang, p.Topic, p.Arg, held, player?.CurrentBlockSelection?.Position, capi.IsSinglePlayer, two ? Lang.Get("vintagedoctor:side-client") : null,
                    p.Facts, serverHasTopic, local => PutTogether(p, local, player, held, two), _ => dialog?.SetBusy(Lang.Get("vintagedoctor:gui-working")));
            }
            catch (Exception e)
            {
                capi.Logger.Error("[vintagedoctor] Could not put the page together: {0}", e);
                dialog?.SetIdle(Lang.Get("vintagedoctor:gui-failed", e.Message));
            }
        }

        /// <summary>This game's half, then the server's, then where to go next: the page as the window shows it.</summary>
        void PutTogether(PendingPage p, DoctorReport local, IClientPlayer player, ItemStack held, bool two)
        {
            if (p != pending) return;
            try
            {
                var final = new DoctorReport { Title = local.Title, Key = local.Key, File = local.File };
                foreach (string outcome in new[] { p.LocalOutcome, p.Server?.Outcome })
                {
                    if (string.IsNullOrEmpty(outcome)) continue;
                    final.Add(outcome, outcome.StartsWith(DoctorReport.WarnMark, StringComparison.Ordinal) ? DoctorLineStyle.Warn : DoctorLineStyle.Total);
                }
                string topic = local.Key.Split('|')[0];
                bool checkUp = topic == Pages.Overview;
                DoctorReport server = two ? DoctorReport.FromPacket(p.Server) : null;
                // Why the server's half isn't there, if it isn't.
                string without = two ? null : p.Server != null && p.Server.Denied ? Lang.Get("vintagedoctor:server-denied") : p.TimedOut ? Lang.Get("vintagedoctor:server-timeout")
                    : p.ServerMissing && !capi.IsSinglePlayer ? Lang.Get("vintagedoctor:server-missing") : null;
                var x = new DoctorContext { Doctor = this, Api = capi, Side = EnumAppSide.Client, Player = player, LanguageCode = ClientLang, Topic = p.Topic, Argument = p.Arg, HeldItem = held, Ev = p.Ev, R = final };
                if (checkUp)
                {
                    // The check-up is one list by mod: each mod's cards from this game, then from the server.
                    if (without != null) final.Add(without, DoctorLineStyle.Note);
                    var halves = new List<(DoctorReport half, string note)> { (local, null) };
                    if (server != null) halves.Add((server, Lang.Get(capi.IsSinglePlayer ? "vintagedoctor:note-world" : "vintagedoctor:note-server")));
                    Pages.Arrange(x, halves, two ? Pages.OtherTopics(serverTopics, serverTopicMods) : null, two, true);
                }
                else
                {
                    final.Lines.AddRange(local.Lines);
                    if (server != null) final.Lines.AddRange(server.Lines);
                    if (without != null) final.Add(without, DoctorLineStyle.Note);
                    Pages.Back(x);
                }
                SaveText("last-report-client.txt", final.PlainText(capi.World));
                Show(final.ToPacket(p.Seq));
            }
            catch (Exception e)
            {
                capi.Logger.Error("[vintagedoctor] Could not put the page together: {0}", e);
                dialog?.SetIdle(Lang.Get("vintagedoctor:gui-failed", e.Message));
            }
        }

        void Show(DoctorReportPacket packet)
        {
            if (dialog != null && dialog.IsOpened()) { dialog.Show(packet); return; }
            dialog = new GuiDialogDoctor(capi, packet, DoctorVersion, OnWindowAction);
            dialog.TryOpen();
        }

        /// <summary>
        /// Opens a file with whatever the computer opens such files with (a text editor, for a log). On Windows that
        /// is asked of the system directly, so a path with spaces in it works; elsewhere, and if that fails, the
        /// game's own way of opening a folder or an address does it (xdg-open, open).
        /// </summary>
        void OpenFile(string file)
        {
            try
            {
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true });
                    return;
                }
            }
            catch (Exception e) { capi.Logger.Notification("[vintagedoctor] Windows wouldn't open {0} ({1}); trying the game's way.", System.IO.Path.GetFileName(file), e.Message); }
            NetUtil.OpenUrlInBrowser(file);
        }

        /// <summary>A button pressed in the window.</summary>
        void OnWindowAction(string kind, string target, string arg)
        {
            try
            {
                switch (kind)
                {
                    case "open": OpenDoctor(target, arg); break;
                    case "refresh": OpenDoctor(target, arg, fresh: true); break;
                    case "fix":
                        // Measuring the network on the server measures what this game receives at the same time.
                        if (target == "s:doctor:netmeasure" && Net != null && !Net.Running && !capi.IsSinglePlayer)
                            Net.Start(int.TryParse(arg, out int secs) ? secs : 20);
                        OpenDoctor(pending?.Topic ?? Pages.Overview, pending?.Arg ?? "", target, arg);
                        break;
                    case "url":
                        if (target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) NetUtil.OpenUrlInBrowser(target);
                        break;
                    case "copy":
                        capi.Forms.SetClipboardText(target);
                        dialog?.SetIdle(Lang.Get("vintagedoctor:gui-copied"));
                        break;
                    case "folder":
                        {
                            string path = target == DoctorReport.LogsFolder ? GamePaths.Logs : target == "mods" ? GamePaths.DataPathMods : target == "modconfig" ? GamePaths.ModConfig : target == "doctor" ? DataFolder : null;
                            if (path == null) break;
                            // One file of the folder (a log), when it is there; the folder when it isn't (no crash
                            // report was ever written, say). The name may come from a server or another mod: only a
                            // plain log or text file's name gets as far as being opened.
                            if (!string.IsNullOrEmpty(arg) && DoctorReport.OpenableFile(arg))
                            {
                                string file = System.IO.Path.Combine(path, arg);
                                if (System.IO.File.Exists(file))
                                {
                                    OpenFile(file);
                                    dialog?.SetIdle(Lang.Get("vintagedoctor:gui-opened", arg));
                                    break;
                                }
                                NetUtil.OpenUrlInBrowser(path);
                                dialog?.SetIdle(Lang.Get("vintagedoctor:gui-nofile", arg));
                                break;
                            }
                            NetUtil.OpenUrlInBrowser(path);
                            dialog?.SetIdle(Lang.Get("vintagedoctor:gui-opened-folder"));
                            break;
                        }
                }
            }
            catch (Exception e) { capi.Logger.Warning("[vintagedoctor] The window's button '{0}' failed: {1}", kind, e.Message); }
        }
    }
}
