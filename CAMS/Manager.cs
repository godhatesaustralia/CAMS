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
        public IMyShipController Reference => Manager.Controller;
        public Dictionary<string, Action<MyCommandLine>> Commands = new Dictionary<string, Action<MyCommandLine>>();
        public CombatManager Manager;
        public readonly UpdateFrequency Frequency;
        public CompBase(string n, UpdateFrequency u)
        {
            Name = n;
            Frequency = u;
        }
        public abstract void Setup(CombatManager m, ref iniWrap p);
        public abstract void Update(UpdateFrequency u);
    }

    public class CombatManager
    {
        public MyGridProgram Program;
        public IMyGridTerminalSystem Terminal;
        public IMyShipController Controller;
        IMyTextSurface Display, sysA, sysB;
        public DebugAPI Debug;
        public bool Based;
        string activeScr = Lib.tr;
        public Vector3D Center => Controller.WorldMatrix.Translation;
        public Dictionary<long, Target> Targets = new Dictionary<long, Target>();
        public Dictionary<string, Screen> Screens = new Dictionary<string, Screen>();
        public Dictionary<string, CompBase> Components = new Dictionary<string, CompBase>();
        public Random Random = new Random();
        public WCAPI API;
        private MyCommandLine cmd = new MyCommandLine();

        double RuntimeMS = 0, totalRt, WorstRun, AverageRun;
        long Frame = 0, WorstFrame, Ticks = 0;
        public double Runtime => RuntimeMS;
        public long RuntimeTicks => Ticks;



        public void Start()
        {
            Targets.Clear();
            var p = new iniWrap();
            var r = new MyIniParseResult();
            if (p.CustomData(Program.Me, out r))
            {
                Based = p.Bool(Lib.hdr, "vcr");
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
                        Display = c.GetSurface(0);
                        Display.ContentType = VRage.Game.GUI.TextPanel.ContentType.SCRIPT;
                    }
                    Program.GridTerminalSystem.GetBlocksOfType<IMyShipController>(null, (b) =>
                    {
                        var s = b as IMyTextSurfaceProvider;
                        if (s == null) return false;
                        if (b.CustomName.Contains(Lib.sA)) sysA = s.GetSurface(0);
                        else if (b.CustomName.Contains(Lib.sB)) sysB = s.GetSurface(0);
                        return true;
                    });
                }
                foreach (var c in Components)
                    c.Value.Setup(this, ref p);
                Screens[activeScr].Active = true;
            }
            else throw new Exception($"\n{r.Error} at line {r.LineNo} of {Program.Me} custom data.");

        }

        private void UpdateTimes()
        {
            Frame++;
            var r = Program.Runtime.TimeSinceLastRun;
            RuntimeMS += r.TotalMilliseconds;
            Ticks += r.Ticks;
        }

        public CombatManager(MyGridProgram p)
        {
            Program = p;
            Terminal = Program.GridTerminalSystem;
        }

        public void Update(string arg, UpdateType src)
        {
            UpdateTimes();
            cmd.Clear();
            if (arg != "" && cmd.TryParse(arg))
            {
                if (Components.ContainsKey(cmd.Argument(0)) && Components[cmd.Argument(0)].Commands.ContainsKey(cmd.Argument(1)))
                    Components[cmd.Argument(0)].Commands[cmd.Argument(1)].Invoke(cmd);
                else if (cmd.Argument(0) == "screen" && Screens.ContainsKey(cmd.Argument(1)))
                {
                    activeScr = cmd.Argument(1);
                    Screens[activeScr].Active = true;
                }
                else
                {
                    var scr = Screens[activeScr];
                    if (cmd.Argument(0) == "up")
                        scr.Up();
                    else if (cmd.Argument(0) == "down")
                        scr.Down();
                    else if (cmd.Argument(0) == "select")
                        scr.Select.Invoke(scr);
                    else if (cmd.Argument(0) == "back")
                        scr.Back.Invoke(scr);
                }
            }
            var u = Lib.UpdateConverter(src);
            var rt = Program.Runtime.LastRunTimeMs;
            if (WorstRun < rt) 
            {
                WorstRun = rt; 
                WorstFrame = Frame; 
            }
            totalRt += rt;
            Program.Runtime.UpdateFrequency |= u;
            if ((u & UpdateFrequency.Update10) != 0)
            {
                AverageRun = totalRt / Frame;  
                Debug.RemoveDraw();
            }
            UpdateFrequency tgtFreq = UpdateFrequency.Update1;
            foreach (var comp in Components.Values)
            {
                comp.Update(tgtFreq);
                tgtFreq |= comp.Frequency;
            }
            Screens[activeScr].Draw(Display, u);
            Screens[Lib.sA].Draw(sysA, u);
            Screens[Lib.sB].Draw(sysB, u);
            Program.Runtime.UpdateFrequency = tgtFreq;
            string r = "[[COMBAT MANAGER]]\n\n";
            //foreach (var tgt in Targets.Values)
            //    r += $"{tgt.eIDString}\nDIST {tgt.Distance}, ELAPSED {tgt.Elapsed(Runtime)}\n";
            Debug.PrintHUD(((ScanComp)Components[Lib.sn]).Debug);
            r += $"RUNS - {Frame}\nRUNTIME - {rt} ms\nAVG - {AverageRun.ToString("0.####")} ms\nWORST - {WorstRun} ms, F{WorstFrame}\n";
            //r = Inventory.DebugString;
            Program.Echo(r);
        }
    }
}