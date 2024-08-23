using System;
using System.Linq;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        public const string H = "CAMS", IgcFleet = "[FLT-CA]";
        public Color PMY, SDY, BKG;
        public const SpriteType TXT = SpriteType.TEXT, SHP = SpriteType.TEXTURE, CLP = SpriteType.CLIP_RECT;
        public bool Based;
        
        public double
            PDSpray,
            ScanDistLimit;

        public int
            SendIGCTicks,
            ReceiveIGCTicks,
            MaxAutoTgtChecks,
            MaxScansMasts,
            MaxScansPDLR,
            MaxTgtKillTracks,
            MaxRotorTurretUpdates,
            MastCheckTicks,
            PriorityCheckTicks;

        public string
            ShipTag,
            ControllerTag,
            DisplayGroup,
            TurPDLRGroup,
            TurMainGroup,
            RackEKVGroup,
            RackMainGroup;

        void ParseComputerSettings()
        {
            var r = new MyIniParseResult();
            using (var p = new iniWrap())
                if (p.CustomData(Me, out r))
                {
                    string
                        grp = "RotorGroup",
                        def = " CAMS Azimuths",
                        arys = p.String(H, "lidarTags", "[A]\n[B]\n[C]\n[D]");

                    MastAryTags = arys.Split('\n');
                    for (int i = 0; i < MastAryTags.Length; i++)
                        MastAryTags[i] = MastAryTags[i].Trim('|');

                    ShipTag = p.String(H, "tag", H);
                    ControllerTag = p.String(H, "controller", "Helm");
                    DisplayGroup = p.String(H, "displays", "MFD Users");
                    TurPDLRGroup = p.String(H, "pd" + grp, "PD" + def);
                    TurMainGroup = p.String(H, "main" + grp, "Main" + def);
                    RackEKVGroup = p.String(H, "ekv" + grp, "EKV" + def);
                    RackMainGroup = p.String(H, "msl" + grp, "MSL" + def);

                    Based = p.Bool(H, "vcr");
                    ReceiveIGCTicks = p.Int(H, "igcCheckInterval", 0);
                    SendIGCTicks = p.Int(H, "igcTransmitInterval", 0);
                    PDSpray = p.Double(H, "spray", -1);
                    ScanDistLimit = p.Double(H, "maxRaycast", 8E3);
                    MaxScansMasts = p.Int(H, "maxScansMast", 1);
                    MaxScansPDLR = p.Int(H, "maxScansPDLR", 3);
                    MaxAutoTgtChecks = p.Int(H, "tStep", 4);
                    MaxRotorTurretUpdates = p.Int(H, "maxTurUpdates", 2);
                    MaxTgtKillTracks = p.Int(H, "maxKillTracks", 3);
                    MastCheckTicks = p.Int(H, "mastUpdateTicks", 3);
                    PriorityCheckTicks = p.Int(H, "priorityUpdateTicks", 10);

                    BKG = p.Color(H, "backgroundColor", new Color(7, 16, 7));
                    PMY = p.Color(H, "primaryColor", new Color(100, 250, 100));
                    SDY = p.Color(H, "secondaryColor",new Color(50, 125, 50));

                    Targets.UpdateRadarSettings(this);
                }
                else throw new Exception($"\n{r.Error} at line {r.LineNo} of {Me} custom data.");
        }

        void CacheMainSystems()
        {
            #region controller and displays
            Terminal.GetBlocksOfType<IMyShipController>(null, (b) =>
            {
                if (b.CubeGrid == Me.CubeGrid && b.CustomName.Contains(ControllerTag))
                    Controller = b;
                return false;
            });
            var dspGrp = new List<IMyTerminalBlock>();
            Terminal.GetBlockGroupWithName(DisplayGroup).GetBlocks(dspGrp);
            if (dspGrp.Count > 0)
            {
                foreach (var b in dspGrp)
                {
                    Display d;
                    if (b is IMyTextPanel)
                    {
                        d = new Display(this, b, LCDScreens.First().Key, Based);
                        Displays.Add(d.Name, d);
                    }
                    else if (b is IMyTextSurfaceProvider)
                    {
                        d = new Display(this, b, Lib.LN, Based);
                        Displays.Add(d.Name, d);
                    }
                }
                DisplayRR = new RoundRobin<string, Display>(Displays.Keys.ToArray());
            }
            else throw new Exception($"\nNo displays found with tag \'{DisplayGroup}\'.");
            #endregion

            #region masts and sensors
            Terminal.GetBlocksOfType<IMyLargeTurretBase>(null, b =>
            {
                if ((b.CubeGrid.EntityId == ID) && !(b is IMyLargeInteriorTurret))
                {
                    if (b.BlockDefinition.SubtypeName == "LargeCalibreTurret")
                        Artillery.Add(b);
                    else AllTurrets.Add(b);
                }
                return false;
            });
            Terminal.GetBlocksOfType<IMyMotorStator>(null, mtr =>
            {
                if (mtr.CustomName.Contains(Lib.ARY) && mtr.CubeGrid.EntityId == ID)
                {
                    var tur = new LidarMast(this, mtr);
                    tur.Setup(this, ref MastAryTags);
                    if (!Masts.ContainsKey(tur.Name))
                        Masts.Add(tur.Name, tur);
                }
                return false;
            });
            MastNames = Masts.Keys.ToArray();
            #endregion

            #region turrets and racks
            var r = new List<IMyMotorStator>();
            var l = new List<ArmLauncherWHAM>();
            
            Terminal.GetBlockGroupWithName(TurPDLRGroup).GetBlocks(null, b =>
            {
                var a = b as IMyMotorStator;
                if (a != null)
                    r.Add(a);
                return false;
            });
            var pdn = new List<string>();
            foreach (var az in r)
            {
                var pd = new PDT(az, this, MaxScansPDLR);
                if (pd != null)
                {
                    Turrets.Add(pd.Name, pd);
                    pdn.Add(pd.Name);
                }
            }

            r.Clear();
            Terminal.GetBlockGroupWithName(TurMainGroup).GetBlocks(null, b =>
            {
                var a = b as IMyMotorStator;
                if (a != null)
                    r.Add(a);
                return false;
            });
            foreach (var az in r)
            {
                var tr = new RotorTurret(az, this);
                if (tr != null)
                    Turrets.Add(tr.Name, tr);
            }

            r.Clear();
            Terminal.GetBlockGroupWithName(RackEKVGroup).GetBlocks(null, b =>
            {
                var a = b as IMyMotorStator;
                if (a != null)
                    r.Add(a);
                return false;
            });
            foreach (var arm in r)
            {
                var rk = new ArmLauncherWHAM(arm, this);
                if (rk.Init())
                    l.Add(rk);
            }

            AMSLaunchers = l.ToArray();
            TurretNames = Turrets.Keys.ToArray();
            PDTNames = pdn.ToArray();

            AssignRR = new RoundRobin<string, RotorTurret>(TurretNames);
            UpdateRR = new RoundRobin<string, RotorTurret>(TurretNames);
            #endregion

        }

        void AddSystemCommands()
        {
            Commands.Add("switch", b =>
            {
                if (_cmd.ArgumentCount == 3 && Displays.ContainsKey(_cmd.Argument(2)))
                    Displays[_cmd.Argument(2)].SetActive(_cmd.Argument(1));
            });

            Commands.Add("designate", b =>
            {
                if (b.ArgumentCount == 2 && Masts.ContainsKey(b.Argument(1)))
                    Masts[b.Argument(1)].Designate();
            });

            Commands.Add("manual", b =>
            {
                if (b.ArgumentCount == 2 && Masts.ContainsKey(b.Argument(1)))
                    Masts[b.Argument(1)].Retvrn();
            });

            Commands.Add("turret_reset", b =>
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

            Commands.Add("system_update", b =>
            {
                if (b.ArgumentCount != 2)
                    return;
                else if (b.Argument(1) == "settings")
                    ParseComputerSettings();
                else if (b.Argument(1) == "components")
                    CacheMainSystems();
            });
        }

        static MySprite SPR(SpriteType t, string d, Vector2 pos, Vector2? sz = null, Color? c = null, string f = null, TextAlignment a = TextAlignment.CENTER, float rs = 0) => new MySprite(t, d, pos, sz, c, f, a, rs);

        void AddSystemScreens()
        {
            #region masts screen
            Vector2
                sqvpos = Lib.V2(206, 204), // standard rect pos
                sqvsz = Lib.V2(88, 24), // standard rect size
                sqoff = Lib.V2(0, 32); // standard rect offset

            var al = Lib.LFT;
            var cnr = TextAlignment.CENTER;

            CtrlScreens.Add(Lib.MS, new Screen
            (
                () => MastNames.Length, 
                new MySprite[]
                {
                    SPR(TXT, "", Lib.V2(20, 108), null, PMY, Lib.VB, 0, 1.3275f),// 0. TUR NAME
                    SPR(TXT, "", Lib.V2(20, 184), null, PMY, Lib.VB, 0, 0.6785f), // 1
                    SPR(SHP, Lib.SQS, sqvpos, sqvsz, PMY, "", al), // 2
                    SPR(SHP, Lib.SQS, sqvpos + sqoff, sqvsz, PMY, "", al), // 3
                    SPR(SHP, Lib.SQS,  sqvpos + 2 * sqoff, sqvsz, PMY, "", al), // 4
                    SPR(SHP, Lib.SQS, sqvpos + 3 * sqoff, sqvsz, PMY, "", al), // 5
                    SPR(SHP, Lib.TRI, Lib.V2(252, 126), Lib.V2(48, 28), PMY), // 6
                    SPR(SHP, Lib.TRI, Lib.V2(252, 162), Lib.V2(48, 28), PMY, null, cnr, MathHelper.Pi), // 7
                    SPR(TXT, "TEST RGT\nTEST RGT\nTEST RGT\nTEST RGT\nTEST RGT\nTEST RGT\nTEST RGT\nTEST RGT", Lib.V2(320, 112), null, PMY, Lib.VB, al, 0.7325f),
                    SPR(TXT, "", Lib.V2(20, 362), null, PMY, Lib.VB, al, 0.6785f),
                    SPR(SHP, Lib.SQS, Lib.V2(312, 256), Lib.V2(8, 288), PMY), // 8
                    SPR(SHP, Lib.SQS, Lib.V2(156, 180), Lib.V2(308, 8), PMY), // 9
                    SPR(SHP, Lib.SQS, Lib.V2(156, 356), Lib.V2(308, 8), PMY), // 10
                    SPR(SHP, Lib.SQS, Lib.V2(188, 264), Lib.V2(8, 176), PMY),   // 11
                },
                ScrollMS, null, null
            ));
            #endregion

            #region turrets screen
            CtrlScreens.Add(Lib.TR, new Screen
            (
                () => TurretNames.Length,
                new MySprite[]
                {
                    SPR(TXT, "", Lib.V2(20, 112), null, PMY, Lib.VB, 0, 0.925f),// 1. TUR NAME
                    SPR(TXT, "AZ\nEL", Lib.V2(20, 160), null, PMY, Lib.VB, 0, 1.825f),// 2. ANGLE HDR
                    SPR(TXT, "", Lib.V2(132, 164), null, PMY, Lib.VB, 0, 0.9125f),// 3. ANGLE DATA
                    SPR(TXT, "", Lib.V2(20, 348), null, PMY, Lib.VB, 0, 0.925f),// 5. STATE
                    SPR(SHP, Lib.SQS, Lib.V2(256, 162), Lib.V2(496, 4), PMY, null),
                    SPR(SHP, Lib.SQS, Lib.V2(256, 346), Lib.V2(496, 4), PMY, null)
                    
                },
                ScrollTR, null, null
            ));
            #endregion

            #region launchers
            CtrlScreens.Add(Lib.LN, new Screen
            (
                () => AMSLaunchers.Length,
                new MySprite[]
                {
                    SPR(TXT, "", Lib.V2(24, 108), null, PMY, Lib.VB, 0, 1.3275f),
                    SPR(TXT, "", Lib.V2(24, 188), null, PMY, Lib.VB, 0, 0.6025f),
                    SPR(TXT, "", Lib.V2(24, 336), null, PMY, Lib.VB, 0, 0.9125f),
                    SPR(TXT, "", Lib.V2(0, 0)),
                    SPR(TXT, "", Lib.V2(0, 0)),
                    SPR(SHP, Lib.SQS, Lib.V2(256, 180), Lib.V2(496, 8), PMY),
                    SPR(SHP, Lib.TRI, Lib.V2(464, 126), Lib.V2(48, 28), PMY),
                    SPR(SHP, Lib.TRI, Lib.V2(464, 162), Lib.V2(48, 28), PMY, null, cnr, MathHelper.Pi),
                },
                ScrollLN, null, null
            ));
            #endregion
        }
    }
}