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
        public abstract void Setup(CombatManager m);
        public abstract void Update();
    }

    public class CombatManager
    {
        public MyGridProgram Program;
        public IMyGridTerminalSystem Terminal;
        public IMyShipController Controller;
        public Vector3D Center => Controller.WorldMatrix.Translation;
        public Dictionary<long, Target> Targets = new Dictionary<long, Target>();
        public Dictionary<string, CompBase> Components = new Dictionary<string, CompBase>();
        public Random Random = new Random();
        public WCAPI API;

        public void RegisterLidar(LidarArray l) => ((ScanComp)Components[Lib.sn]).Lidars.Add(l);

        double RuntimeMS = 0, totalRt, WorstRun, AverageRun;
        long Frame = 0, WorstFrame, Ticks = 0;
        public double Runtime => RuntimeMS;
        public long RuntimeTicks => Ticks;



        public void Start()
        {
            Targets.Clear();
            Program.GridTerminalSystem.GetBlocksOfType<IMyShipController>(null, (b) =>
            {
                if (b.CubeGrid.EntityId == Program.Me.CubeGrid.EntityId && b.Name.Contains("Helm"))
                    Controller = b;
                return true;
            });
            foreach (var c in Components.Values)
                c.Setup(this);
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
            if (arg != "")
                switch (arg)
                {
                    case "reset":
                        {
                            ((ScanComp)Components[Lib.sn]).ResetAllStupidTurrets();
                            break;
                    }
                    default: break;
                }
            UpdateTimes();
            var rt = Program.Runtime.LastRunTimeMs;
            if (WorstRun < rt) { WorstRun = rt; WorstFrame = Frame; }
            totalRt += rt;
            if (Frame % 10 == 0)
                AverageRun = totalRt / Frame;
            foreach (var c in Components.Values)
                c.Update();
            string r = "[[COMBAT MANAGER]]\n\n";
            r += $"\nRUNS - {Frame}\nRUNTIME - {rt} ms\nAVG - {AverageRun.ToString("0.####")} ms\nWORST - {WorstRun} ms, F{WorstFrame}\n";
            //r = Inventory.DebugString;
            Program.Echo(r);
        }
    }
}