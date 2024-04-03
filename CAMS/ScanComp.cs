using System.Collections.Generic;

namespace IngameScript
{
    public class ScanComp : CompBase
    {
        public List<DynamicLidar> Turrets;
        public List<LidarArray> Lids;
        public ScanComp(string n, CombatManager m) : base(n, m) 
        { 

        }
    }
}