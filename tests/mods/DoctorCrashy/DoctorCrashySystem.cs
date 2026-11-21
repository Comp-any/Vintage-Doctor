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
            throw new MissingMethodException("Method not found: 'Void Vintagestory.API.Common.DoesNotExist.Gone()'.");
        }
    }
}
