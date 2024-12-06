using Sandbox.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        void ScrollMS(int p, int x, bool b, Screen s)
        {
            int i = b ? x : p;
            var l = Masts[MastNames[i]];
            var g = $"{i + 1:0}/{MastNames.Length:0}"; 

            g += l.RPM;
            s.Write(g, 1);
            
            g = "SCAN";
            for (i = 0; i < l.Lidars.Length; i++)
                g += $"\nSD-{l.Lidars[i].Tag[1]}";
            s.Write(g, 2);

            g = l.TGT;
            for (i = 0; i < l.Lidars.Length; i++)
                g += l.Lidars[i].ScanAVG != 0 ? $"\n{l.Lidars[i].ScanAVG:0000E00}M" : "\nCHARGING";
            s.Write(g, 3);
            s.Write($"{l.Name}\nRPM-AZ\nRPM-EL", 0);

            i = b ? p : 0;
            Targets.TargetData(i, ref s);
        }

        void BackMS(int p, Screen s)
        {
            Targets.Selected = -1;
            s.Enter = Targets.TargetMode;
            Targets.SelTag = "";
            s.Max = () => MastNames.Length;
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