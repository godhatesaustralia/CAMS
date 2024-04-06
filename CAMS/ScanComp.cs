using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;

namespace IngameScript
{
    public class ScanComp : CompBase
    {
        Dictionary<long, Target> TargetsScratchpad = new Dictionary<long, Target>();
        public Dictionary<long, Target> Targets => Manager.Targets; // IEnumerator sneed
        public long ID => Manager.Program.Me.CubeGrid.EntityId;
        string[] tags; //= { "[A]", "[B]", "[C]", "[D]" };
        readonly HashSet<long> subgrids = new HashSet<long>();
        public double Time => Manager.Runtime;
        public List<LidarArray> Lidars = new List<LidarArray>();
        public Dictionary<string, LidarTurret> Masts = new Dictionary<string, LidarTurret>();
        public List<IMyLargeTurretBase> Turrets, LockingTurrets;
        public double maxRaycast;
        public string Debug;
        int tPtr, tStep;
        public ScanComp(string n) : base(n) 
        {
            Turrets = new List<IMyLargeTurretBase>();
            LockingTurrets = new List<IMyLargeTurretBase>();
        }

        void Clear()
        {
            Turrets.Clear(); 
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
                if (Targets.ContainsKey(i) && (Manager.Runtime - Targets[i].Timestamp <= Lib.maxTimeTGT))
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
                if ((Manager.Runtime - Targets[i].Timestamp <= Lib.maxTimeTGT))
                    return;
            }
            TargetsScratchpad[i] = new Target(info, Manager.Runtime, ID, d);
        }
        
    }
}