using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace IngameScript
{
    public class RoundRobin<K, V>
    {
        public readonly K[] IDs;
        int _start, _current;

        public RoundRobin(K[] ks, int s = 0)
        {
            _start = s;
            IDs = ks;
            Reset();
        }

        public RoundRobin(ref Dictionary<K, V> dict, int s = 0)
        {
            _start = s;
            IDs = dict.Keys.ToArray();
            Reset();
        }

        public V Next(ref Dictionary<K, V> dict)
        {
            if (_current < IDs.Length)
                _current++;
            if (_current == IDs.Length)
                _current = _start;
            return dict[IDs[_current]];
        }

        // checks whether end of the key collection has been reached+
        public bool Next(ref Dictionary<K, V> dict, out V val)
        {
            if (_current < IDs.Length)
                _current++;
            if (_current == IDs.Length)
                _current = _start;
            val = dict[IDs[_current]];
            return _current < IDs.Length - (_start + 1);
        }

        public void Reset() => _start = _current = 0;

    }

    public partial class Program
    {
        string[] MastNames, MastAryTags, TurretNames, PDTNames;
        public IMyGridTerminalSystem Terminal => GridTerminalSystem;
        public IMyShipController Controller;
        public DebugAPI Debug;
        public static long ID;
        public Vector3D Center => Controller.WorldMatrix.Translation;
        public Vector3D Velocity => Controller.GetShipVelocities().LinearVelocity;
        public Vector3D Gravity;
        public TargetProvider Targets;
        public Dictionary<string, Screen>
            CtrlScreens = new Dictionary<string, Screen>(),
            LCDScreens = new Dictionary<string, Screen>();
        public Dictionary<string, Display> Displays = new Dictionary<string, Display>();

        public Dictionary<string, Action<MyCommandLine>> Commands = new Dictionary<string, Action<MyCommandLine>>();
        public Random RNG = new Random();
        MyCommandLine _cmd = new MyCommandLine();
        RoundRobin<string, Display> DisplayRR;
        double _lastRT, _totalRT = 0, _worstRT, _avgRT;

        public bool GlobalPriorityUpdateSwitch = true;

        int 
            _turCheckPtr = 0;
        long 
            _frame = 0, 
            _worstF;
        const int _rtMax = 10;
        Queue<double> _runtimes = new Queue<double>(_rtMax);
        public double RuntimeMS => _totalRT;
        public long F => _frame;

        #region scanner
        Dictionary<string, LidarMast> Masts = new Dictionary<string, LidarMast>();
        List<IMyLargeTurretBase> 
            AllTurrets = new List<IMyLargeTurretBase>(), 
            Artillery = new List<IMyLargeTurretBase>();
        IMyBroadcastListener _FLT, _TGT;

        #endregion
        
        #region defense
        int TurretCount => TurretNames.Length;
        ArmLauncherWHAM[] AMSLaunchers;
        
        Dictionary<long, long> TargetsKillDict = new Dictionary<long, long>();
        Dictionary<string, RotorTurret> Turrets = new Dictionary<string, RotorTurret>();
        RoundRobin<string, RotorTurret>
            AssignRR, UpdateRR;
        #endregion

        public double GaussRNG() => (2 * RNG.NextDouble() - 1 + 2 * RNG.NextDouble() - 1 + 2 * RNG.NextDouble() - 1) / 3;

        public bool PassTarget(MyDetectedEntityInfo info, bool m = false)
        {
            ScanResult fake;
            return PassTarget(info, out fake, m);
        }

        public bool PassTarget(MyDetectedEntityInfo info, out ScanResult r, bool m = false)
        {
            r = ScanResult.Failed;
            if (info.IsEmpty())
                return false;
            if (Targets.Blacklist.Contains(info.EntityId))
                return false;
            int rel = (int)info.Relationship, t = (int)info.Type;
            if (rel == 1 || rel == 5) // owner or friends
                return false;
            if (t != 2 && t != 3) // small grid and large grid respectively
                return false;
            if (info.BoundingBox.Size.Length() < 0.5)
                return false;
            r = Targets.AddOrUpdate(ref info, ID);
            if (!m)
                Targets.ScannedIDs.Add(info.EntityId);
            return true;
        }

        static Vector3D 
            lastVel = Vector3D.Zero,
            lastAccel = Vector3D.Zero;
        static long lastVelT = 0;

        public Vector3D Acceleration
        {
            get 
            {
                if (F - lastVelT > 0)
                {
                    lastAccel = 0.5 * (lastAccel + (Velocity - lastVel) / (F - lastVelT));
                    lastVelT = F;
                    lastVel = Velocity;
                }
                return lastAccel;
            }
        }
    }
}