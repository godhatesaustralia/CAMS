using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using System.Runtime.InteropServices;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        public const string 
            H = "CAMS", 
            P = "pri", 
            IgcFleet = "[FLT-CA]";
        public Color PMY, SDY, BKG;
        public const SpriteType TXT = SpriteType.TEXT, SHP = SpriteType.TEXTURE, CLP = SpriteType.CLIP_RECT;
        public float FSCL;

        public double
            PDSpray,
            ScanChgLimit,
            ScanDistLimit;

        public int
            TgtRefreshTicks,
            HardpointsCount,
            SendIGCTicks,
            ReceiveIGCTicks,
            MaxAutoTgtChecks,
            MaxScansMasts,
            MaxScansPDLR,
            MaxTgtKillTracks,
            MaxRotorTurretUpdates,
            PriorityCheckTicks;

        public string
            F_BD,
            F_DF,
            ShipTag,
            ControllerTag,
            DisplayGroup,
            TurPDLRGroup,
            TurMainGroup,
            RackNamesList,
            PanelNamesList;

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
                    DisplayGroup = p.String(H, "displays", "CAMS MFD");
                    TurPDLRGroup = p.String(H, "pd" + grp, "PD" + def);
                    TurMainGroup = p.String(H, "main" + grp, "Main" + def);
                    RackNamesList = p.String(H, "rack" + grp);
                    PanelNamesList = p.String(H, "lidar" + grp);

                    Lib.VCR = p.Bool(H, "vcr");
                    TgtRefreshTicks = p.Int(H, "tgtRefreshInterval", 4);
                    ReceiveIGCTicks = p.Int(H, "igcCheckInterval", 0);
                    SendIGCTicks = p.Int(H, "igcTransmitInterval", 0);
                    PDSpray = p.Double(H, "spray", -1);
                    ScanDistLimit = p.Double(H, "maxRaycast", 8E3);
                    ScanChgLimit = p.Double(H, "minLdrChg", 1E4);
                    MaxScansMasts = p.Int(H, "maxScansMast", 1);
                    MaxScansPDLR = p.Int(H, "maxScansPDLR", 3);
                    MaxAutoTgtChecks = p.Int(H, "tStep", 4);
                    MaxRotorTurretUpdates = p.Int(H, "maxTurUpdates", 1);
                    MaxTgtKillTracks = p.Int(H, "maxKillTracks", 3);

                    PriorityCheckTicks = p.Int(H, "priorityUpdateTicks", 10);
                    HardpointsCount = p.Int(H, "mslHardpoints", 16);

                    BKG = p.Color(H, "backgroundColor", new Color(3, 8, 3));
                    PMY = p.Color(H, "primaryColor", new Color(100, 250, 100));
                    SDY = p.Color(H, "secondaryColor", new Color(50, 125, 50));

                    _surf.BackgroundColor = BKG;
                    
                    if (Lib.VCR)
                    {
                        Lib.F_BD = "VCRBold";
                        Lib.F_DF = "VCR";
                        Lib.FSCL = 1;

                        sprites = p.Sprites(H, Lib.SPR);
                    }
                    else 
                    {
                        Lib.F_BD = Lib.F_DF = "Debug";
                        Lib.FSCL = 1.625f;

                        sprites = p.Sprites(H, "SPRITES_V");
                    }

                    Targets.UpdateRadarSettings(this);

                    mslReuse = new List<Missile>(HardpointsCount);
                    Missiles = new Dictionary<long, Missile>(HardpointsCount * 2);
                    ekvTracks = new HashSet<long>(MaxTgtKillTracks);
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
            if (Controller == null) throw new Exception($"\nNo ship controller found with tag {ControllerTag}.");

            var dspGrp = new List<IMyTerminalBlock>();
            Terminal.GetBlockGroupWithName(DisplayGroup)?.GetBlocks(dspGrp);

            if (dspGrp.Count > 0)
            {
                foreach (var b in dspGrp)
                {
                    Display d;
                    if (b is IMyTextPanel)
                    {
                        d = new Display(this, b, Lib.RD);
                        Displays.Add(d.Name, d);
                    }
                    else if (b is IMyTextSurfaceProvider)
                    {
                        d = new Display(this, b, Lib.LN);
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
            if (Masts.Count > 0) MastsRR = new RoundRobin<string, LidarMast>(Lib.Keys(ref Masts));
            else 
            {
                CtrlScreens.Remove(Lib.MS);
                // todo - alternate
            }

            // if ( PanelNamesList != "")
            // {
            //     var c = new List<IMyCameraBlock>();
            //     foreach (var b in PanelNamesList.Split('\n'))
            //     {
            //         c.Clear();
            //         var l = Terminal.GetBlockGroupWithName(b);
            //         l?.GetBlocksOfType(c);

            //         if (c.Count > 0)
            //         {
            //             var dir = Controller.WorldMatrix.GetClosestDirection(c[0].WorldMatrix.Forward);
            //             Panels[dir] = new LidarArray(c);
            //         }
            //     }
            // }
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
            if (Turrets.Count > 0)
                PDTRR = new RoundRobin<string, RotorTurret>(Lib.Keys(ref Turrets));


            var ntmp = new List<string>();
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
                {
                    ntmp.Add(tr.Name);
                    Turrets.Add(tr.Name, tr);
                }
            }
            if (ntmp.Count > 0)
            {
                var mn = new string[ntmp.Count];
                for (int i = 0; i < ntmp.Count; i++)
                    mn[i] = ntmp[i]; 

                MainRR = new RoundRobin<string, RotorTurret>(mn);
            }
            if (Turrets.Count > 0)
            {
                AssignRR = new RoundRobin<string, RotorTurret>(Lib.Keys(ref Turrets));
            }
            else CtrlScreens.Remove(Lib.TR);

            ntmp.Clear();
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
                    ntmp.Add(kvp.Key);

            AMSNames = new string[ntmp.Count];
            for (int i = 0; i < ntmp.Count; i++)
                AMSNames[i] = ntmp[i];

            string[] temp;

            if (Launchers.Count > 0)
            {
                temp = Lib.Keys(ref Launchers);
                ReloadRR = new RoundRobin<string, Launcher>(temp);
                FireRR = new RoundRobin<string, Launcher>(temp);
            }
            else CtrlScreens.Remove(Lib.LN);

            #endregion

        }

        static MySprite SPR(SpriteType t, string d, Vector2 pos, Vector2? sz = null, Color? c = null, string f = null, TextAlignment a = TextAlignment.CENTER, float rs = 0) => new MySprite(t, d, pos, sz, c, f, a, rs);

        void AddSystemScreens()
        {    
            Vector2? n = null;

            CtrlScreens[Lib.MS] = new Screen
            (
                () => MastsRR.IDs.Length,
                new MySprite[]
                {
                    SPR(TXT, "", Lib.V2(20, 108), n, PMY, Lib.F_BD, 0, 0.8735f),// 4
                    SPR(TXT, "", Lib.V2(272, 108), n, SDY, Lib.F_DF, Lib.RGT, 0.8735f),
                    SPR(TXT, "", Lib.V2(20, 248), n, PMY, Lib.F_BD, 0, 0.6135f), // 5
                    SPR(TXT, "", Lib.V2(272, 248), n, SDY, Lib.F_DF, Lib.RGT, 0.6135f), //7                 
                    SPR(TXT, "TGTS\nTEID\nDIST\nELEV\nASPD\nACCL\nSIZE\nSCOR\nHITS", Lib.V2(292, 112), n, PMY, Lib.F_BD, 0, 0.6495f),
                    SPR(TXT, "", Lib.V2(492, 112), n, SDY, Lib.F_DF, Lib.RGT, 0.6495f),
                    SPR(SHP, Lib.SQS, Lib.V2(282, 256), Lib.V2(8, 288), PMY), // 9
                    SPR(SHP, Lib.SQS, Lib.V2(144, 242), Lib.V2(268, 8), PMY)
                },
                ScrollMS, Targets.TargetMode, BackMS
            );

            #region turrets screen
            CtrlScreens[Lib.TR] = new Screen
            (
                () => AssignRR.IDs.Length,
                new MySprite[]
                {
                    SPR(TXT, "", Lib.V2(20, 112), n, PMY, Lib.F_BD, 0, 0.925f),// 1. TUR NAME
                    SPR(TXT, "AZ\nEL", Lib.V2(20, 160), n, PMY, Lib.F_BD, 0, 1.825f),// 2. ANGLE HDR 1
                    SPR(TXT, "TG\nCR\nTG\nCR", Lib.V2(132, 164), n, PMY, Lib.F_BD, 0, 0.9125f), // 2. ANGLE HDR 2
                    SPR(TXT, "", Lib.V2(192, 164), n, SDY, Lib.F_DF, 0, 0.9125f),// 4. ANGLE DATA
                    SPR(TXT, "", Lib.V2(20, 348), n, PMY, Lib.F_BD, 0, 0.925f),// 5. STATE
                    SPR(TXT, "CLCK\nWSPD\nFIRE\nTRCK\nARPM\nERPM", Lib.V2(342, 164), n, PMY, Lib.F_BD, 0, 0.6045f),
                    SPR(TXT, "", Lib.V2(496, 164), n, SDY, Lib.F_DF, Lib.RGT, 0.6045f),
                    SPR(SHP, Lib.SQS, Lib.V2(256, 162), Lib.V2(496, 4), PMY, null),
                    SPR(SHP, Lib.SQS, Lib.V2(256, 346), Lib.V2(496, 4), PMY, null),
                    SPR(SHP, Lib.SQS, Lib.V2(332, 255), Lib.V2(4, 296), PMY)
                },
                ScrollTR, null, null
            );
            #endregion

            #region launchers
            CtrlScreens[Lib.LN] = new Screen
            (
                () => ReloadRR.IDs.Length,
                new MySprite[]
                {
                    SPR(SHP, Lib.SQS, Lib.V2(89, 131.625f), Lib.V2(128, 36), SDY),
                    SPR(TXT, "", Lib.V2(24, 108), n, PMY, Lib.F_BD, 0, 0.8735f),
                    SPR(TXT, "", Lib.V2(300, 108), n, SDY, Lib.F_DF, Lib.RGT, 0.8735f),
                    SPR(TXT, "", Lib.V2(300, 248), n, SDY, Lib.F_DF, Lib.RGT, 0.6135f),
                    SPR(TXT, "", Lib.V2(24, 248), n, PMY, Lib.F_BD, 0, 0.6135f),
                    SPR(TXT, "MEID\nBATT\nFUEL\nCONN\n\nMEID\nBATT\nFUEL\nCONN", Lib.V2(326, 112), n, PMY, Lib.F_BD, 0, 0.6495f),
                    SPR(TXT, "", Lib.V2(490, 112), n, SDY, Lib.F_DF, Lib.RGT, 0.6495f),
                    SPR(SHP, Lib.SQS, Lib.V2(160, 242), Lib.V2(308, 8), PMY), // 5
                    SPR(SHP, Lib.SQS, Lib.V2(408, 254), Lib.V2(184, 8), PMY),
                    SPR(SHP, Lib.SQS, Lib.V2(314, 256), Lib.V2(8, 288), PMY), // 8
                },
                ScrollLN, EnterLN, BackLN
            );
            #endregion
        }
    }
}