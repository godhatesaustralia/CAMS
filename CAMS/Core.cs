using Sandbox.Game;
using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
            var rel = info.Relationship;
            if (rel == MyRelationsBetweenPlayerAndBlock.Owner || rel == MyRelationsBetweenPlayerAndBlock.Friends)
                return false;
            if (info.Type != MyDetectedEntityType.SmallGrid && info.Type != MyDetectedEntityType.LargeGrid)
                return false;
            if (info.BoundingBox.Size.Length() < 1.5)
                return false;
            r = Targets.AddOrUpdate(ref info, ID);
            if (!m)
                Targets.ScannedIDs.Add(info.EntityId);
            return true;
        }

        public TargetProvider Targets => Main.Targets; // IEnumerator sneed
        public readonly UpdateFrequency Frequency;
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
        IMyTextSurface _main, _sysA, _sysB;
        public DebugAPI Debug;
        public bool Based;
        bool _sysDisplays;
        string _activeScr = Lib.TR;
        public Vector3D Center => Controller.WorldMatrix.Translation;
        public Vector3D Gravity;
        public TargetProvider Targets;
        public Dictionary<string, Screen> Screens = new Dictionary<string, Screen>();
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
            _activeScr = "masts";
            Targets.Clear();
            var r = new MyIniParseResult();
            using (var p = new iniWrap())
                if (p.CustomData(Me, out r))
                {
                    Based = p.Bool(Lib.HDR, "vcr");
                    _sysDisplays = p.Bool(Lib.HDR, "systems", true);
                    GridTerminalSystem.GetBlocksOfType<IMyShipController>(null, (b) =>
                    {
                        if (b.CubeGrid.EntityId == Me.CubeGrid.EntityId && b.CustomName.Contains("Helm"))
                            Controller = b;
                        return true;
                    });
                    if (Controller != null)
                    {
                        var c = Controller as IMyTextSurfaceProvider;
                        if (c != null)
                        {
                            _main = c.GetSurface(0);
                            _main.ContentType = VRage.Game.GUI.TextPanel.ContentType.SCRIPT;
                        }
                        GridTerminalSystem.GetBlocksOfType<IMyShipController>(null, (b) =>
                        {
                            var s = b as IMyTextSurfaceProvider;
                            if (s == null) return false;
                            if (b.CustomName.Contains(Lib.SYA)) _sysA = s.GetSurface(0);
                            else if (b.CustomName.Contains(Lib.SYB)) _sysB = s.GetSurface(0);
                            return true;
                        });
                    }
                    foreach (var c in Components)
                        c.Value.Setup(this);
                    Screens[_activeScr].Active = true;
                }
                else throw new Exception($"\n{r.Error} at line {r.LineNo} of {Me} custom data.");
        }

    }
}