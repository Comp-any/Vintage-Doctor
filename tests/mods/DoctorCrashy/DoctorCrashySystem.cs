using System;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

// A mod that is broken on purpose, for the doctor's "sick server" test: it writes the same error three times and
// then fails to start the way a mod built for another game version does.
namespace DoctorCrashy
{
    public class DoctorCrashySystem : ModSystem
    {
        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            for (int i = 0; i < 3; i++) api.Logger.Error("[doctorcrashy] a made-up error from the broken test mod");
            // Two texts of the doctorsick mod with formatting tags out of place, read by the game's own parser the
            // way the chat, a tooltip or the handbook would: the game writes its errors about them to the log. The
            // same texts are in doctorsick's lang file, which is how the doctor is to find whose they are.
            Vintagestory.API.Common.VtmlParser.Tokenize(api.Logger, "Pull the lever -> the gate opens");   // font-ok: the broken text the test is about
            Vintagestory.API.Common.VtmlParser.Tokenize(api.Logger, "<strong>Hot!</font> Don't touch");
            throw new MissingMethodException("Method not found: 'Void Vintagestory.API.Common.DoesNotExist.Gone()'.");
        }
    }
}
