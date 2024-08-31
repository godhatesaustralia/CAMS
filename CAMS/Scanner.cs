using System;
using Sandbox.ModAPI.Ingame;
using VRage;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        void ScrollMS(int p, Screen s)
        {
            string grps = ""; int i = 0;
            var l = Masts[MastNames[p]];
            for (; i < l.Lidars.Count; i++)
            {
                var scan = l.Lidars[i].scanAVG != 0 ? $"{l.Lidars[i].scanAVG:G1}M\n" : "READY\n";
                grps += $"{l.Lidars[i].tag[1]} " + scan;
            }

            grps += $">{(!l.Manual ? "DETECT" : "MANUAL")}  SN {l.Scans:00}";
            s.Write(MastNames[p], 0);
            s.Write(grps, 1);
            s.Color(p == 0 ? SDY : PMY, 6);
            s.Color(p == MastNames.Length - 1 ? SDY : PMY, 7);
            s.Write($"A/E RPM: {l.aRPM:00}/{l.eRPM:00}", 9);
            s.Write(Targets.Log, 8);

            for (i = 0; i < l.Lidars.Count; i++)
                s.Color(l.Lidars[i].Scans > 0 ? PMY : SDY, i + 2);
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
    }
}