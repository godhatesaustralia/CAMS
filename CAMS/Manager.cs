using Sandbox.Game;
using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace IngameScript
{



    public abstract class CompBase
    {
        public readonly string Name;
        public virtual string Debug { get; protected set; }
        public IMyShipController Reference => Manager.Controller;
        public Dictionary<string, Action<MyCommandLine>> Commands = new Dictionary<string, Action<MyCommandLine>>();
       // public Dictionary<string, RotorTurret> Turrets = new Dictionary<string, RotorTurret>();
        public CombatManager Manager;
        protected IMyProgrammableBlock Me => Manager.Program.Me;
        protected HashSet<string> Handoff = new HashSet<string>();
        public TargetProvider Targets => Manager.Targets; // IEnumerator sneed
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

        //public bool TransferControl(string n)
        //{
        //    if (Turrets[n].tEID != -1 || Handoff.Contains(n))
        //        return false;

        //    Handoff.Add(n);
        //    Turrets[n].Stop();
        //    return true;
        //}

        //public bool TakeControl(string n, string o)
        //{
        //    if (Handoff.Contains(n))
        //        Handoff.Remove(n);
        //    return Manager.Components[o].TransferControl(n);
        //}

        public abstract void Setup(CombatManager m);
        public abstract void Update(UpdateFrequency u);
    }

    public class CombatManager
    {
        public MyGridProgram Program;
        public IMyGridTerminalSystem Terminal;
        public IMyShipController Controller;
        IMyTextSurface _main, _sysA, _sysB;
        public DebugAPI Debug;
        public bool Based;
        bool _sysDisplays;
        string _activeScr = Lib.tr;
        public Vector3D Center => Controller.WorldMatrix.Translation;
        public Vector3D Gravity;
        public TargetProvider Targets;
        public Dictionary<string, Screen> Screens = new Dictionary<string, Screen>();
        public Dictionary<string, CompBase> Components = new Dictionary<string, CompBase>();
        public Random RNG = new Random();
        MyCommandLine _cmd = new MyCommandLine();

        double _runtime = 0, _totalRT, _worstRT, _avgRT;
        long _frame = 0, _worstFR;
        public double Runtime => _runtime;
        public long F => _frame;

        public void Start()
        {
            _activeScr = "masts";
            Targets.Clear();
            var r = new MyIniParseResult();
            using (var p = new iniWrap())
            if (p.CustomData(Program.Me, out r))
            {
                Based = p.Bool(Lib.hdr, "vcr");
                _sysDisplays = p.Bool(Lib.hdr, "systems", true);
                Program.GridTerminalSystem.GetBlocksOfType<IMyShipController>(null, (b) =>
                {
                    if (b.CubeGrid.EntityId == Program.Me.CubeGrid.EntityId && b.CustomName.Contains("Helm"))
                        Controller = b;
                    return true;
                });
                if (Controller != null)
                {
                    var c = Controller as IMyTextSurfaceProvider;
                    if (c!= null)
                    {
                        _main = c.GetSurface(0);
                        _main.ContentType = VRage.Game.GUI.TextPanel.ContentType.SCRIPT;
                    }
                    Program.GridTerminalSystem.GetBlocksOfType<IMyShipController>(null, (b) =>
                    {
                        var s = b as IMyTextSurfaceProvider;
                        if (s == null) return false;
                        if (b.CustomName.Contains(Lib.sA)) _sysA = s.GetSurface(0);
                        else if (b.CustomName.Contains(Lib.sB)) _sysB = s.GetSurface(0);
                        return true;
                    });
                }
                foreach (var c in Components)
                    c.Value.Setup(this);
                Screens[_activeScr].Active = true;
            }
            else throw new Exception($"\n{r.Error} at line {r.LineNo} of {Program.Me} custom data.");

        }

        void UpdateTimes()
        {
            _frame++;
            _runtime += Program.Runtime.TimeSinceLastRun.TotalMilliseconds;
        }

        public CombatManager(MyGridProgram p)
        {
            Program = p;
            Terminal = Program.GridTerminalSystem;
            Targets = new TargetProvider(this);
        }

        public void Update(string arg, UpdateType src)
        {
            UpdateTimes();
            _cmd.Clear();
            if (arg != "" && _cmd.TryParse(arg))
            {
                if (Components.ContainsKey(_cmd.Argument(0)) && Components[_cmd.Argument(0)].Commands.ContainsKey(_cmd.Argument(1)))
                    Components[_cmd.Argument(0)].Commands[_cmd.Argument(1)].Invoke(_cmd);
                else if (_cmd.Argument(0) == "screen" && Screens.ContainsKey(_cmd.Argument(1)))
                {
                    _activeScr = _cmd.Argument(1);
                    Screens[_activeScr].Active = true;
                }
                else
                {
                    var s = Screens[_activeScr];
                    if (_cmd.Argument(0) == "up")
                        s.Up();
                    else if (_cmd.Argument(0) == "down")
                        s.Down();
                    else if (_cmd.Argument(0) == "select")
                        s.Select.Invoke(s);
                    else if (_cmd.Argument(0) == "back")
                        s.Back.Invoke(s);
                }
            }
            var rt = Program.Runtime.LastRunTimeMs;
            if (_worstRT < rt) 
            {
                _worstRT = rt; 
                _worstFR = _frame; 
            }
            var u = Lib.UpdateConverter(src);
            if ((u & UpdateFrequency.Update10) != 0)
            {
                _avgRT = _totalRT / _frame;
                Debug.RemoveDraw();
                Gravity = Controller.GetNaturalGravity();
            }
            UpdateFrequency tgtFreq = UpdateFrequency.Update1;
            
            foreach (var comp in Components.Values)
            {
                comp.Update(tgtFreq);
                tgtFreq |= comp.Frequency;
            }
            Targets.Update(u);
            _totalRT += rt;
            Program.Runtime.UpdateFrequency |= u;

            Screens[_activeScr].Draw(_main, u);
            if (_sysDisplays)
            {
                Screens[Lib.sA].Draw(_sysA, u);
                Screens[Lib.sB].Draw(_sysB, u);
            }

            Program.Runtime.UpdateFrequency = tgtFreq;
            string r = "[[COMBAT MANAGER]]\n\n";
            foreach (var tgt in Targets.AllTargets())
                r += $"{tgt.eIDString}\nDIST {tgt.Distance:0000}, ELAPSED {tgt.Elapsed(Runtime):###0}\n";
            //foreach (var c in Components.Values)
            //    Debug.PrintHUD(c.Debug);
            //Debug.PrintHUD(Components[Lib.tr].Debug);
            r += $"RUNS - {_frame}\nRUNTIME - {rt} ms\nAVG - {_avgRT:0.####} ms\nWORST - {_worstRT} ms, F{_worstFR}\n";
            r += Components[Lib.sn].Debug;
            Program.Echo(r);
        }
    }
}