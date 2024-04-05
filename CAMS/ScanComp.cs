using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;

namespace IngameScript
{
    public class ScanComp : CompBase
    {
        public Dictionary<long, Target> Targets => Manager.Targets;
        public readonly long ID;
        readonly HashSet<long> subgrids = new HashSet<long>();
        public double Time => Manager.Runtime;
        public List<LidarArray> Lids;
        public double maxDistance = 5000;
        public ScanComp(string n, CombatManager m) : base(n, m) 
        {
            ID = Manager.Controller.CubeGrid.EntityId;
            Manager.Terminal.GetBlocksOfType<IMyMotorStator>(null, (b) =>
            {
                var i = b.TopGrid.EntityId;
                if (!subgrids.Contains(i))
                    subgrids.Add(i);
                return true;
            });
        }

        public void AddOrUpdateTGT(ref Target t)
        {
            t.Distance = (Manager.Center - t.Position).Length();
            if (subgrids.Contains(t.EID)) return;
            if (Targets.ContainsKey(t.EID))
            {
                if ((Manager.Runtime - Targets[t.EID].Timestamp <= Lib.maxTimeTGT))
                    return;
                else
                    Targets[t.EID] = t;
            }
            else Targets.Add(t.EID, t);
        }
    }
}