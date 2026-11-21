using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using VintageDoctor.API;

// An example of a mod handing Vintage Doctor its own checks, in the ways that need code. It pretends to be a mod
// with pumps and tanks. Vintage Doctor's test run loads it next to the doctor, so everything here is known to work.
namespace DoctorExample
{
    public class DoctorExampleSystem : ModSystem
    {
        public static int JammedPumps = 3;
        ICoreServerAPI sapi;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            // Something for the doctor's known-problem lists to recognise in the log.
            api.Logger.Warning("[doctorexample] pump 3 jammed (a made-up warning from the example mod)");

            // Way 1: the doctor's own API. Vintage Doctor is not a requirement of this mod, so its types are only
            // touched in a class of their own, and only when the doctor is installed.
            if (api.ModLoader.IsModEnabled("vintagedoctor")) WithDoctor.Register(api);

            // Way 2: the event bus. No reference to the doctor at all; if it isn't installed, nobody asks.
            api.Event.RegisterEventBusListener(OnDoctorCollects, 0.5, "vintagedoctor:collect");
            api.Event.RegisterEventBusListener(OnDoctorFixes, 0.5, "vintagedoctor:fix");

            // Way 3, also on the event bus: a whole page of your own, written line by line. This is how a mod that
            // already has a doctor of its own hands it over (Vintage Doctor 1.2.0 and later; older ones never ask).
            api.Event.RegisterEventBusListener(OnDoctorAsksForPages, 0.5, "vintagedoctor:topics");
            api.Event.RegisterEventBusListener(OnDoctorWantsPage, 0.5, "vintagedoctor:page");
            api.Event.RegisterEventBusListener(OnDoctorAsksAbout, 0.5, "vintagedoctor:about");
        }

        void OnDoctorAsksForPages(string eventName, ref EnumHandling handling, IAttribute data)
        {
            if (!(data is ITreeAttribute tree) || tree.GetString("side") != "server") return;
            ITreeAttribute page = tree.GetOrAddTreeAttribute("topics").GetOrAddTreeAttribute("example-tanks");   // opened with /doctor example-tanks
            page.SetString("mod", "doctorexample");
            page.SetString("question", "My example tanks are empty");
            page.SetBool("ownHeadings", true);   // the page starts with a heading of its own
        }

        void OnDoctorWantsPage(string eventName, ref EnumHandling handling, IAttribute data)
        {
            if (!(data is ITreeAttribute tree) || tree.GetString("topic") != "example-tanks") return;
            if (tree.GetString("argument") != "slow") { WritePage(tree, "This page came at once."); return; }

            // A page that takes a while (a real mod would work it out on another thread): say "later" now, and
            // deliver it from the game's main thread when it is ready. The doctor waits, and tells the player.
            string request = tree.GetString("request");
            tree.SetBool("later", true);
            tree.SetInt("seconds", 20);   // how long the doctor should wait before giving up (5 to 300)
            sapi.Event.RegisterCallback(_ =>
            {
                var answer = new TreeAttribute();
                answer.SetString("request", request);
                WritePage(answer, "This page came a moment later.");
                sapi.Event.PushEvent("vintagedoctor:deliver", answer);
            }, 500);
        }

        /// <summary>A page is lines of text, each with a style and an indent.</summary>
        static void WritePage(ITreeAttribute tree, string last)
        {
            // Styles: 0 text, 1 heading, 2 a result line, 3 a quiet note, 4 a warning line, 5 a card. A card starts
            // with ✓ (passed), • (to know), ⚠ (may affect play) or ✖ (a problem); indented lines after it are its details.
            // Buttons are written into a line: {{fix|<fix id>|<argument>|<label>}} and {{open|<page>|<argument>|<label>}}.
            var lines = new (string text, byte style, byte indent)[]
            {
                ("The example tanks", 1, 0),
                ("⚠ 2 example tanks are half empty", 5, 0),
                ("tank at 10 3 10", 3, 1),
                ("→ Fill them from a pump.", 0, 1),
                ("{{fix|doctorexample:fill||Fill the example tanks}} {{fix|doctorexample:slowfill||Fill them slowly}} {{open|example-tanks|slow|The same page, slowly}}", 0, 1),
                (last, 3, 0)
            };
            tree["texts"] = new StringArrayAttribute(lines.Select(l => l.text).ToArray());
            tree.SetBytes("styles", lines.Select(l => l.style).ToArray());
            tree.SetBytes("indents", lines.Select(l => l.indent).ToArray());
        }

        /// <summary>On the doctor's pages about one item or block: a card of your own about that thing.</summary>
        void OnDoctorAsksAbout(string eventName, ref EnumHandling handling, IAttribute data)
        {
            if (!(data is ITreeAttribute tree) || tree.GetString("subject") != "item:game:ingot-copper") return;
            ITreeAttribute result = tree.GetOrAddTreeAttribute("results").GetOrAddTreeAttribute("doctorexample:made-of");
            result.SetString("level", "info");
            result.SetString("title", "Example pumps are made of this");
            result.SetString("link", "example-tanks");   // a page to open, with "linkArgument" and "linkLabel"
            result.SetString("linkLabel", "The example tanks");
        }

