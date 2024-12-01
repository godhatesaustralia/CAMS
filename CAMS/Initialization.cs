using System;
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
        public const string H = "CAMS", P = "pri", IgcFleet = "[FLT-CA]";
        public Color PMY, SDY, BKG;
        public const SpriteType TXT = SpriteType.TEXT, SHP = SpriteType.TEXTURE, CLP = SpriteType.CLIP_RECT;
        public bool Based;

        public double
            PDSpray,
            ScanDistLimit;

        public int
            HardpointsCount,
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
            RackNamesList;

        void ParseComputerSettings()
        {
            var r = new MyIniParseResult();
            using (var p = new iniWrap())
                if (p.CustomData(Me, out r))
                {
                    string
                        grp = "Rotors",
                        def = " CAMS Azimuths",
                        arys = p.String(H, "lidarTags", "[A],[B],[C],[D]");

                    MastAryTags = arys.Split(',');

                    ShipTag = p.String(H, "tag", H);
                    ControllerTag = p.String(H, "controller", "Helm");
                    DisplayGroup = p.String(H, "displays", "MFD Users");
                    TurPDLRGroup = p.String(H, "pd" + grp, "PD" + def);
                    TurMainGroup = p.String(H, "main" + grp, "Main" + def);
                    RackNamesList = p.String(H, "rack" + grp);

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
                    HardpointsCount = p.Int(H, "mslHardpoints", 16);

                    BKG = p.Color(H, "backgroundColor", new Color(3, 8, 3));
                    PMY = p.Color(H, "primaryColor", new Color(100, 250, 100));
                    SDY = p.Color(H, "secondaryColor", new Color(50, 125, 50));

                    _surf.BackgroundColor = BKG;
                    sprites = p.Sprites(H, "sprites");

                    Targets.UpdateRadarSettings(this);

                    mslReuse = new List<Missile>(HardpointsCount);
                    Missiles = new Dictionary<long, Missile>(HardpointsCount * 2);
                    ekvTargets = new HashSet<long>(MaxTgtKillTracks);
                }
                else throw new Exception($"\n{r.Error} at line {r.LineNo} of {Me} custom data.");
        }

        void CacheMainSystems()
        {
            #region controller and displays
            Terminal.GetBlocksOfType<IMyShipController>(null, b =>
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
                        d = new Display(this, b, Lib.RD, Based);
                        Displays.Add(d.Name, d);
                    }
                    else if (b is IMyTextSurfaceProvider)
                    {
                        d = new Display(this, b, Lib.MS, Based);
                        Displays.Add(d.Name, d);
                    }
                }
                DisplayRR = new RoundRobin<string, Display>(Lib.Keys(ref Displays));
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
            MastNames = Lib.Keys(ref Masts);
            #endregion
            
            #region turrets and racks
            var r = new List<IMyMotorStator>();

            Terminal.GetBlockGroupWithName(TurPDLRGroup).GetBlocks(null, b =>
            {
                var a = b as IMyMotorStator;
                if (a != null)
                    r.Add(a);
                return false;
            });
            foreach (var az in r)
            {
                var pd = new PDT(az, this, MaxScansPDLR);
                if (pd != null)
                    Turrets.Add(pd.Name, pd);
            }
            PDTNames = Lib.Keys(ref Turrets);

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

            var antmp = new List<string>();
            foreach (var b in RackNamesList.Split('\n'))
            {
                var a = Terminal.GetBlockWithName(b) as IMyMotorStator;
                if (a != null)
                {
                    var q = new iniWrap();
                    if (q.CustomData(a))
                    {
                        Launcher l;
                        if (q.HasKey(H, Lib.N))
                        {
                            var n = q.String(H, Lib.N);
                            if (n != "")
                            {
                                l = q.Bool(H, "arm") ? new ArmLauncher(n, this, a) : new Launcher(n, this);
                                if (l.Setup(ref q)) Launchers[l.Name] = l;
                            }
                        }
                        else
                        {
                            var n = q.String(H, "names").Split('\n');
                            if (n[0].Length > 0)
                                foreach (var s in n)
                                {
                                    l = new Launcher(s.Trim('|'), this);
                                    if (l.Setup(ref q)) Launchers[l.Name] = l;
                                }
                        }
                    }
                    q.Dispose();
                }
            }

            foreach (var kvp in Launchers)
                if (kvp.Value.Auto)
                    antmp.Add(kvp.Key);

            AMSNames = new string[antmp.Count];
            for (int i = 0; i < antmp.Count; i++)
                AMSNames[i] = antmp[i];

            var temp = Lib.Keys(ref Launchers);

            ReloadRR = new RoundRobin<string, Launcher>(temp);
            FireRR = new RoundRobin<string, Launcher>(temp);

            temp = Lib.Keys(ref Turrets);

            AssignRR = new RoundRobin<string, RotorTurret>(temp);
            UpdateRR = new RoundRobin<string, RotorTurret>(temp);
            #endregion

        }

        void SystemCommands()
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

            Commands.Add("fire", b =>
            {
                if (!Targets.Exists(Targets.Selected)) return;

                if (b.ArgumentCount == 2 && Launchers.ContainsKey(b.Argument(1)))
                    Launchers[b.Argument(1)].Fire(Targets.Selected, ref Missiles);
            });

            Commands.Add("spread", b =>
            {
                if (Targets.Count == 0) return;
                
                _fireID = b.Argument(1) == P ? Targets.Prioritized.Min.EID : Targets.Selected;
                if(int.TryParse(b.Argument(2), out _launchCt))
                    _nxtFireF = F;
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
                sqvpos = Lib.V2(20, 244), // standard rect pos
                sqvsz = Lib.V2(132, 36), // standard rect size
                sqoff = Lib.V2(0, 42); // standard rect offset
            Vector2? n = null;

            var cnr = TextAlignment.CENTER;

            CtrlScreens.Add(Lib.MS, new Screen
            (
                () => MastNames.Length,
                new MySprite[]
                {
                    SPR(TXT, "", Lib.V2(20, 108), n, PMY, Lib.VB, 0, 0.8735f),// 4
                    SPR(TXT, "", Lib.V2(272, 108), n, SDY, Lib.V, Lib.RGT, 0.8735f),
                    SPR(TXT, "", Lib.V2(20, 248), n, PMY, Lib.VB, 0, 0.6135f), // 5
                    SPR(TXT, "", Lib.V2(272, 248), n, SDY, Lib.V, Lib.RGT, 0.6135f), //7
                    
                    SPR(TXT, "", Lib.V2(20, 362), n, PMY, Lib.VB, 0, 0.5935f),       
                    SPR(SHP, Lib.SQS, Lib.V2(282, 256), Lib.V2(8, 288), PMY), // 9
                    SPR(SHP, Lib.SQS, Lib.V2(144, 242), Lib.V2(268, 8), PMY)
                },
                ScrollMS, null, null
            ));
            #endregion

            #region turrets screen
            CtrlScreens.Add(Lib.TR, new Screen
            (
                () => UpdateRR.IDs.Length,
                new MySprite[]
                {
                    SPR(TXT, "", Lib.V2(20, 112), n, PMY, Lib.VB, 0, 0.925f),// 1. TUR NAME
                    SPR(TXT, "AZ\nEL", Lib.V2(20, 160), n, PMY, Lib.VB, 0, 1.825f),// 2. ANGLE HDR 1
                    SPR(TXT, "TG\nCR\nTG\nCR", Lib.V2(132, 164), n, PMY, Lib.VB, 0, 0.9125f), // 2. ANGLE HDR 2
                    SPR(TXT, "", Lib.V2(192, 164), n, SDY, Lib.V, 0, 0.9125f),// 4. ANGLE DATA
                    SPR(TXT, "", Lib.V2(20, 348), n, PMY, Lib.VB, 0, 0.925f),// 5. STATE
                    SPR(TXT, "MODE\nWSPD\nFIRE\nTRCK\nARPM\nERPM", Lib.V2(342, 164), n, PMY, Lib.VB, Lib.LFT, 0.6045f),
                    SPR(TXT, "", Lib.V2(496, 164), n, SDY, Lib.V, Lib.RGT, 0.6045f),
                    SPR(SHP, Lib.SQS, Lib.V2(256, 162), Lib.V2(496, 4), PMY, null),
                    SPR(SHP, Lib.SQS, Lib.V2(256, 346), Lib.V2(496, 4), PMY, null),
                    SPR(SHP, Lib.SQS, Lib.V2(332, 255), Lib.V2(4, 296), PMY)
                },
                ScrollTR, null, null
            ));
            #endregion

            #region launchers
            CtrlScreens.Add(Lib.LN, new Screen
            (
                () => ReloadRR.IDs.Length,
                new MySprite[]
                {
                    SPR(TXT, "", Lib.V2(24, 108), n, PMY, Lib.VB, 0, 0.925f),
                    SPR(TXT, "", Lib.V2(136, 160), n, SDY, Lib.V, 0, 0.6025f),
                    SPR(TXT, "", Lib.V2(24, 160), n, PMY, Lib.VB, 0, 0.6025f),
                    SPR(TXT, "", Lib.V2(320, 112), n, SDY, Lib.V, 0, 0.5935f),
                    SPR(TXT, "TGT_SELCTD\n\nMX_PRY_TGT\n\nEKV_TGT_CT\n\nSYS_TGT_CT\n\nSYS_MSL_CT", Lib.V2(320, 112), null, PMY, Lib.VB, 0, 0.5935f),
                    SPR(SHP, Lib.SQS, Lib.V2(156, 180), Lib.V2(308, 8), PMY), // 5
                    SPR(SHP, Lib.SQS, Lib.V2(312, 256), Lib.V2(8, 288), PMY), // 8
                    SPR(SHP, Lib.SQS, Lib.V2(156, 320), Lib.V2(308, 8), PMY), // 9
                    SPR(SHP, Lib.SQS, Lib.V2(120, 252), Lib.V2(8, 144), PMY, null, Lib.LFT)
                },
                ScrollLN, null, null
            ));
            #endregion
        }
    }
}