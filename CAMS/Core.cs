using Sandbox.Game;
using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Noise.Combiners;
using VRageMath;

namespace IngameScript
{
    public class RoundRobin<K, V>
    {
        public readonly K[] IDs;
        int start, current;

        public RoundRobin(K[] ks, int s = 0)
        {
            start = s;
            IDs = ks;
            Reset();
        }

        public RoundRobin(ref Dictionary<K, V> dict, int s = 0)
        {
            start = s;
            IDs = dict.Keys.ToArray();
            Reset();
        }

        public V Next(ref Dictionary<K, V> dict)
        {
            if (current < IDs.Length)
                current++;
            if (current == IDs.Length)
                current = start;
            return dict[IDs[current]];
        }

        // checks whether end of the key collection has been reached+
        public bool Next(ref Dictionary<K, V> dict, out V val)
        {
            if (current < IDs.Length)
                current++;
            if (current == IDs.Length)
                current = start;
            val = dict[IDs[current]];
            return current < IDs.Length - (start + 1);
        }

        public void Reset() => start = current = 0;

    }

    public abstract class CompBase
    {
        public readonly string Name;
        public long ID => Main.Me.CubeGrid.EntityId;
        public virtual string Debug { get; protected set; }
        public IMyShipController Reference => Main.Controller;

        public Vector3D Velocity => Main.Controller.GetShipVelocities().LinearVelocity;
        public Dictionary<string, Action<MyCommandLine>> Commands = new Dictionary<string, Action<MyCommandLine>>();
        public Program Main;
        public double NextDbl() => 2 * Main.RNG.NextDouble() - 1; // from lamp
        public double GaussRNG() => (NextDbl() + NextDbl() + NextDbl()) / 3;
        public double Time => Main.RuntimeMS;
        public long F => Main.F;

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
            if (info.BoundingBox.Size.Length() < 1.5)
                return false;
            r = Targets.AddOrUpdate(ref info, ID);
            if (!m)
                Targets.ScannedIDs.Add(info.EntityId);
            return true;
        }

        public TargetProvider Targets => Main.Targets; // IEnumerator sneed
        public UpdateFrequency Frequency;
        public CompBase(string n, UpdateFrequency u)
        {
            Name = n;
            Frequency = u;
        }
        public T Cast<T>()
        where T : CompBase
        {
            return (T)this;
        }

        public abstract void Setup(Program prog);
        public abstract void Update(UpdateFrequency u);
    }

    public partial class Program
    {

        public IMyGridTerminalSystem Terminal => GridTerminalSystem;
        public IMyShipController Controller;
        public DebugAPI Debug;
        public bool Based;
        public double PDSpray = -1;
        string _tag;
        public Vector3D Center => Controller.WorldMatrix.Translation;
        public Vector3D Velocity => Controller.GetShipVelocities().LinearVelocity;
        public Vector3D Gravity;
        public TargetProvider Targets;
        public Dictionary<string, Screen>
            CtrlScreens = new Dictionary<string, Screen>(),
            LCDScreens = new Dictionary<string, Screen>();
        public Dictionary<string, Display> Displays = new Dictionary<string, Display>();

        public Dictionary<string, CompBase> Components = new Dictionary<string, CompBase>();
        public Random RNG = new Random();
        MyCommandLine _cmd = new MyCommandLine();
        RoundRobin<string, Display> DisplayRR;
        double _totalRT = 0, _worstRT, _avgRT;
        long _frame = 0, _worstF;
        const int _rtMax = 10;
        Queue<double> _runtimes = new Queue<double>(_rtMax);
        public double RuntimeMS => _totalRT;
        public long F => _frame;

        void Start()
        {
            Targets.Clear();
            var r = new MyIniParseResult();
            var dspGrp = new List<IMyTerminalBlock>();
            using (var p = new iniWrap())
                if (p.CustomData(Me, out r))
                {
                    Based = p.Bool(Lib.HDR, "vcr");
                    _tag = p.String(Lib.HDR, "tag", Lib.HDR);
                    PDSpray = p.Double(Lib.HDR, "spray", -1);
                    string
                        ctrl = p.String(Lib.HDR, "controller", "Helm");
                    GridTerminalSystem.GetBlocksOfType<IMyShipController>(null, (b) =>
                    {
                        if (b.CubeGrid == Me.CubeGrid && b.CustomName.Contains(ctrl))
                            Controller = b;
                        return true;
                    });

                    foreach (var c in Components)
                        c.Value.Setup(this);

                    GridTerminalSystem.GetBlockGroupWithName(_tag + ' ' + p.String(Lib.HDR, "displays", "MFD Users")).GetBlocks(dspGrp);
                    if (dspGrp.Count > 0)
                    {
                        foreach (var b in dspGrp)
                        {
                            Display d;
                            if (b is IMyTextPanel)
                            {
                                d = new Display(this, b, LCDScreens.First().Key, Based);
                                Displays.Add(d.Name, d);
                            }
                            else if (b is IMyTextSurfaceProvider)
                            {
                                d = new Display(this, b, CtrlScreens.First().Key, Based);
                                Displays.Add(d.Name, d);
                            }
                        }
                        DisplayRR = new RoundRobin<string, Display>(Displays.Keys.ToArray());
                    }

                }
            else throw new Exception($"\n{r.Error} at line {r.LineNo} of {Me} custom data.");
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