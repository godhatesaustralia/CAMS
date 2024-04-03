using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace IngameScript
{



    public class CompBase
    {
        public readonly string Name;
        public Dictionary<string, Action<MyCommandLine>> Commands = new Dictionary<string, Action<MyCommandLine>>();
        public IMyShipController Reference => Manager.Controller;
        protected CombatManager Manager;
        public WCAPI wcAPI => Manager.API;
        public double Time => Manager.Time;
        public Random rand => Manager.Random;

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
        public Dictionary<long, Target> Targets = new Dictionary<long, Target>();
        public Random Random = new Random();
        public WCAPI API;

        double RuntimeMS = 0;
        long Frame = 0;
        public double Time => RuntimeMS;


        public CombatManager(MyGridProgram p)
        {
            Program = p;
            Terminal = Program.GridTerminalSystem;
        }
        

    }
}