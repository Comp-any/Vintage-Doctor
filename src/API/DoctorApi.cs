using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace VintageDoctor.API
{
    /// <summary>How serious a finding is. The doctor's window shows each as a card of that colour.</summary>
    public enum DoctorLevel
    {
        /// <summary>A test that passed (green).</summary>
        Ok = 0,
        /// <summary>Something worth knowing that is neither good nor bad (grey).</summary>
        Info = 1,
        /// <summary>Something that may affect play (yellow).</summary>
        Warning = 2,
        /// <summary>Something is not running properly, or could crash (red).</summary>
        Problem = 3
    }

    /// <summary>
    /// What other mods use to hand Vintage Doctor their own checks, fixes and pages.
    /// Get it with <see cref="DoctorApi.Get"/> (null when Vintage Doctor isn't installed). Each side of the game has
    /// its own: register server things in StartServerSide and client things in StartClientSide.
    /// <para>Mods that don't want to reference this DLL can use the event bus or a JSON file instead: see docs/api.md.</para>
    /// </summary>
    public interface IDoctorApi
    {
        /// <summary>Vintage Doctor's version, e.g. "1.0.0".</summary>
        string DoctorVersion { get; }

        /// <summary>The API's version. Raised only when something is added; nothing is ever removed from version 1.</summary>
        int ApiVersion { get; }

        /// <summary>Adds a check to the check-up (and to your mod's own page in the doctor). Registering the same Id again replaces it.</summary>
        void RegisterCheck(DoctorCheck check);

        /// <summary>Adds something the doctor can do when the player presses a fix button a check offered.</summary>
        void RegisterFix(DoctorFix fix);

        /// <summary>Adds a page of your own, listed under "What are you seeing?" and opened with /doctor &lt;id&gt;.</summary>
        void RegisterTopic(DoctorTopic topic);

        /// <summary>Teaches the doctor a log message: when it shows up in the log, the doctor explains it in your words.</summary>
        void RegisterKnownProblem(KnownProblem problem);

        /// <summary>
        /// Tells the doctor about something that just went wrong while playing (a machine that jammed, a save that failed).
        /// It is kept for this session and shown in the check-up under your mod's name. Safe to call from any thread.
        /// </summary>
        void ReportIncident(string modId, DoctorLevel level, string title, string details = null);

        /// <summary>Removes everything registered with this mod id (for mods that reload themselves).</summary>
        void Unregister(string modId);
    }

    /// <summary>One check: a piece of code that looks at something and says what it found.</summary>
    public sealed class DoctorCheck
    {
        /// <summary>A unique id, best prefixed with your mod id: "mymod:pipes".</summary>
        public string Id;
        /// <summary>Your mod id. The doctor shows the mod's name before your findings and lists them on the mod's page.</summary>
        public string ModId;
        /// <summary>Only run when the player asks for the deeper checks (for anything that takes more than a few milliseconds).</summary>
        public bool Deep;
        /// <summary>
        /// The check itself. Call <see cref="IDoctorContext.Ok"/>, <see cref="IDoctorContext.Warn"/>... once or more.
        /// Runs on the game's main thread, so it may read the world. An exception is caught and shown as a problem
        /// with your check's name; it never takes the game down.
        /// </summary>
        public Action<IDoctorContext> Run;

        public DoctorCheck() { }
        public DoctorCheck(string modId, string id, Action<IDoctorContext> run) { ModId = modId; Id = id; Run = run; }
    }

    /// <summary>Something the doctor does when a fix button is pressed.</summary>
    public sealed class DoctorFix
    {
        /// <summary>A unique id, best prefixed with your mod id: "mymod:reconnect". This is what a check passes to <see cref="IDoctorContext.FixButton"/>.</summary>
        public string Id;
        /// <summary>Your mod id.</summary>
        public string ModId;
        /// <summary>
        /// Does the fix and returns one line saying what happened (shown at the top of the window). Return
        /// <see cref="IDoctorFixContext.Failed"/> when it didn't work. On the server it only runs for players allowed
        /// to see the server's report (admins by default) and for the console.
        /// </summary>
        public Func<IDoctorFixContext, string> Apply;

        public DoctorFix() { }
        public DoctorFix(string modId, string id, Func<IDoctorFixContext, string> apply) { ModId = modId; Id = id; Apply = apply; }
    }

    /// <summary>A page of your own in the doctor.</summary>
    public sealed class DoctorTopic
    {
        /// <summary>What comes after /doctor to open it: lower-case letters, digits and '-' only, e.g. "mymod-network".</summary>
        public string Id;
        /// <summary>Your mod id.</summary>
        public string ModId;
        /// <summary>The line shown in the doctor's list of questions, e.g. "My pipes aren't moving anything".</summary>
        public string Question;
        /// <summary>Builds the page. <see cref="IDoctorContext.Argument"/> is whatever followed the id.</summary>
        public Action<IDoctorContext> Run;

        public DoctorTopic() { }
        public DoctorTopic(string modId, string id, string question, Action<IDoctorContext> run) { ModId = modId; Id = id; Question = question; Run = run; }
    }

    /// <summary>A log message the doctor should recognise and explain.</summary>
    public sealed class KnownProblem
    {
        /// <summary>A unique id, e.g. "mymod:old-config".</summary>
        public string Id;
        /// <summary>The mod the message is about (the doctor counts the finding against it).</summary>
        public string ModId;
        /// <summary>Text the log line must contain; or a regular expression when <see cref="IsRegex"/> is set. Case is ignored.</summary>
        public string Match;
        public bool IsRegex;
        /// <summary>Optional: text that must also appear in the stack trace under the line.</summary>
        public string MatchStack;
        /// <summary>"server", "client" or "both" (the default): which log to look in.</summary>
        public string Side = "both";
        public DoctorLevel Level = DoctorLevel.Warning;
        /// <summary>The finding, in plain words. A lang key ("mymod:doctor-oldconfig") works too.</summary>
        public string Title;
        /// <summary>Optional: why it happens.</summary>
        public string Explain;
        /// <summary>Optional: what to do about it.</summary>
        public string Advice;
        /// <summary>Optional: the id of a registered fix to offer as a button, its argument and the button's words.</summary>
        public string FixId, FixArgument, FixLabel;
    }

    /// <summary>What a check or a page writes its findings to.</summary>
    public interface IDoctorContext
    {
        /// <summary>The game's API for this side (cast to ICoreServerAPI / ICoreClientAPI as needed).</summary>
        ICoreAPI Api { get; }
        EnumAppSide Side { get; }
        /// <summary>Who asked. Null when the console or a start-up scan asked.</summary>
        IPlayer Player { get; }
        /// <summary>The asker's language ("en", "de"...), for <see cref="Translate"/>.</summary>
        string LanguageCode { get; }
        /// <summary>The page being built: "overview" for the check-up, "mod" for a mod's page, or a topic id.</summary>
        string Topic { get; }
        /// <summary>What followed the topic (a mod id, an item code...). Never null.</summary>
        string Argument { get; }
        /// <summary>True when the findings go into a bug report: say facts only.</summary>
        bool Compact { get; }
        /// <summary>The item the asker holds, if any.</summary>
        ItemStack HeldItem { get; }

        /// <summary>A test that passed.</summary>
        void Ok(string text);
        /// <summary>Something worth knowing.</summary>
        void Info(string text);
        /// <summary>Something that may affect play.</summary>
        void Warn(string text);
        /// <summary>Something that isn't running properly or could crash.</summary>
        void Problem(string text);
        /// <summary>A finding at a level decided at run time.</summary>
        void Result(DoctorLevel level, string text);

        /// <summary>A line under the last finding (hidden until its card is opened).</summary>
        void Detail(string text);
        /// <summary>Background under the last finding; left out of bug reports.</summary>
        void Explain(string text);
        /// <summary>"→ do this" under the last finding; left out of bug reports.</summary>
        void Advice(string text);
        /// <summary>A button under the last finding that runs a registered fix, then shows this page again.</summary>
        void FixButton(string fixId, string argument, string label);
        /// <summary>A button under the last finding that opens another page of the doctor ("mod", "log", a topic id...).</summary>
        void LinkButton(string topic, string argument, string label);
        /// <summary>A button that opens a web page in the player's browser (http and https only).</summary>
        void UrlButton(string url, string label);
        /// <summary>A button that copies text to the player's clipboard.</summary>
        void CopyButton(string text, string label);
        /// <summary>A heading; findings after it are grouped under it.</summary>
        void Section(string heading);

        /// <summary>Text that shows a block or item as its icon and name (which opens its page) when put into any finding.</summary>
        string ItemRef(CollectibleObject collectible);

        /// <summary>A translated text from any mod's lang file, in the asker's language: Translate("mymod:doctor-pipes", 3).</summary>
        string Translate(string key, params object[] args);

        /// <summary>
        /// This session's errors and warnings from the game's log on this side, already grouped (the same message
        /// repeated is one entry with a count). Pass a mod id for only the ones the doctor traced to that mod.
        /// </summary>
        IReadOnlyList<DoctorLogIssue> LogIssues(string modId = null);
    }

    /// <summary>One kind of error or warning from the log.</summary>
    public sealed class DoctorLogIssue
    {
        /// <summary>"Error", "Warning" or "Fatal".</summary>
        public string Level;
        /// <summary>The first such line, without its time stamp.</summary>
        public string Message;
        /// <summary>The stack trace under it, if there was one (first lines).</summary>
        public IReadOnlyList<string> Stack;
        /// <summary>How often it appeared.</summary>
        public int Count;
        /// <summary>The mod the doctor traced it to, or null.</summary>
        public string ModId;
        /// <summary>When it first and last appeared, as written in the log.</summary>
        public string FirstTime, LastTime;
    }

    /// <summary>What a fix gets.</summary>
    public interface IDoctorFixContext
    {
        ICoreAPI Api { get; }
        EnumAppSide Side { get; }
        /// <summary>Who pressed the button. Null for the console.</summary>
        IPlayer Player { get; }
        string LanguageCode { get; }
        /// <summary>The argument the check gave the button. Never null.</summary>
        string Argument { get; }
        /// <summary>Marks an outcome as a failure: return Failed("couldn't because...").</summary>
        string Failed(string why);
        /// <summary>A translated text in the asker's language.</summary>
        string Translate(string key, params object[] args);
    }

    /// <summary>Finds Vintage Doctor.</summary>
    public static class DoctorApi
    {
        public const string ModId = "vintagedoctor";

        /// <summary>
        /// Vintage Doctor's API on this side, or null when the mod isn't installed. Only call this from a class that
        /// is not loaded when the mod is missing (see docs/api.md, "Without making it a requirement").
        /// </summary>
        public static IDoctorApi Get(ICoreAPI api)
        {
            if (api == null || !api.ModLoader.IsModEnabled(ModId)) return null;
            return api.ModLoader.GetModSystem<VintageDoctorSystem>() as IDoctorApi;
        }
    }
}
