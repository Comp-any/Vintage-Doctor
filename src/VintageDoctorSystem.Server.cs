using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VintageDoctor
{
    public partial class VintageDoctorSystem
    {
        ICoreServerAPI sapi;
        IServerNetworkChannel serverChannel;
        /// <summary>Players whose game has the doctor too (it says hello when it joins), so /doctor can open their window.</summary>
        readonly HashSet<string> clientsWithDoctor = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>What the check-up found right after start-up, for the log line and the note to admins.</summary>
        public int StartupProblems { get; private set; } = -1;
        public int StartupWarnings { get; private set; }
        string profileAsker;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            this.api = api;
            IsDedicated = api.Server.IsDedicated;
            serverChannel = api.Network.GetChannel(Channel);
            serverChannel.SetMessageHandler<DoctorRequestPacket>(OnRequest);
            BeginSession();
            api.Event.RegisterGameTickListener(_ => { Ticks.Tick(); MemoryTrend.Tick(); if (Profile.Running) Profile.Tick(); }, 1);
            Profile.OnFinished = _ => OnProfileFinished();
            RegisterCommand(api);
            api.Event.PlayerDisconnect += p => { lock (gate) clientsWithDoctor.Remove(p.PlayerUID); };
            api.Event.PlayerNowPlaying += OnPlayerNowPlaying;
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () =>
            {
                // A few seconds in, so the log holds the whole start-up.
                api.Event.RegisterCallback(_ => StartupScan(), 4000);
            });
        }

        void StartupScan()
        {
            if (!Config.ScanAtStartup && !Config.SelfTestOnStart) return;
            WithEvidence(true, ev =>
            {
                try
                {
                    DoctorReport r = BuildPage(ev, null, "en", Pages.Overview, "");
                    StartupProblems = r.Problems;
                    StartupWarnings = r.Warnings;
                    SaveText("startup-checkup.txt", CheckUp(r, ev, null, "en").PlainText(sapi.World));
                    if (Config.ScanAtStartup)
                    {
                        if (StartupProblems + StartupWarnings == 0) sapi.Logger.Notification("[vintagedoctor] Check-up after start-up: nothing wrong ({0} ms to read the logs and folders).", ev.ReadMilliseconds);
                        else sapi.Logger.Notification("[vintagedoctor] Check-up after start-up: {0} problem(s), {1} warning(s). Type /doctor to see them.", StartupProblems, StartupWarnings);
                    }
                }
                catch (Exception e) { sapi.Logger.Warning("[vintagedoctor] The check-up after start-up failed: {0}", e); }
                if (Config.SelfTestOnStart)
                {
                    try { SelfTest.Run(this, sapi); }
                    catch (Exception e) { sapi.Logger.Error("[vintagedoctor] The self-test failed to run: {0}", e); }
                }
            });
        }

        bool MaySeeServer(IServerPlayer player) => player != null && player.HasPrivilege(Config.ServerReportPrivilege);

        void OnPlayerNowPlaying(IServerPlayer player)
        {
            if (!Config.TellAdminsOnJoin || !MaySeeServer(player)) return;
            sapi.Event.RegisterCallback(_ =>
            {
                try
                {
                    if (player.ConnectionState != EnumClientState.Playing) return;
                    string lang = player.LanguageCode;
                    var parts = new List<string>();
                    if (NewCrashThisSession) parts.Add(Lang.GetL(lang, "vintagedoctor:join-crash"));
                    else if (PreviousUnclean) parts.Add(Lang.GetL(lang, "vintagedoctor:join-unclean"));
                    if (StartupProblems > 0) parts.Add(Lang.GetL(lang, "vintagedoctor:join-problems", StartupProblems, StartupWarnings));
                    if (parts.Count == 0) return;
                    player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(lang, "vintagedoctor:join-prefix") + " " + string.Join(" ", parts) + " " + Lang.GetL(lang, "vintagedoctor:join-how"), EnumChatType.Notification);
                }
                catch (Exception e) { sapi.Logger.Warning("[vintagedoctor] Could not tell {0} about the check-up: {1}", player?.PlayerName, e.Message); }
            }, 9000);
        }

        // ---------------------------------------------------------------- requests from players' windows

        void OnRequest(IServerPlayer player, DoctorRequestPacket p)
        {
            if (p == null || player == null) return;
            try
            {
                if (p.Topic == "hello") { lock (gate) clientsWithDoctor.Add(player.PlayerUID); return; }
                lock (gate) clientsWithDoctor.Add(player.PlayerUID);
                if (!MaySeeServer(player))
                {
                    serverChannel.SendPacket(new DoctorReportPacket { Id = p.Id, Denied = true, Key = (p.Topic ?? "") + "|" + (p.Arg ?? ""), ServerVersion = DoctorVersion }, player);
                    return;
                }
                ItemStack held = null;
                if (p.HeldStack != null && p.HeldStack.Length > 0)
                {
                    try { held = new ItemStack(p.HeldStack); if (!held.ResolveBlockOrItem(sapi.World)) held = null; } catch { held = null; }
                }
                held ??= player.InventoryManager?.ActiveHotbarSlot?.Itemstack;
                BlockPos looked = p.HasLookedAt ? new BlockPos(p.LookedAtX, p.LookedAtY, p.LookedAtZ, p.LookedAtDim) : null;
                string lang = player.LanguageCode;
                bool hasFix = !string.IsNullOrEmpty(p.FixId);
                ITreeAttribute facts = FactsFromBytes(p.ClientFacts);

                // A mod that needs time for its page or its fix: the player's window is told to keep waiting.
                void Working(int seconds)
                {
                    try { serverChannel.SendPacket(new DoctorReportPacket { Id = p.Id, Working = true, WaitSeconds = seconds, Key = (p.Topic ?? "") + "|" + (p.Arg ?? ""), ServerVersion = DoctorVersion }, player); }
                    catch { }
                }

                void Answer(Evidence ev, string outcome)
                {
                    string label = Lang.GetL(lang, p.SameProcess ? "vintagedoctor:side-world" : "vintagedoctor:side-server");
                    BuildPageThen(ev, player, lang, p.Topic, p.Arg, held, looked, p.SameProcess, label, facts, p.ClientHasTopic, r =>
                    {
                        if (player.ConnectionState != EnumClientState.Playing) return;
                        r.File = SaveText("last-report-server.txt", r.PlainText(sapi.World));
                        var topics = AllTopics();
                        // The player's game may not have a mod that has a page here: it gets the mod's name for the section's heading.
                        if (r.SectionTitles.Count > 0)
                        {
                            foreach (var t in topics)
                            {
                                ModEntry owner = ev.Mods.Get(t.ModId);
                                if (owner != null && !r.SectionTitles.ContainsKey(owner.Id)) r.SectionTitles[owner.Id] = string.IsNullOrEmpty(owner.Version) ? owner.Name : owner.Name + " " + owner.Version;
                            }
                        }
                        DoctorReportPacket packet = r.ToPacket(p.Id);
                        packet.Outcome = outcome ?? "";
                        packet.ServerVersion = DoctorVersion;
                        packet.ServerMods = ev.Mods.UserMods.Select(m => m.Key).ToArray();
                        // In the asker's language: their game may not have the mod whose lang key the question is.
                        packet.ServerTopics = topics.Select(t => t.Id + "|" + WordsIn(lang, t.Question ?? t.Id)).ToArray();
                        packet.ServerTopicMods = topics.Select(t => t.ModId ?? "").ToArray();
                        serverChannel.SendPacket(packet, player);
                    }, Working);
                }

                if (hasFix)
                {
                    WithEvidence(false, ev =>
                    {
                        if (p.FixId == "doctor:profile") profileAsker = player.PlayerUID;
                        // What was read may no longer be true after a fix.
                        RunFixThen(p.FixId, p.FixArg, player, lang, ev, facts, outcome => WithEvidence(true, fresh => Answer(fresh, outcome)), Working);
                    });
                    return;
                }
                WithEvidence(p.Fresh, ev => Answer(ev, null));
            }
            catch (Exception e)
            {
                sapi.Logger.Error("[vintagedoctor] A request from {0} failed: {1}", player.PlayerName, e);
            }
        }

        void OnProfileFinished()
        {
            if (sapi == null) return;
            string uid = profileAsker;
            profileAsker = null;
            if (uid == null) return;
            try
            {
                var player = sapi.World.PlayerByUid(uid) as IServerPlayer;
                bool has;
                lock (gate) has = clientsWithDoctor.Contains(uid);
                if (player != null && has && player.ConnectionState == EnumClientState.Playing) serverChannel.SendPacket(new DoctorOpenPacket { Topic = Checks.TopicPerf, Arg = "" }, player);
            }
            catch { }
        }

        // ---------------------------------------------------------------- /doctor

        void RegisterCommand(ICoreServerAPI api)
        {
            var parsers = api.ChatCommands.Parsers;
            api.ChatCommands.Create("doctor")
                .WithDescription("Vintage Doctor: find out what's wrong with the game and its mods. /doctor opens the check-up; /doctor crash, perf, mods, mod [mod id], log, files, world... open one page; /doctor fix [id] [argument] does a fix.")
                .RequiresPrivilege(Privilege.chat)
                .WithArgs(parsers.OptionalWord("page"), parsers.OptionalAll("what"))
                .HandleWith(OnCommand);
        }

        TextCommandResult OnCommand(TextCommandCallingArgs args)
        {
            string topic = (args[0] as string ?? "").Trim();
            string arg = (args[1] as string ?? "").Trim();
            string lang = args.LanguageCode ?? "en";
            try
            {
                var player = args.Caller?.Player as IServerPlayer;
                if (player != null)
                {
                    bool has;
                    lock (gate) has = clientsWithDoctor.Contains(player.PlayerUID);
                    if (has && !topic.Equals("fix", StringComparison.OrdinalIgnoreCase))
                    {
                        // The player's game asks back with its own half (its log, its settings), and shows the window.
                        serverChannel.SendPacket(new DoctorOpenPacket { Topic = topic, Arg = arg }, player);
                        return TextCommandResult.Success(Lang.GetL(lang, "vintagedoctor:cmd-opening"));
                    }
                    if (!MaySeeServer(player)) return TextCommandResult.Error(Lang.GetL(lang, "vintagedoctor:cmd-noprivilege", Config.ServerReportPrivilege));
                }
                if (topic.Equals("selftest", StringComparison.OrdinalIgnoreCase))
                {
                    if (player != null && !MaySeeServer(player)) return TextCommandResult.Error(Lang.GetL(lang, "vintagedoctor:cmd-noprivilege", Config.ServerReportPrivilege));
                    return TextCommandResult.Success(SelfTest.Run(this, sapi));
                }
                Evidence ev = EvidenceNow(true);
                // A command has to answer at once. When a mod needs time for its fix or its page, the answer says so,
                // and the result follows in chat (for a player) or in the server's console and log.
                void Later(string text)
                {
                    try
                    {
                        if (player != null) { if (player.ConnectionState == EnumClientState.Playing) player.SendMessage(GlobalConstants.GeneralChatGroup, ChatSafe(text), EnumChatType.CommandSuccess); }
                        else sapi.Logger.Notification("[vintagedoctor] /doctor {0} {1}\n{2}", topic, arg, text);
                    }
                    catch (Exception e) { sapi.Logger.Warning("[vintagedoctor] Could not pass on what a mod delivered later: {0}", e.Message); }
                }
                bool answered = false;
                if (topic.Equals("fix", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = arg.Split(new[] { ' ' }, 2);
                    if (parts[0].Length == 0) return TextCommandResult.Error(ChatSafe(Lang.GetL(lang, "vintagedoctor:cmd-fix-usage")));
                    string now = null;
                    RunFixThen(parts[0], parts.Length > 1 ? parts[1] : "", player, lang, ev, null, outcome =>
                    {
                        ForgetEvidence();
                        if (!answered) now = outcome; else Later(outcome);
                    });
                    answered = true;
                    ForgetEvidence();
                    if (now == null) return TextCommandResult.Success(Lang.GetL(lang, "vintagedoctor:cmd-working"));
                    return now.StartsWith(DoctorReport.WarnMark, StringComparison.Ordinal) ? TextCommandResult.Error(ChatSafe(now)) : TextCommandResult.Success(ChatSafe(now));
                }
                string page = null;
                BuildPageThen(ev, player, lang, topic, arg, player?.InventoryManager?.ActiveHotbarSlot?.Itemstack, null, false, null, null, false, r =>
                {
                    // The check-up is put in order here, by mod, as the window does for a player.
                    if (r.Key.StartsWith(Pages.Overview + "|", StringComparison.Ordinal)) r = CheckUp(r, ev, player, lang);
                    string text = r.PlainText(sapi.World);
                    string file = SaveText("last-report-server.txt", text);
                    if (file != null) text += "\n" + Lang.GetL(lang, "vintagedoctor:cmd-saved", Anonymise.Short(file));
                    if (!answered) page = text; else Later(text);
                });
                answered = true;
                if (page == null) return TextCommandResult.Success(Lang.GetL(lang, "vintagedoctor:cmd-working"));
                return TextCommandResult.Success(player != null ? ChatSafe(page) : page);
            }
            catch (Exception e)
            {
                sapi.Logger.Error("[vintagedoctor] /doctor failed: {0}", e);
                return TextCommandResult.Error("/doctor failed: " + e.Message + " (the server log has the details)");
            }
        }

        /// <summary>
        /// Text for chat: the report's marks and anything else the game's font may lack written in ordinary characters
        /// (chat is drawn by the game; the console isn't), then &lt; and &gt; written out, because chat reads them as
        /// markup and a stack trace would break the message.
        /// </summary>
        internal static string ChatSafe(string text) => GameFont.Drawable(text ?? "").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
