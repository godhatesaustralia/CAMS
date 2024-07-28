using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    public class Scanner : CompBase
    {
        string[] masts, tags; //= { "[A]", "[B]", "[C]", "[D]" };

        public List<LidarArray> Lidars = new List<LidarArray>();
        public Dictionary<string, LidarMast> Masts = new Dictionary<string, LidarMast>();
        bool _fixedRange, _useNetwork, _useBackup = false;
        public List<IMyLargeTurretBase> AllTurrets, Artillery;
        int tPtr, tStep, mastCheckInterval;
        const string
            IgcFleet = "[FLT-CA]",
            IgcTgt = "[FLT-TG]";
        IMyBroadcastListener _FLT, _TGT;

        public Scanner(string n) : base(n, Lib.u1 | Lib.u10 | Lib.u100)
        {
            AllTurrets = new List<IMyLargeTurretBase>();
            Artillery = new List<IMyLargeTurretBase>();
        }

        public void SetDebug(string s) => Debug += s;

        void Clear()
        {
            AllTurrets.Clear();
            Artillery.Clear();
            Lidars.Clear();
            Masts.Clear();
        }
        // [CAMS]
        // tStep = 5
        // maxRaycast = 4000
        // lidarTags = 
        // |[A]
        // |[B]
        // |[C]
        // |[D]
        string mastUpdate(ref Screen s, int ptr)
        {
            string grps = ""; int i = 0;
            var l = Masts[masts[ptr]];
            for (; i < l.Lidars.Count; i++)
            {
                var scan = l.Lidars[i].scanAVG != 0 ? $"{l.Lidars[i].scanAVG:G1}M\n" : "READY\n";
                grps += $"SCAN {l.Lidars[i].tag[1]} " + scan;
            }
            grps += $"TARGETS {Main.Targets.Count:00} CTRL " + (!l.Manual ? "OFF" : "MAN");
            s.SetData(grps, 1);
            for (i = 0; i < l.Lidars.Count; ++i)
                s.SetColor(l.Lidars[i].Scans > 0 ? Lib.GRN : Lib.DRG, i + 2);
            return l.Name;
        }
        public override void Setup(Program m)
        {
            Clear();
            Main = m;
            _FLT = m.IGC.RegisterBroadcastListener(IgcFleet);
            _TGT = m.IGC.RegisterBroadcastListener(IgcTgt);
            var lct = "LargeCalibreTurret";
            using (var p = new iniWrap())
                if (p.CustomData(Main.Me))
                {
                    var h = Lib.HDR;
                    tStep = p.Int(h, "tStep", 4);
                    var tagstr = p.String(h, "lidarTags", "[A]\n[B]\n[C]\n[D]");
                    var a = tagstr.Split('\n');
                    _fixedRange = p.Bool(h, "fixedAntennaRange", true);
                    _useNetwork = p.Bool(h, "network", false);
                    for (int i = 0; i < a.Length; i++)
                        a[i] = a[i].Trim('|');
                    tags = a;
                    m.Terminal.GetBlocksOfType(AllTurrets, b => b.BlockDefinition.SubtypeName != lct && (b.CubeGrid.EntityId == ID) && !(b is IMyLargeInteriorTurret));
                    m.Terminal.GetBlocksOfType(Artillery, b => b.BlockDefinition.SubtypeName == lct && (b.CubeGrid.EntityId == ID));
                    m.Terminal.GetBlocksOfType<IMyMotorStator>(null, mtr =>
                    {
                        if (mtr.CustomName.Contains(Lib.ARY) && mtr.CubeGrid.EntityId == ID)
                        {
                            var tur = new LidarMast(m, mtr, tags);
                            tur.Setup(ref m);
                            if (!Masts.ContainsKey(tur.Name))
                                Masts.Add(tur.Name, tur);
                        }
                        return true;
                    });
                    masts = Masts.Keys.ToArray();
                }
            Vector2
                sqvpos = Lib.V2(356, 222), // standard rect pos
                sqvsz = Lib.V2(128, 28), // standard rect size
                sqoff = Lib.V2(0, 40); // standard rect offset
            var al = TextAlignment.LEFT;
            var sprites = new MySprite[]
            {
                new MySprite(Lib.TXT, "", Lib.V2(24, 112), null, Lib.GRN, Lib.VB, 0, 1.75f),// 1. TUR NAME
                new MySprite(Lib.TXT, "", Lib.V2(24, 200), null, Lib.GRN, Lib.VB, 0, 0.8195f),
                new MySprite(Lib.SHP, Lib.SQS, sqvpos, sqvsz, Lib.GRN, "", al),
                new MySprite(Lib.SHP, Lib.SQS, sqvpos + sqoff, sqvsz, Lib.GRN, "", al),
                new MySprite(Lib.SHP, Lib.SQS,  sqvpos + 2 * sqoff, sqvsz, Lib.GRN, "", al),
                new MySprite(Lib.SHP, Lib.SQS, sqvpos + 3 * sqoff, sqvsz, Lib.GRN, "", al),
            };

            m.CtrlScreens.Add("masts", new Screen(() => masts.Length, sprites, (p, s) => s.SetData($"{mastUpdate(ref s, p)} {p + 1}/{masts.Length}", 0), 128f));
            Commands.Add("clear", b => Main.Targets.Clear());
            Commands.Add("designate", b =>
            {
                if (Masts.ContainsKey(b.Argument(2)))
                    Masts[b.Argument(2)].Designate();
            });
            Commands.Add("manual", b =>
            {
                if (Masts.ContainsKey(b.Argument(2)))
                    Masts[b.Argument(2)].Retvrn();
            });
            Commands.Add("tureset", b =>
            {
                foreach (var t in AllTurrets)
                {
                    t.Azimuth = 0;
                    t.Elevation = 0;
                }
                foreach (var t in Artillery)
                {
                    t.Azimuth = 0;
                    t.Elevation = 0;
                }
            });
        }
        void CheckTurret(IMyLargeTurretBase t, bool arty = false)
        {
            MyDetectedEntityInfo info;
            if (t.HasTarget)
            {
                info = t.GetTargetedEntity();
                if (Main.Targets.Exists(info.EntityId))
                    return;
                else if (Main.PassTarget(info) && arty && (int)info.Type == 2) // if small, retarget
                {
                    t.ResetTargetingToDefault();
                    t.EnableIdleRotation = false;
                }
            }
        }
        public override void Update(UpdateFrequency u)
        {
            Debug = "";

            if (Main.F % 3 == 0) // guar
                foreach (var m in Masts.Values)
                    m.Update();

            int n = Math.Min(AllTurrets.Count, tPtr + tStep);
            for (; tPtr < n; tPtr++)
                CheckTurret(AllTurrets[tPtr]);

            for (int i = 0; i < Artillery.Count; i++)
                CheckTurret(Artillery[i], true);

            if (n == AllTurrets.Count)
            {
                tPtr = 0;
            }
        }
    }
}