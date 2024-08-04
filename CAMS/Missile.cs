using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;

namespace IngameScript
{
    public class Missile
    {
        IMyRemoteControl _ctrl;
        IMyShipConnector _ctor;
        IMyShipMergeBlock _merge;
        List<IMyCameraBlock> _sensors = new List<IMyCameraBlock>();
        List<IMyGasTank> _fuelTanks = new List<IMyGasTank>();
        List<IMyThrust> _thrust = new List<IMyThrust>();
        List<IMyGyro> _gyros = new List<IMyGyro>();
        List<IMyWarhead> _warhead = new List<IMyWarhead>();
        PDController _yaw, pitch, roll;

        
    }
}