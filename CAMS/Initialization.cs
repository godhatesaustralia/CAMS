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
        public const string H = "CAMS";
        public bool Based, ReceiveIGC, SendIGC;
        public double
            PDSpray,
            ScanDistLimit;
        public int
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
                    ReceiveIGC = p.Bool(H, "useNetworkedTargeting", false);
                    SendIGC = p.Bool(H, "transmitTargetData", false);
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
            var dspGrp = new List<IMyTerminalBlock>();
            Terminal.GetBlocksOfType<IMyShipController>(null, (b) =>
            {
                if (b.CubeGrid == Me.CubeGrid && b.CustomName.Contains(ControllerTag))
                    Controller = b;
                return false;
            });
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
                        d = new Display(this, b, CtrlScreens.First().Key, Based);
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
                    var tur = new LidarMast(this, mtr, MastAryTags);
                    tur.Setup(this);
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

        void AddSystemScreens()
        {
            #region masts
            Vector2
                sqvpos = Lib.V2(356, 222), // standard rect pos
                sqvsz = Lib.V2(128, 28), // standard rect size
                sqoff = Lib.V2(0, 40); // standard rect offset
            var al = Lib.LFT;
            CtrlScreens.Add(Lib.MS, new Screen
            (
                () => MastNames.Length, 
                new MySprite[]{
                    new MySprite(TXT, "", Lib.V2(24, 112), null, PMY, Lib.VB, 0, 1.75f),// 1. TUR NAME
                    new MySprite(TXT, "", Lib.V2(24, 200), null, PMY, Lib.VB, 0, 0.8195f),
                    new MySprite(SHP, Lib.SQS, sqvpos, sqvsz, PMY, "", al),
                    new MySprite(SHP, Lib.SQS, sqvpos + sqoff, sqvsz, PMY, "", al),
                    new MySprite(SHP, Lib.SQS,  sqvpos + 2 * sqoff, sqvsz, PMY, "", al),
                    new MySprite(SHP, Lib.SQS, sqvpos + 3 * sqoff, sqvsz, PMY, "", al),
                },
                (p, s) => s.SetData($"{MastScreen(ref s, p)} {p + 1}/{MastNames.Length}", 0), 
                128f
            ));
            #endregion

            #region turrets
            CtrlScreens.Add(Lib.TR, new Screen
            (
                () => TurretNames.Length,
                new MySprite[]
                {
                new MySprite(TXT, "", new Vector2(20, 112), null, PMY, Lib.VB, 0, 0.925f),// 1. TUR NAME
                new MySprite(TXT, "AZ\nEL", new Vector2(20, 160), null, PMY, Lib.VB, 0, 1.825f),// 2. ANGLE HDR
                new MySprite(TXT, "", new Vector2(132, 164), null, PMY, Lib.VB, 0, 0.9125f),// 3. ANGLE DATA
                new MySprite(TXT, "", new Vector2(20, 348), null, PMY, Lib.VB, 0, 0.925f)// 5. STATE
                },
                (p, s) =>
                {
                    var turret = Turrets[TurretNames[p]];
                    string n = turret.Name, st = turret.Status.ToString().ToUpper();
                    int ct = p >= 9 ? 12 : 13;
                    ct -= turret.Name.Length;
                    for (; ct-- > 0;)
                        n += " ";
                    s.SetData(n + $"{p + 1}/{TurretCount}", 0);
                    s.SetData($"RPM {turret.aRPM:00.0}\nCUR {turret.aCur:000}°\nRPM {turret.eRPM:00.0}\nCUR {turret.eCur:000}°", 2);
                    s.SetData(st, 3);
                }
            ));
            #endregion
        }
    }
}