using Sandbox.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        void ScrollMS(int p, Screen s)
        {
            var g = "SCAN"; int i = 0;
            var l = Masts[MastNames[p]];
            
            for (; i < l.Lidars.Count; i++)
                g += $"\nSD-{l.Lidars[i].tag[1]}";
            s.Write(g, 2);

            g = l.TGT;
            for (i = 0; i < l.Lidars.Count; i++)
                g += l.Lidars[i].scanAVG != 0 ? $"\n{l.Lidars[i].scanAVG:0000E00}M" : "\nCHARGING";
            s.Write(g, 3);
            s.Write($"{MastNames[p]}\nRPM-AZ\nRPM-EL", 0);

            g = l.Manual ? "CTC" : "SPN";
            g += l.RPM;
            s.Write(g, 1);
        }

        void GetTurretTgt(IMyLargeTurretBase t, bool arty = false)
        {
            MyDetectedEntityInfo info;
            if (t.HasTarget)
            {
                info = t.GetTargetedEntity();
                if (info.IsEmpty() || Targets.Exists(info.EntityId))
                    return;
                else if (PassTarget(info) && arty && (int)info.Type == 2) // if small, retarget
                {
                    t.ResetTargetingToDefault();
                    t.EnableIdleRotation = false;
                }
            }
        }
    }
}