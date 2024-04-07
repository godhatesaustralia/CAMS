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
        public Dictionary<string, Action<MyCommandLine>> Commands = new Dictionary<string, Action<MyCommandLine>>();
        public IMyShipController Reference => Manager.Controller;
        public CombatManager Manager;
        protected UpdateFrequency freq;
        public CompBase(string n, UpdateFrequency u)
        {
            Name = n;
            freq = u;
        }
        public abstract void Setup(CombatManager m, ref iniWrap p);
        public abstract void Update(UpdateFrequency u);
    }

    public class CombatManager
    {
        public MyGridProgram Program;
        public IMyGridTerminalSystem Terminal;
        public IMyShipController Controller;
        IMyTextSurface Display;
        public DebugAPI Debug;
        public bool Based;
        string activeScr = Lib.tr;
        public Vector3D Center => Controller.WorldMatrix.Translation;
        public Dictionary<long, Target> Targets = new Dictionary<long, Target>();
        List<long> removeIDs = new List<long>();
        public Dictionary<string, Screen> Screens = new Dictionary<string, Screen>();
        public Random Random = new Random();
        public WCAPI API;
        public ScanComp Scanner;
        public TurretComp Turrets;
        MyCommandLine cmd = new MyCommandLine();

        public void RegisterLidar(LidarArray l) => Scanner.Lidars.Add(l);

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
                }
                //foreach (var c in Components.Values)
                //    c.Setup(this, ref p);
                Scanner.Setup(this, ref p);
                Turrets.Setup(this, ref p);
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
            for (int i = 0; i < removeIDs.Count; i++)
                if (Targets.ContainsKey(removeIDs[i]))
                    Targets.Remove(removeIDs[i]);
            removeIDs.Clear();
            cmd.Clear();
            if (arg != "" && cmd.TryParse(arg))
            {
                switch (cmd.Argument(0))
                {
                    case "reset":
                        Scanner.ResetAllStupidTurrets();
                        break;    
                    case "designate":
                        Scanner.Designate(cmd.Argument(1));
                        break;
                    case "screen":
                        if (Screens.ContainsKey(cmd.Argument(1)))
                        {
                            activeScr = cmd.Argument(1);
                            Screens[activeScr].Active = true;
                        }    
                        break;
                    case "up":
                        Screens[activeScr].Up();
                        break;
                    case "down":
                        Screens[activeScr].Down();
                        break;
                    default: break;
     
                }
            }

            
            var u = Lib.UpdateConverter(src);
            var rt = Program.Runtime.LastRunTimeMs;
            if (WorstRun < rt) { WorstRun = rt; WorstFrame = Frame; }
            totalRt += rt;
            Program.Runtime.UpdateFrequency |= u;
            if ((u & UpdateFrequency.Update10) != 0)
            {
                AverageRun = totalRt / Frame; 
                Scanner.Update(u);
                Debug.RemoveDraw();
            }
            Turrets.Update(u);
            foreach (var id in Targets.Keys)
                if (Targets[id].Elapsed(Runtime) >= Lib.maxTimeTGT)
                    removeIDs.Add(id);
            Screens[activeScr].Draw(Display, u);
            string r = "[[COMBAT MANAGER]]\n\n";
            foreach (var tgt in Targets.Values)
                r += $"{tgt.EID.ToString("X").Remove(0, 6)}\nDIST {tgt.Distance}, ELAPSED {tgt.Elapsed(Runtime)}\n";
            Debug.PrintHUD(Scanner.Debug);
            r += $"RUNS - {Frame}\nRUNTIME - {rt} ms\nAVG - {AverageRun.ToString("0.####")} ms\nWORST - {WorstRun} ms, F{WorstFrame}\n";
            //r = Inventory.DebugString;
            Program.Echo(r);
        }
    }
}