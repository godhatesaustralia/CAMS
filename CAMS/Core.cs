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

                }
            else throw new Exception($"\n{r.Error} at line {r.LineNo} of {Me} custom data.");
        }

    }
}