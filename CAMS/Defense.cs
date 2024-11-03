using Sandbox.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        void ScrollLN(int p, Screen s)
        {
            var ln = Launchers[ReloadRR.IDs[p]];
            s.Write(ln.Name, 0);
            string r = ln.Report[0];
            for (int i = 1; i < ln.Report.Length; i++)
                r += $"\n{ln.Report[i]}";
            s.Write(r, 1);
            //s.Write(ln.Status.ToString().ToUpper(), 2);
            s.Color(p == 0 ? SDY : PMY, 6);
            s.Color(p == ReloadRR.IDs.Length - 1 ? SDY : PMY, 7);
        }

        void ScrollTR(int p, Screen s)
        {
            var turret = Turrets[UpdateRR.IDs[p]];
            string n = turret.Name, st = turret.Status.ToString().ToUpper();
            int ct = p >= 9 ? 12 : 13;
            ct -= turret.Name.Length;

            for (; ct-- > 0;)
                n += " ";
            ct = 17 - st.Length;
            for (; ct-- > 8;)
                st += " ";
            st += $"TGT {turret.TGT}";
            s.Write(n + $"{p + 1}/{UpdateRR.IDs.Length}", 0);
            s.Write(turret.AZ + "\n" + turret.EL, 2);
            s.Write(st, 3);
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

        const long TGT_LOSS_TK = 32;
        void UpdateMissileGuidance()
        {
            
            foreach (var m in Missiles.Values)
            {
                if (m.NextUpdateF <= F)
                {
                    var t = Targets.Get(m.TEID);
                    if (t == null)
                    {
                        if (F - m.NextUpdateF < TGT_LOSS_TK)
                            continue;
                        if (Targets.Count > 0)
                            m.TEID = Targets.Prioritized.Min.EID;
                        else mslCull.Add(m.MEID);
                    }
                    else
                    {
                        m.Update(t);
                    }
                }
                else if (m.NextStatusF <= F && m.Inoperable())
                    mslCull.Add(m.MEID);
            }

            foreach (var id in mslCull)
            {
                Missiles[id].Kill();
                Missiles.Remove(id);
            }
            mslCull.Clear();
        }

        void UpdateLaunchers()
        {
            foreach (var l in Launchers.Values)
                if (F >= l.NextUpdateF && l.Status != RackState.Inoperable)
                    l.NextUpdateF = F + l.Update();

            if (Targets.Count == 0)
                return;

            Target t;
            for (int i = 0; i < MaxTgtKillTracks && i < Targets.Prioritized.Count; i++)
            {
                t = Targets.Prioritized.Min;
                foreach (var n in AMSNames)
                    if (t.PriorityKill && Launchers[n].Fire(t.EID, ref Missiles))
                        Targets.Prioritized.Remove(t);            
            }

            PDT tur;
            foreach (var id in ekvTargets)
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