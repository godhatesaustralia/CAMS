using Sandbox.Game;
using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Noise.Combiners;
using VRageMath;

namespace IngameScript
{



    public abstract class CompBase
    {
        public readonly string Name;
        public virtual string Debug { get; protected set; }
        public IMyShipController Reference => Main.Controller;
        public Dictionary<string, Action<MyCommandLine>> Commands = new Dictionary<string, Action<MyCommandLine>>();
       // public Dictionary<string, RotorTurret> Turrets = new Dictionary<string, RotorTurret>();
        public Program Main;
        protected IMyProgrammableBlock Me => Main.Me;
        protected HashSet<string> Handoff = new HashSet<string>();
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

        const string
          IgcFleet = "[FLT-CA]",
          IgcParams = "IGC_MSL_PAR_MSG",
          IgcHoming = "IGC_MSL_HOM_MSG",
          IgcBeamRiding = "IGC_MSL_OPT_MSG",
          IgcIff = "IGC_IFF_PKT",
          IgcFire = "IGC_MSL_FIRE_MSG",
          Igcregister = "IGC_MSL_REG_MSG",
          IgcUnicast = "UNICAST";


        double _runtime = 0, _totalRT, _worstRT, _avgRT;
        long _frame = 0, _worstFR;
        const int _rtMax = 10;
        Queue<double> _runtimes = new Queue<double>(_rtMax); 
        public double RuntimeMS => _runtime;
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