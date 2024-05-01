using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace IngameScript
{
    public class ScanComp : CompBase
    {
        string[] masts, tags; //= { "[A]", "[B]", "[C]", "[D]" };

        public List<LidarArray> Lidars = new List<LidarArray>();
        public Dictionary<string, LidarMast> Masts = new Dictionary<string, LidarMast>();

        public List<IMyLargeTurretBase> AllTurrets, Artillery;
        List<IMyCameraBlock> _camerasDebug = new List<IMyCameraBlock>();
        public double maxRaycast;
        int tPtr, tStep;
        //DEBUG
        IMyTextPanel _panel;
        public ScanComp(string n) : base(n, Lib.u1 | Lib.u10 | Lib.u100)
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
        string mastUpdate(ref Screen s, int? ptr = null)
        {
            string grps = ""; int i = 0, scns = 0, cams = 0, p = ptr ?? s.ptr;
            var l = Masts[masts[p]];
            for (; i < l.Lidars.Count; i++)
            {
                grps += $"GRP {l.Lidars[i].tag} {l.Lidars[i].Scans}/{l.Lidars[i]._ct}\n";
                cams += l.Lidars[i]._ct;
                scns += l.Lidars[i].Scans;
            }
            grps += $"TTL CAMS {cams} SCNS " + (scns < 10 ? $" {scns}" : $"{scns}");
            s.SetData(grps, 1);
            return l.Name;
        }
        public override void Setup(Program m)
        {
            Clear();
            Main = m;
            var lct = "LargeCalibreTurret";
            using (var p = new iniWrap())
                if (p.CustomData(Main.Me))
                {
                    tStep = p.Int(Lib.HDR, "tStep", 4);
                    maxRaycast = p.Double(Lib.HDR, "maxRaycast", 8E3);
                    var tagstr = p.String(Lib.HDR, "lidarTags", "[A]\n[B]\n[C]\n[D]");
                    var a = tagstr.Split('\n');
                    for (int i = 0; i < a.Length; i++)
                        a[i] = a[i].Trim('|');
                    tags = a;
                   m.Terminal.GetBlocksOfType(AllTurrets, b => b.BlockDefinition.SubtypeName != lct && (b.CubeGrid.EntityId == ID) && !(b is IMyLargeInteriorTurret));
                    m.Terminal.GetBlocksOfType(Artillery, b => b.BlockDefinition.SubtypeName == lct && (b.CubeGrid.EntityId == ID));
                    m.Terminal.GetBlocksOfType<IMyMotorStator>(null, mtr =>
                    {
                        if (mtr.CustomName.Contains(Lib.ARY) && mtr.CubeGrid.EntityId == ID)
                        {
                            var tur = new LidarMast(this, mtr, tags);
                            tur.Setup(ref m);
                            if (!Masts.ContainsKey(tur.Name))
                                Masts.Add(tur.Name, tur);
                        }
                        return true;
                    });
                    m.Terminal.GetBlocksOfType<IMyTextPanel>(null, b =>
                    {
                        if (b.CustomName.Contains("BCR Info LCD CIC-FWD"))
                            _panel = b as IMyTextPanel;
                        return false;
                    });
                    masts = Masts.Keys.ToArray();
                }
            //m.Screens.Add("targets", new Screen(() => _targetIDs.Count, new MySprite[]
            //{
            //  new MySprite(SpriteType.TEXT, "", new Vector2(20, 112), null, Lib.GRN, Lib.VB, 0, 0.925f),// 1. TUR NAME
            //  new MySprite(SpriteType.TEXT, "", new Vector2(20, 160), null, Lib.GRN, Lib.VB, 0, 1.825f),// 2. ANGLE HDR
            //  new MySprite(SpriteType.TEXT, "", new Vector2(132, 164), null, Lib.GRN, Lib.VB, 0, 0.9125f),// 3. ANGLE DATA
            //  new MySprite(SpriteType.TEXT, "", new Vector2(488, 160), null, Lib.GRN, Lib.VB, (TextAlignment)1, 1.825f),// 4. RPM
            //  new MySprite(SpriteType.TEXT, "", new Vector2(20, 348), null, Lib.GRN, Lib.VB, 0, 0.925f)// 5. WPNS
            //  }, (s) =>
            //  {
            //      var t = _targetIDs[s.ptr];
            //      s.SetData($"T {t.ToString("X")}", 0);
            //      s.SetData($"TGT {MathHelper.ToDegrees(t.aziTgt)}°\nCUR {t.aziDeg}°\nTGT {MathHelper.ToDegrees(t.elTgt)}°\nCUR {t.elDeg}°", 2);
            //      s.SetData($"{t.Azimuth.TargetVelocityRPM}\n{t.Elevation.TargetVelocityRPM}", 3);
            //      s.SetData("WEAPONS " + (t.isShoot ? "ENGAGING" : "INACTIVE"), 4);
            //  }));
            var al = TextAlignment.LEFT;
            var sqvpos = new Vector2(356, 222); // standard rect pos
            var sqvsz = new Vector2(128, 28); // standard rect size
            var sqoff = new Vector2(0, 40); // standard rect offset
            m.Screens.Add("masts", new Screen(() => masts.Length, new MySprite[]
            {
                new MySprite(Lib.TXT, "", new Vector2(24, 112), null, Lib.GRN, Lib.VB, 0, 1.75f),// 1. TUR NAME
                new MySprite(Lib.TXT, "", new Vector2(24, 200), null, Lib.GRN, Lib.VB, 0, 0.8195f),
                new MySprite(Lib.SHP, Lib.SQS, sqvpos, sqvsz, Lib.GRN, "", al),
                new MySprite(Lib.SHP, Lib.SQS, sqvpos + sqoff, sqvsz, Lib.GRN, "", al),
                new MySprite(Lib.SHP, Lib.SQS,  sqvpos + 2 * sqoff, sqvsz, Lib.GRN, "", al),
                new MySprite(Lib.SHP, Lib.SQS, sqvpos + 3 * sqoff, sqvsz, Lib.GRN, "", al),
            }, (s) =>
            {       
                s.SetData($"{mastUpdate(ref s)} {s.ptr + 1}/{masts.Length}", 0);
            }, 128f, Lib.u10));

            m.Screens.Add(Lib.SYA, new Screen(() => masts.Length, new MySprite[]
{
               new MySprite(Lib.TXT, "", new Vector2(24, 112), null, Lib.GRN, Lib.VB, 0, 1.75f),// 1. TUR NAME
                new MySprite(Lib.TXT, "", new Vector2(24, 200), null, Lib.GRN, Lib.VB, 0, 0.8195f),
                new MySprite(Lib.SHP, Lib.SQS, sqvpos, sqvsz, Lib.GRN, "", al),
                new MySprite(Lib.SHP, Lib.SQS, sqvpos + sqoff, sqvsz, Lib.GRN, "", al),
                new MySprite(Lib.SHP, Lib.SQS,  sqvpos + 2 * sqoff, sqvsz, Lib.GRN, "", al),
                new MySprite(Lib.SHP, Lib.SQS, sqvpos + 3 * sqoff, sqvsz, Lib.GRN, "", al),
            }, (s) =>
            {
                s.SetData($"{mastUpdate(ref s, 0)} LDR", 0);
            }, 128f, Lib.u10));
            if (Masts.Count == 2)
            m.Screens.Add(Lib.SYB, new Screen(() => masts.Length, new MySprite[]
            {
               new MySprite(Lib.TXT, "", new Vector2(24, 112), null, Lib.GRN, Lib.VB, 0, 1.75f),// 1. TUR NAME
                new MySprite(Lib.TXT, "", new Vector2(24, 200), null, Lib.GRN, Lib.VB, 0, 0.8195f),
                new MySprite(Lib.SHP, Lib.SQS, sqvpos, sqvsz, Lib.GRN, "", al),
                new MySprite(Lib.SHP, Lib.SQS, sqvpos + sqoff, sqvsz, Lib.GRN, "", al),
                new MySprite(Lib.SHP, Lib.SQS,  sqvpos + 2 * sqoff, sqvsz, Lib.GRN, "", al),
                new MySprite(Lib.SHP, Lib.SQS, sqvpos + 3 * sqoff, sqvsz, Lib.GRN, "", al),
            }, (s) =>
            {
                s.SetData($"{mastUpdate(ref s, 1)} LDR", 0);
            }, 128f, Lib.u10));
            Commands.Add("designate", (b) =>
            {
                if (Masts.ContainsKey(b.Argument(2)))
                    Masts[b.Argument(2)].Designate();
            });
            Commands.Add("tureset", (b) =>
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
            if (_panel != null)
            {
                _panel.ContentType = ContentType.TEXT_AND_IMAGE;
            }
        }
        void CheckTurret(IMyLargeTurretBase t)
        {
            MyDetectedEntityInfo info;
            if (t.HasTarget)
            {
                info = t.GetTargetedEntity();
                PassTarget(info);
                t.ResetTargetingToDefault();
                t.EnableIdleRotation = false;
            }
        }
        public override void Update(UpdateFrequency u)
        {
            Debug = "";
            
            if (Main.F % 5 == 0)
                foreach (var m in Masts.Values)
                    m.Update();

                int n = Math.Min(AllTurrets.Count, tPtr + tStep);
                for (; tPtr < n; tPtr++)
                    CheckTurret(AllTurrets[tPtr]);

                for (int i = 0; i < Artillery.Count; i++)
                    CheckTurret(Artillery[i]);

                if (n == AllTurrets.Count)
                {
                    tPtr = 0;
                }
            
            // ---------------------------------------[DEBUG]-------------------------------------------------
            if (_panel != null)
            {
                int count = 0;
                string s = "";
                bool newline = false;
                _panel.ContentType = ContentType.TEXT_AND_IMAGE;
                _panel.ContentType = ContentType.SCRIPT;
                _camerasDebug.Clear();
                foreach (var mast in Masts.Values)
                    mast.DumpAllCameras(ref _camerasDebug);
                _camerasDebug.Sort(temp);
                foreach (var c in _camerasDebug)
                {
                    ++count;
                    var nam = c.CustomName.Remove(0, 4);
                    var a = nam.ToCharArray();
                    var ct = count.ToString("00");
                    a[6] = nam[7];
                    a[7] = ct[0];
                    a[8] = ct[1];
                    nam = new string(a);
                    if (nam.Contains("MAIN"))
                        nam = nam.Substring(0, nam.Length - 5);

                    if (nam[nam.Length - 1] == 'C')
                        s += $"{nam}  {c.AvailableScanRange:G1}m";
                    else
                        s += $"{nam} {c.AvailableScanRange:G1}m";
                    s += newline ? "\n" : "    ";
                    newline = !newline;
                }
                var f = _panel.DrawFrame();
                var cnr = new Vector2(256, 256);
                f.Add(new MySprite(data: "SquareHollow", position: cnr, size: 2 * cnr, color: Lib.GRN));
                f.Add(new MySprite(data: "SquareSimple", position: cnr, size: new Vector2(16, 512), color: Lib.GRN));
                f.Add(new MySprite(SpriteType.TEXT, s.ToUpper(), new Vector2(28, 16), null, Lib.GRN, "VCR", TextAlignment.LEFT, 0.275f));
                f.Dispose();
            }
            // ---------------------------------------[DEBUG]-------------------------------------------------

        }
        RangeComparer temp = new RangeComparer();

    }
}