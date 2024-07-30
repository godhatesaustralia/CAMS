using Sandbox.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        string MastScreen(ref Screen s, int ptr)
        {
            string grps = ""; int i = 0;
            var l = Masts[MastNames[ptr]];
            for (; i < l.Lidars.Count; i++)
            {
                var scan = l.Lidars[i].scanAVG != 0 ? $"{l.Lidars[i].scanAVG:G1}M\n" : "READY\n";
                grps += $"SCAN {l.Lidars[i].tag[1]} " + scan;
            }
            grps += $"TARGETS {Targets.Count:00} CTRL " + (!l.Manual ? "OFF" : "MAN");
            s.SetData(grps, 1);
            for (i = 0; i < l.Lidars.Count; ++i)
                s.SetColor(l.Lidars[i].Scans > 0 ? PMY : SDY, i + 2);
            return l.Name;
        }

        void GetTurretTgt(IMyLargeTurretBase t, bool arty = false)
        {
            MyDetectedEntityInfo info;
            if (t.HasTarget)
            {
                info = t.GetTargetedEntity();
                if (Targets.Exists(info.EntityId))
                    return;
                else if (PassTarget(info) && arty && (int)info.Type == 2) // if small, retarget
                {
                    t.ResetTargetingToDefault();
                    t.EnableIdleRotation = false;
                }
            }
        }

        void HandleIGC()
        {
            
        }
    }
}