        void OnDoctorCollects(string eventName, ref EnumHandling handling, IAttribute data)
        {
            if (!(data is ITreeAttribute tree) || tree.GetString("side") != "server") return;
            // Asked for one mod's page only? Then only answer for ours.
            string onlyMod = tree.GetString("mod");
            if (onlyMod != null && onlyMod != "doctorexample") return;
            ITreeAttribute result = tree.GetOrAddTreeAttribute("results").GetOrAddTreeAttribute("doctorexample:tanks");
            result.SetString("level", "info");   // ok, info, warning or problem
            result.SetString("title", "2 example tanks are half empty");
            result.SetString("details", "tank at 10 3 10\ntank at 12 3 10");
            result.SetString("advice", "Fill them from a pump.");
            result.SetString("fix", "doctorexample:fill");
            result.SetString("fixLabel", "Fill the example tanks");
        }

        void OnDoctorFixes(string eventName, ref EnumHandling handling, IAttribute data)
        {
            if (!(data is ITreeAttribute tree)) return;
            if (tree.GetString("id") == "doctorexample:fill") tree.SetString("result", "Filled 2 example tanks");   // set "failed" to true as well when it didn't work
            if (tree.GetString("id") == "doctorexample:slowfill")
            {
                // A fix that takes a while works like a page that does: "later" now, the result when it is done.
                string request = tree.GetString("request");
                tree.SetBool("later", true);
                sapi.Event.RegisterCallback(_ =>
                {
                    var answer = new TreeAttribute();
                    answer.SetString("request", request);
                    answer.SetString("result", "Filled 2 example tanks, slowly");
                    sapi.Event.PushEvent("vintagedoctor:deliver", answer);
                }, 500);
            }
        }
    }

    /// <summary>Everything that names Vintage Doctor's types. Never loaded when the doctor isn't installed.</summary>
    static class WithDoctor
    {
        public static void Register(ICoreAPI api)
        {
            IDoctorApi doctor = DoctorApi.Get(api);
            if (doctor == null) return;

            doctor.RegisterCheck(new DoctorCheck("doctorexample", "doctorexample:pumps", ctx =>
            {
                if (DoctorExampleSystem.JammedPumps == 0) { ctx.Ok("All example pumps are running"); return; }
                ctx.Warn(DoctorExampleSystem.JammedPumps + " example pumps are jammed");
                ctx.Detail("pump at 10 5 10");
                ctx.Explain("A pump jams when its outlet is blocked.");
                ctx.Advice("Clear the outlets, or let the doctor restart the pumps.");
                ctx.FixButton("doctorexample:unjam", "all", "Restart the example pumps");
                // What the doctor read from the log is there to use too.
                int mine = ctx.LogIssues("doctorexample").Count;
                if (mine > 0) ctx.Detail(mine + " message(s) from this mod in the log");
            }));

            doctor.RegisterFix(new DoctorFix("doctorexample", "doctorexample:unjam", ctx =>
            {
                if (ctx.Argument != "all") return ctx.Failed("Which pump?");
                int n = DoctorExampleSystem.JammedPumps;
                DoctorExampleSystem.JammedPumps = 0;
                return "Restarted " + n + " example pumps";
            }));

            doctor.RegisterTopic(new DoctorTopic("doctorexample", "example-pumps", "My example pumps aren't pumping", ctx =>
            {
                ctx.Info("The example mod has " + DoctorExampleSystem.JammedPumps + " jammed pumps");
                ctx.LinkButton("mod", "doctorexample", "Everything about the example mod");
            }));

            doctor.RegisterKnownProblem(new KnownProblem
            {
                Id = "doctorexample:overflow", ModId = "doctorexample", Match = "example tank overflow", Level = DoctorLevel.Warning,
                Title = "An example tank overflowed", Explain = "More went in than it holds.", Advice = "Build a bigger tank."
            });

            // Something that goes wrong while playing can be reported as it happens.
            doctor.ReportIncident("doctorexample", DoctorLevel.Info, "The example mod started", "reported with ReportIncident");

            // What Vintage Doctor 1.2.0 added is used from a class of its own too, so an older doctor isn't asked for it.
            if (doctor.ApiVersion >= 2) WithDoctor2.Register(doctor);
        }
    }

    /// <summary>What needs API version 2 (Vintage Doctor 1.2.0).</summary>
    static class WithDoctor2
    {
        public static void Register(IDoctorApi doctor)
        {
            // A check for the doctor's page about one item: it runs there instead of on the check-up.
            doctor.RegisterCheck(new DoctorCheck("doctorexample", "doctorexample:item", ctx =>
            {
                if (ctx.Subject?.Code?.Path != "ingot-copper") return;
                ctx.Info("The example mod uses " + ctx.ItemRef(ctx.Subject) + " for its pipes");
                ctx.LinkButton("example-pumps", "", "The example pumps");
            }) { Pages = new[] { "item" } });

            // A page with headings of its own and several buttons on one line.
            doctor.RegisterTopic(new DoctorTopic("doctorexample", "example-pipes", "My example pipes leak", ctx =>
            {
                ctx.Section("The example pipes");
                ctx.Ok("No example pipe leaks");
                ctx.Write(ctx.LinkText("example-pumps", "", "The pumps") + " " + ctx.LinkText("example-tanks", "", "The tanks") + " " + ctx.FixText("doctorexample:unjam", "all", "Restart the pumps"));
            }) { OwnHeadings = true });
        }
    }
}
