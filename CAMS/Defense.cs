using Sandbox.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
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
                    if (tur.tEID != -1 && tur.CanTarget(tur.tEID))
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
                }
                #endregion

                for (int i = 0; i < MaxRotorTurretUpdates; i++)
                    UpdateRR.Next(ref Turrets).UpdateTurret();
            }
        }

        void UpdateAMS()
        {
            while (Datalink.MissileReady.HasPendingMessage)
            {
                var m = Datalink.MissileReady.AcceptMessage();
                if (m.Data is long)
                    foreach (var rk in AMSLaunchers)
                        if (rk.CheckHandshake((long)m.Data)) break;
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