using Sandbox.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        const long TGT_LOSS_TK = 91, FIRE_TK = 7;
        void ScrollLN(int p, Screen s)
        {
            var ln = Launchers[ReloadRR.IDs[p]];
            s.Write(ln.Name, 0);
            string r = "";
            foreach (var l in ln.Log)
                r += l;
            s.Write(r, 1);
            s.Write(ln.Status.ToString().ToUpper(), 2);
            s.Color(p == 0 ? SDY : PMY, 6);
            s.Color(p == ReloadRR.IDs.Length - 1 ? SDY : PMY, 7);
            for (int i = 0; i < ln.Total; i++)
            ;
        }

        void EnterLN(int p, Screen s)
        {

        }

        void ScrollTR(int p, Screen s)
        {
            var t = Turrets[UpdateRR.IDs[p]];
            string n = t.Name;
            int ct = 12;
            ct -= t.Name.Length;

            for (; ct-- > 0;)
                n += " ";

            s.Write(n + $"{p + 1:00}/{UpdateRR.IDs.Length:00}", 0);

            n = "ST " + t.Status.ToString().ToUpper();
            ct = 17 - n.Length;
            for (; ct-- > 5;)
                n += " ";
            n += $">{t.TGT}";
            s.Write(t.AZ + "\n" + t.EL, 3);
            s.Write(n, 4);
            s.Write($"{(t.ActiveCTC ? "MANL" : "AUTO")}\n{t.Speed:0000}\n{t.Range:0000}\n{t.TrackRange:0000}\n{t.aRPM:0000}\n{t.eRPM:0000}", 6);

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
                    if (tur.tEID != -1 && tur.CanTarget(tur.tEID) && (tur.Status != AimState.Blocked || tur.Status != AimState.Resting))
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


        void UpdateMissileGuidance()
        {
            if (Missiles.Count == 0) return;

            foreach (var m in Missiles.Values)
            {
                if (m.NextUpdateF <= F)
                {
                    var t = Targets.Get(m.TEID);
                    if (t == null)
                    {
                        if (Targets.Count > 0)
                        {
                            t = Targets.Prioritized.Min;
                            m.TEID = t.EID;
                            if (t.PriorityKill)
                                ekvTargets.Add(t.EID);
                        }
                        else if (F - m.LastActiveF > TGT_LOSS_TK)
                            m.Hold();
                        else mslCull.Add(m.MEID);
                    }
                    else
                    {
                        m.Update(t);
                        if (m.Inoperable) mslCull.Add(m.MEID);
                    }
                }
                else if (m.NextStatusF <= F) m.CheckStatus();
            }

            foreach (var id in mslCull)
            {
                Missiles[id].Clear();
                if (mslReuse.Count < mslReuse.Capacity)
                    mslReuse.Add(Missiles[id]);
                Missiles.Remove(id);
            }
            mslCull.Clear();
        }

        void UpdateLaunchers()
        {
            var l = ReloadRR.Next(ref Launchers);
            if (F >= l.NextUpdateF && l.Status != RackState.Inoperable)
                l.NextUpdateF = F + l.Update();

            if (Targets.Count == 0)
                return;

            if (_launchCt > 0 && F >= _nxtFireF)
            {
                if (Targets.Exists(_fireID))
                {
                    var r = FireRR.Next(ref Launchers);

                    while (r.Auto || r.Status != RackState.Ready)
                        if (!FireRR.Next(ref Launchers, out r))
                            break;

                    if (r.Fire(Targets.Selected, ref Missiles))
                    {
                        _launchCt--;
                        _nxtFireF += FIRE_TK;
                    }
                }
                else 
                {
                    _launchCt = 0;
                    FireRR.Reset();
                }
            }

            Target t;
            PDT tur;
            for (int i = 0; i < MaxTgtKillTracks && i < Targets.Prioritized.Count; i++)
            {
                t = Targets.Prioritized.Min;
                if (t.PriorityKill && !ekvTargets.Contains(t.EID))
                {
                    foreach (var n in AMSNames)
                        if (Launchers[n].Fire(t.EID, ref Missiles))
                        {
                            ekvTargets.Add(t.EID);
                            Targets.Prioritized.Remove(t);
                            break;
                        }
                }
            }
            foreach (var id in ekvTargets)
                foreach (var n in PDTNames)
                {
                    tur = (PDT)Turrets[n];
                    t = Targets.Get(id);
                    if (tur.tEID == -1 && tur.AssignLidarTarget(t))
                        break;
                }
        }
    }
}