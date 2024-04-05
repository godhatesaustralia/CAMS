using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace IngameScript
{



    public class CompBase
    {
        public readonly string Name;
        public Dictionary<string, Action<MyCommandLine>> Commands = new Dictionary<string, Action<MyCommandLine>>();
        public IMyShipController Reference => Manager.Controller;
        public CombatManager Manager;

        public CompBase(string n, CombatManager m)
        {
            Name = n;
            Manager = m;
        }
    }

    public class CombatManager
    {
        public MyGridProgram Program;
        public IMyGridTerminalSystem Terminal;
        public IMyShipController Controller;
        public Vector3D Center => Controller.WorldMatrix.Translation;
        public Dictionary<long, Target> Targets = new Dictionary<long, Target>();
        public Random Random = new Random();
        public WCAPI API;

        double RuntimeMS = 0;
        long Frame = 0, Ticks = 0;
        public double Runtime => RuntimeMS;
        public long RuntimeTicks => Ticks;

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
        }

    }
}