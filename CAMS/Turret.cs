using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;

namespace IngameScript
{
    // fuck it
    public partial class Program : MyGridProgram
    {
        public abstract class TurretBase
        {
            public string Name; // yeah
            protected IMyMotorStator _azimuth, _elevation;
            public IMyTurretControlBlock _ctc;

            protected Program _m;
            protected TurretBase(IMyMotorStator a, Program m)
            {
                _azimuth = a;
                _m = m;
            }


        }
    }
}