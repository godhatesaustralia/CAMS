using System;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        const int TGT_LOSS_TK = 91, FIRE_TK = 7;
        void ScrollLN(int p, Screen s)
        {
            int i = s.Sel ? s.Index : p;
            var ln = Launchers[ReloadRR.IDs[i]];
            s.Write($"{ln.Name}\nBLPRT\nWELDR", 0);
            s.Write($"{i + 1:00}/{ReloadRR.IDs.Length:00}\n" + ln.Parts, 1);

            var r = $"{ln.NextUpdateF:X9}";
            foreach (var l in ln.Log)
                r += l;
            s.Write(r, 2);
            r = $"FRAME";
            foreach (var t in ln.Time)
                r += t;
            s.Write(r, 3);
            
            i = s.Sel ? p : 0;
            var m = ln.Get(i);
            s.Write($"{i + 1}/{ln.Total}\n{(ln.Auto ? "AMS" : "DEF")}\n", 5);
            
            s.Write(m.IDTG + m.Status(), 7);

        }

        void EnterLN(int p, Screen s)
        {
            s.Sel = true;
            s.Index = p;
            var ln = Launchers[ReloadRR.IDs[p]];
            _lnSel = ln.Name;
            
            s.Max = () => ln.Total;
        }

        void BackLN(int p, Screen s)
        {
            s.Sel = false;
            _lnSel = "";
            s.Max = () => ReloadRR.IDs.Length;
        }

        void ScrollTR(int p, Screen s)
        {
            int i = s.Sel ? s.Index : p;
            var t = Turrets[UpdateRR.IDs[i]];
            var n = t.Name;
            int ct = 12;
            ct -= t.Name.Length;

            for (; ct-- > 0;)
                n += " ";

            s.Write(n + $"{i + 1:00}/{UpdateRR.IDs.Length:00}", 0);

            n = "ST " + t.Status.ToString().ToUpper();
            ct = 17 - n.Length;
            for (; ct-- > 5;)
                n += " ";
            n += $">{t.TGT}";
            s.Write(t.AZ + "\n" + t.EL, 3);
            s.Write(n, 4);
            s.Write($"{(t.ActiveCTC ? "MANL" : "AUTO")}\n{t.Speed:0000}\n{t.Range:0000}\n{t.TrackRange:0000}\n{t.aRPM:0000}\n{t.eRPM:0000}", 6);

        }

        // this sucks lol
        void CommandFire(MyCommandLine b)
        {
            if (Targets.Count == 0 || _launchCt > 0) return;

            bool spr = b.Argument(0) == "spread", sel = b.Argument(1) == P;

            _lnFire = spr ? "" : (sel ? _lnSel : b.Argument(1));
            _fireID = !sel && (b?.Argument(2) ?? "") == P && Targets.Exists(Targets.Selected) ? Targets.Selected : Targets.Prioritized.Min.EID;

            if (spr && int.TryParse(b.Argument(2), out _launchCt))
                _nxtFireF = F;

            if (!Launchers.ContainsKey(_lnFire)) 
            {
                _lnFire = "";
                return;
            }

            _launchCt = Launchers[_lnFire].Total;
            _nxtFireF = F;
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
                        else if (F > m.LastActiveF + TGT_LOSS_TK)
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
            if (F >= l.NextUpdateF && l.Status != RackState.Offline)
                l.NextUpdateF = F + l.Update();

            if (Targets.Count == 0)
                return;

            if (_launchCt > 0 && F >= _nxtFireF)
            {
                if (Targets.Exists(_fireID))
                {
                    Launcher r;
                    if (_lnFire != "")
                        r = Launchers[_lnFire];
                    else
                    {
                        r = FireRR.Next(ref Launchers);

                        while (r.Auto || r.Status != RackState.Ready)
                            if (!FireRR.Next(ref Launchers, out r))
                                break;
                    }

                    if (r.Fire(_fireID))
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
                        if (Launchers[n].Fire(t.EID))
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