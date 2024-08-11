using System;
using System.Security.Cryptography;
using Sandbox.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        void LauncherScroll(int p, Screen s)
        {
            var ln = AMSLaunchers[p];
            s.SetData(ln.Name, 0);
            string r = ln.Report[0];
            for (int i = 1; i < 4; i++)
                r += $"\n{ln.Report[i]}";
            s.SetData(r, 1);
            s.SetData(ln.Status.ToString().ToUpper(), 2);
            s.SetColor(p == 0 ? SDY : PMY, 6);
            s.SetColor(p == MastNames.Length - 1 ? SDY : PMY, 7);
        }

        void TurretScroll(int p, Screen s)
        {
            var turret = Turrets[TurretNames[p]];
            string n = turret.Name, st = turret.Status.ToString().ToUpper();
            int ct = p >= 9 ? 12 : 13;
            ct -= turret.Name.Length;

            for (; ct-- > 0;)
                n += " ";
            ct = 17 - st.Length;
            for (; ct-- > 8;)
                st += " ";
            st += $"TGT {turret.TGT}";
            s.SetData(n + $"{p + 1}/{TurretCount}", 0);
            s.SetData(turret.AZ + "\n" + turret.EL, 2);
            s.SetData(st, 3);
        }

        void UpdateRotorTurrets()
        {
            if (Targets.Count > 0)
            {
                #region target assignment
                if (!GlobalPriorityUpdateSwitch)
                {
                    RotorTurret tur;
                    Target temp = null;
                    GlobalPriorityUpdateSwitch = AssignRR.Next(ref Turrets, out tur);
                    if (tur.tEID != -1 && tur.CanTarget(tur.tEID) && (tur.Status != AimState.Blocked || tur.Status != AimState.Rest))
                        return;

                    foreach (var tgt in Targets.Prioritized)
                    {
                        int type = (int)tgt.Type;
                        if (tur.CanTarget(tgt.EID))
                        {
                            temp = tgt;
                            if (!tgt.Engaged && ((tur.IsPDT && type == 2) || (!tur.IsPDT && type == 3)))
                                break;
                        }
                    }

                    if (temp != null)
                        tur.tEID = temp.EID;
                }
                #endregion
            }
            for (int i = 0; i++ < MaxRotorTurretUpdates;)
                UpdateRR.Next(ref Turrets).UpdateTurret();
        }

        void UpdateAMS()
        {
            while (IGC.UnicastListener.HasPendingMessage)
            {
                var m = IGC.UnicastListener.AcceptMessage();
                if (m.Data is long)
                {
                    foreach (var rk in AMSLaunchers)
                        if (rk.CheckHandshake((long)m.Data)) break;
                }

            }

            foreach (var rk in AMSLaunchers)
                if (F >= rk.NextUpdateF)
                    rk.NextUpdateF = F + rk.Update();

            if (Targets.Count == 0)
                return;

            Target t;
            for (int i = 0; i < MaxTgtKillTracks && i < Targets.Prioritized.Count; i++)
            {
                long ekv;
                t = Targets.Prioritized.Min;
                foreach (var rk in AMSLaunchers)
                    if (t.PriorityKill && !TargetsKillDict.ContainsKey(t.EID))
                    {
                        if (rk.Fire(out ekv))
                            TargetsKillDict.Add(t.EID, ekv);
                    }

                Targets.Prioritized.Remove(t);
            }

            if (TargetsKillDict.Count == 0)
                return;

            foreach (var tgt in TargetsKillDict.Keys)
            {
                t = Targets.Get(tgt);
                if (!Targets.Prioritized.Contains(t))
                    Targets.Prioritized.Add(t);
            }

            PDT tur;
            foreach (var id in TargetsKillDict.Keys)
                foreach (var n in PDTNames)
                {
                    tur = (PDT)Turrets[n];
                    if (tur.tEID == -1 && !tur.Inoperable)
                    {
                        tur.AssignLidarTarget(id);
                        break;
                    }
                }
        }
    }
}