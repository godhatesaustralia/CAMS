using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    public class ScanComp : CompBase
    {
        Dictionary<long, Target> TargetsScratchpad = new Dictionary<long, Target>();
        public Dictionary<long, Target> Targets => Manager.Targets; // IEnumerator sneed
        public long ID => Manager.Program.Me.CubeGrid.EntityId;
        string[] masts, tags; //= { "[A]", "[B]", "[C]", "[D]" };
        readonly HashSet<long> subgrids = new HashSet<long>();
        public double Time => Manager.Runtime;
        public List<LidarArray> Lidars = new List<LidarArray>();
        public Dictionary<string, LidarTurret> Masts = new Dictionary<string, LidarTurret>();
        List<long> targetIDs = new List<long>();
        public List<IMyLargeTurretBase> Turrets, LockingTurrets;
        public double maxRaycast;
        public string Debug;
        int tPtr, tStep;
        public ScanComp(string n) : base(n, UpdateFrequency.Update10)
        {
            Turrets = new List<IMyLargeTurretBase>();
            LockingTurrets = new List<IMyLargeTurretBase>();
        }

        void Clear()
        {
            Turrets.Clear();
            targetIDs.Clear();
            LockingTurrets.Clear();
            subgrids.Clear();
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
        public override void Setup(CombatManager m, ref iniWrap p)
        {
            Clear();
            Manager = m;
            var lct = "LargeCalibreTurret";
            tStep = p.Int(Lib.hdr, "tStep", 4);
            maxRaycast = p.Double(Lib.hdr, "maxRaycast", 5000);
            var tagstr = p.String(Lib.hdr, "lidarTags", "[A]\n[B]\n[C]\n[D]");
            var a = tagstr.Split('\n');
            for (int i = 0; i < a.Length; i++)
                a[i] = a[i].Trim('|');
            tags = a;
            Manager.Terminal.GetBlocksOfType<IMyMotorStator>(null, (b) =>
            {
                var i = b.TopGrid.EntityId;
                if (!subgrids.Contains(i))
                    subgrids.Add(i);
                return true;
            });
            Manager.Terminal.GetBlocksOfType(Turrets, (b) => b.BlockDefinition.SubtypeName != lct && (b.CubeGrid.EntityId == ID) && !(b is IMyLargeInteriorTurret));
            Manager.Terminal.GetBlocksOfType(LockingTurrets, (b) => b.BlockDefinition.SubtypeName == lct && (b.CubeGrid.EntityId == ID));
            Manager.Terminal.GetBlocksOfType<IMyMotorStator>(null, (mtr) =>
            {
                if (mtr.CustomName.Contains(Lib.array) && mtr.CubeGrid.EntityId == ID)
                {
                    var tur = new LidarTurret(this, mtr, tags);
                    tur.Setup(ref m);
                    if (!Masts.ContainsKey(tur.Name))
                        Masts.Add(tur.Name, tur);
                }
                return true;
            });
            masts = Masts.Keys.ToArray();
            //Manager.Screens.Add("targets", new Screen(() => targetIDs.Count, new MySprite[]
            //{
            //  new MySprite(SpriteType.TEXT, "", new Vector2(20, 112), null, Lib.Green, Lib.vb, 0, 0.925f),// 1. TUR NAME
            //  new MySprite(SpriteType.TEXT, "", new Vector2(20, 160), null, Lib.Green, Lib.vb, 0, 1.825f),// 2. ANGLE HDR
            //  new MySprite(SpriteType.TEXT, "", new Vector2(132, 164), null, Lib.Green, Lib.vb, 0, 0.9125f),// 3. ANGLE DATA
            //  new MySprite(SpriteType.TEXT, "", new Vector2(488, 160), null, Lib.Green, Lib.vb, (TextAlignment)1, 1.825f),// 4. RPM
            //  new MySprite(SpriteType.TEXT, "", new Vector2(20, 348), null, Lib.Green, Lib.vb, 0, 0.925f)// 5. WPNS
            //  }, (s) =>
            //  {
            //      var t = targetIDs[s.ptr];
            //      s.SetData($"T {t.ToString("X")}", 0);
            //      s.SetData($"TGT {MathHelper.ToDegrees(t.aziTgt)}°\nCUR {t.aCur}°\nTGT {MathHelper.ToDegrees(t.elTgt)}°\nCUR {t.eCur}°", 2);
            //      s.SetData($"{t.Azimuth.TargetVelocityRPM}\n{t.Elevation.TargetVelocityRPM}", 3);
            //      s.SetData("WEAPONS " + (t.isShoot ? "ENGAGING" : "INACTIVE"), 4);
            //  }));
            var al = TextAlignment.LEFT;
            Manager.Screens.Add("masts", new Screen(() => masts.Length, new MySprite[]
            {
               new MySprite(SpriteType.TEXT, "", new Vector2(24, 112), null, Lib.Green, Lib.vb, 0, 1.75f),// 1. TUR NAME
                new MySprite(SpriteType.TEXT, "", new Vector2(24, 200), null, Lib.Green, Lib.vb, 0, 0.8195f),
                new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(356, 222) ,new Vector2(128, 28), Lib.Green, "", al),
                new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(356, 262) ,new Vector2(128, 28), Lib.Green, "", al),
                new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(356, 302) ,new Vector2(128, 28), Lib.Green, "", al),
                new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(356, 342) ,new Vector2(128, 28), Lib.Green, "", al),
            }, (s) =>
            {
                string grps = ""; int i = 0, scns = 0, cams = 0;
                var l = Masts[masts[s.ptr]];
                s.SetData($"{l.Name} {s.ptr + 1}/{masts.Length}", 0);
                for (; i < l.Lidars.Count; i++) 
                {
                    grps += $"GRP {l.Lidars[i].tag} {l.Lidars[i].scans}/{l.Lidars[i].ct}\n";
                    cams += l.Lidars[i].ct;
                    scns += l.Lidars[i].scans;
                }
                grps += $"TTL CAMS {cams} SCNS " + (scns < 10 ? $" {scns}" : $"{scns}");
                s.SetData(grps, 1);
                for (i = 0; i < l.Lidars.Count; ++i)
                    s.SetLength(Convert.ToSingle(l.avgDists[i] / maxRaycast), i + 2);
               
            }, 128f, UpdateFrequency.Update10));
        }
        void CheckTurret(IMyLargeTurretBase t)
        {
            MyDetectedEntityInfo info;
            if (t.HasTarget)
            {
                info = t.GetTargetedEntity();
                AddOrUpdateTGT(info);
                t.ResetTargetingToDefault();
                t.Range = 800;
                t.EnableIdleRotation = false;
            }
        }
        public override void Update(UpdateFrequency u)
        {
            Debug = "";
            // i THINK this is a laggy thing
            int n = Math.Min(Turrets.Count, tPtr + tStep);
            for (; tPtr < n; tPtr++)
            {
                CheckTurret(Turrets[tPtr]);
            }
            for (int i = 0; i < LockingTurrets.Count; i++)
            {
                CheckTurret(LockingTurrets[i]);
            }
            if (n == Turrets.Count)
            {
                tPtr = 0;
            }
            foreach (var m in Masts.Values)
                m.Update();
            foreach (var l in Lidars)
                l.TryScanUpdate(this);
            foreach (var tgt in TargetsScratchpad)
            {
                var i = tgt.Key;
                if (Targets.ContainsKey(i) && (Manager.Targets[i].Elapsed(Manager.Runtime) <= Lib.maxTimeTGT))
                    continue;
                Targets[i] = TargetsScratchpad[i];

            }

        }

        public void ResetAllStupidTurrets()
        {
            foreach (var t in Turrets)
            {
                t.Azimuth = 0;
                t.Elevation = 0;
            }
        }

        public void Designate(string name) => Masts[name].Designate();

        public void AddOrUpdateTGT(MyDetectedEntityInfo info)
        {
            var i = info.EntityId;
            if (info.IsEmpty()) return;
            if (subgrids.Contains(i)) return;
            var d = (Manager.Center - info.Position).Length();
            if (Targets.ContainsKey(info.EntityId))
            {
                //if ((Manager.Runtime - Targets[i].Timestamp <= Lib.maxTimeTGT))
                //    return;
            }
            TargetsScratchpad[i] = new Target(info, Manager.Runtime, ID, d);
            targetIDs.Add(info.EntityId);
        }

    }
}