using ProtoBuf;

namespace VintageDoctor
{
    /// <summary>
    /// Client → server: build a page (the check-up, a mod's page...), or first do a fix and then build the page the
    /// player is on. The server answers with a <see cref="DoctorReportPacket"/> carrying the same Id.
    /// </summary>
    [ProtoContract]
    public class DoctorRequestPacket
    {
        [ProtoMember(1)] public int Id;
        [ProtoMember(2)] public string Topic;
        [ProtoMember(3)] public string Arg;
        /// <summary>A server-side fix to do first ("" for none) and its argument.</summary>
        [ProtoMember(4)] public string FixId;
        [ProtoMember(5)] public string FixArg;
        /// <summary>Read the logs and folders again instead of using what was read a moment ago.</summary>
        [ProtoMember(6)] public bool Fresh;
        /// <summary>What the player holds (ItemStack.ToBytes).</summary>
        [ProtoMember(7)] public byte[] HeldStack;
        /// <summary>The block the player is looking at on their computer.</summary>
        [ProtoMember(8)] public bool HasLookedAt;
        [ProtoMember(9)] public int LookedAtX;
        [ProtoMember(10)] public int LookedAtY;
        [ProtoMember(11)] public int LookedAtZ;
        [ProtoMember(12)] public int LookedAtDim;
        /// <summary>The player's game runs in the same program as the server (single player), so what both would
        /// see alike (the Mods folder, config files, code patches, memory) is only reported once, by the game.</summary>
        [ProtoMember(13)] public bool SameProcess;
        [ProtoMember(14)] public string ClientVersion;
        /// <summary>What mods on this game want their server halves to know: a tree of mod id → bytes, as bytes.</summary>
        [ProtoMember(15)] public byte[] ClientFacts;
        /// <summary>The page is one of a mod's that this game has itself, so a server that doesn't know it has nothing to add (and shouldn't answer with the check-up).</summary>
        [ProtoMember(16)] public bool ClientHasTopic;
    }

    /// <summary>Server → client: a page, as lines (see <see cref="DoctorReport"/>).</summary>
    [ProtoContract]
    public class DoctorReportPacket
    {
        [ProtoMember(1)] public int Id;
        [ProtoMember(2)] public string Title;
        [ProtoMember(3)] public string Key;
        [ProtoMember(4)] public string File;
        [ProtoMember(5)] public string[] Texts;
        [ProtoMember(6)] public byte[] Indents;
        [ProtoMember(7)] public byte[] Styles;
        /// <summary>The player may not see the server's report (not an admin).</summary>
        [ProtoMember(8)] public bool Denied;
        /// <summary>What a fix did, to show at the top ("" for none).</summary>
        [ProtoMember(9)] public string Outcome;
        [ProtoMember(10)] public string ServerVersion;
        /// <summary>The server's mods as "id@version", so the player's game can compare its own.</summary>
        [ProtoMember(11)] public string[] ServerMods;
        /// <summary>Pages other mods added on the server: "id|question".</summary>
        [ProtoMember(12)] public string[] ServerTopics;
        /// <summary>Not the page yet: a mod on the server is still working on it, for up to <see cref="WaitSeconds"/> more.</summary>
        [ProtoMember(13)] public bool Working;
        [ProtoMember(14)] public int WaitSeconds;
    }

    /// <summary>Server → client: open the doctor's window on this page (the player typed /doctor).</summary>
    [ProtoContract]
    public class DoctorOpenPacket
    {
        [ProtoMember(1)] public string Topic;
        [ProtoMember(2)] public string Arg;
    }
}
