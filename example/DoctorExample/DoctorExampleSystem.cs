using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using VintageDoctor.API;

// An example of a mod handing Vintage Doctor its own checks, in the two ways that need code. It pretends to be a mod
// with pumps. Vintage Doctor's test run loads it next to the doctor, so everything here is known to work.
namespace DoctorExample
{
    public class DoctorExampleSystem : ModSystem
    {
        public static int JammedPumps = 3;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            // Something for the doctor's known-problem lists to recognise in the log.
            api.Logger.Warning("[doctorexample] pump 3 jammed (a made-up warning from the example mod)");

            // Way 1: the doctor's own API. Vintage Doctor is not a requirement of this mod, so its types are only
            // touched in a class of their own, and only when the doctor is installed.
            if (api.ModLoader.IsModEnabled("vintagedoctor")) WithDoctor.Register(api);

            // Way 2: the event bus. No reference to the doctor at all; if it isn't installed, nobody asks.
            api.Event.RegisterEventBusListener(OnDoctorCollects, 0.5, "vintagedoctor:collect");
            api.Event.RegisterEventBusListener(OnDoctorFixes, 0.5, "vintagedoctor:fix");
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
            if (!(data is ITreeAttribute tree) || tree.GetString("id") != "doctorexample:fill") return;
            tree.SetString("result", "Filled 2 example tanks");   // set "failed" to true as well when it didn't work
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
        }
    }
}
