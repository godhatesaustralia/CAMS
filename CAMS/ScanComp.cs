using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;

namespace IngameScript
{
    public class ScanComp : CompBase
    {
        public Dictionary<long, Target> Targets => Manager.Targets;
        public long ID => Manager.Program.Me.CubeGrid.EntityId;
        readonly string[] tags = { "[A]", "[B]", "[C]", "[D]" };
        readonly HashSet<long> subgrids = new HashSet<long>();
        public double Time => Manager.Runtime;
        public List<LidarArray> Lidars = new List<LidarArray>();
        public List<LidarTurret> Masts = new List<LidarTurret>();
        public List<IMyLargeTurretBase> Turrets, LockingTurrets;
        public double maxDistance = 5000;
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

        public override void Setup(CombatManager m)
        {
            Clear();
            Manager = m;
            var lct = "LargeCalibreTurret";
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
                    if (!Masts.Contains(tur))
                        Masts.Add(tur);
                }
                return true;
            });
            foreach (var mast in Masts)
                mast.Setup(ref Manager);
        }

        public override void Update()
        {
            MyDetectedEntityInfo info;
            for (int i = 0; i < Turrets.Count; i++)
            {
                if (Turrets[i].HasTarget)
                {
                    info = Turrets[i].GetTargetedEntity();
                    if (!Targets.ContainsKey(info.EntityId))
                    {
                        AddOrUpdateTGT(info);
                        continue;
                    }
                }
                Turrets[i].ResetTargetingToDefault();
                Turrets[i].Range = 800;
                Turrets[i].EnableIdleRotation = false;
            }
            foreach (var m in Masts)
                m.Update();
            foreach (var l in Lidars)
                l.TryScanUpdate(this);
        }

        public void ResetAllStupidTurrets()
        {
            foreach (var t in Turrets)
            {
                t.Azimuth = 0;
                t.Elevation = 0;
            }
        }

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
                else
                    Targets[i] = new Target(info, Manager.Runtime, ID, d);
            }
            else Targets.Add(info.EntityId, new Target(info, Manager.Runtime, ID, d));
        }
        
    }
}