using Sandbox.Game;
using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        public CompBase(string n)
        {
            Name = n;
        }
        public abstract void Setup(CombatManager m, ref iniWrap p);
        public abstract void Update(UpdateFrequency u);
    }

    public class CombatManager
    {
        public MyGridProgram Program;
        public IMyGridTerminalSystem Terminal;
        public IMyShipController Controller;
        public DebugAPI Debug;
        public Vector3D Center => Controller.WorldMatrix.Translation;
        public Dictionary<long, Target> Targets = new Dictionary<long, Target>();
        List<long> removeIDs = new List<long>();
        public Dictionary<string, CompBase> Components = new Dictionary<string, CompBase>();
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
                Program.GridTerminalSystem.GetBlocksOfType<IMyShipController>(null, (b) =>
                {
                    if (b.CubeGrid.EntityId == Program.Me.CubeGrid.EntityId && b.CustomName.Contains("Helm"))
                        Controller = b;
                    return true;
                });
                //foreach (var c in Components.Values)
                //    c.Setup(this, ref p);
                Scanner.Setup(this, ref p);
                Turrets.Setup(this, ref p);
                
            }
            else throw new Exception($"\n{r.Error} at line {r.LineNo} of {Program.Me} custom data.");

        }

        private void UpdateTimes()
        {
            Frame++;
            var r = Program.Runtime.TimeSinceLastRun;
            RuntimeMS += r.TotalSeconds;
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
                Debug.RemoveDraw();
            }
            
            Scanner.Update(u);
            Turrets.Update(u);
            foreach (var tgt in Targets)
                if (tgt.Value.Timestamp - RuntimeMS > Lib.maxTimeTGT)
                    removeIDs.Add(tgt.Key);
            string r = "[[COMBAT MANAGER]]\n\n";
            foreach (var tgt in Targets.Values)
                r += $"{tgt.EID.ToString("X").Remove(0, 6)}\nDIST {tgt.Distance}, VEL {tgt.Velocity}\n";
            Debug.PrintHUD(Scanner.Debug);
            r += $"RUNS - {Frame}\nRUNTIME - {rt} ms\nAVG - {AverageRun.ToString("0.####")} ms\nWORST - {WorstRun} ms, F{WorstFrame}\n";
            //r = Inventory.DebugString;
            Program.Echo(r);
        }
    }
}