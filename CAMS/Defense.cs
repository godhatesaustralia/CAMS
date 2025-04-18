using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        const int TUR_REST_T = 37, TGT_LOSS_T = 256, FIRE_T = 7, TRACK_REQ_C = 5;
        void ScrollLN(int p, int x, bool b, Screen s)
        {
            int i = b ? x : p;
            var ln = Launchers[ReloadRR.IDs[i]];
            var r = "";

            if (ln.Name == _lnSel)
            {
                s.Color(SDY, 0);
                r = "SLCTD";
            }
            else
            {
                s.Color(BKG, 0);
                r = $"{i + 1:00}/{ReloadRR.IDs.Length:00}";
            }

            s.Write($"{ln.Name}\nBLPRT\nWELDR", 1);
            s.Write(r + "\n" + ln.Parts, 2);

            r = $"{ln.NextUpdateF:X9}";
            foreach (var l in ln.Log)
                r += l;
            s.Write(r, 3);
            
            r = $"CLOCK";
            foreach (var t in ln.Time)
                r += t;
            s.Write(r, 4);

            i = b ? p : 0;
            b = ln.Total == 1;

            if (!b && i == ln.Total - 1) --i;

            var m = ln.Get(i);
            r = m.IDTG + m.Data() + "\n\n";

            if (b) r += "▮▮▮\n▮▮▮\n▮▮▮\n▮▮▮";
            else
            {
                m = ln.Get(i + 1);
                r += m.IDTG + m.Data();
            }

            s.Write(r, 6);
        }

        void EnterLN(int p, Screen s)
        {
            s.Index = p;
            s.Enter = FireLN;

            var ln = Launchers[ReloadRR.IDs[p]];
            _lnSel = ln.Name;
            s.Max = () => ln.Total;
        }

        void FireLN(int p, Screen s)
        {
            if (Targets.Count == 0) return;

            var t = Targets.Selected == -1 ? Targets.Prioritized.Min.EID : Targets.Selected;
            Launchers[_lnSel].Fire(t);
        }

        void BackLN(int p, Screen s)
        {
            s.Enter = EnterLN;
            s.Max = () => ReloadRR.IDs.Length;

            _lnSel = "";
        }

        void ScrollTR(int p, int x, bool b, Screen s)
        {
            int i = s.Sel ? s.Index : p;
            var t = Turrets[AssignRR.IDs[i]];
            var inf = "▮▮▮▮\n▮▮▮▮\n▮▮▮▮";
  
            s.Write(t.Name + "\nSTATE", 0);
            s.Write($"{i + 1:00}/{AssignRR.IDs.Length:00}\n{(t.Inoperable ? "OFFLN" : "FUNCT")}", 1);
            // if (t.Name == _lnSel)
            // {
            //     s.Color(SDY, 0);
            //     r = "SLCTD";
            // }
            // else
            // {
            //     s.Color(BKG, 0);
            //     r = $"{i + 1:00}/{ReloadRR.IDs.Length:00}";
            // }
            if (t.TEID != -1)
            {
                var tgt = Targets.Get(t.TEID);
                inf = tgt.eIDTag.Remove(0, 1) + $"\n{tgt.Velocity.Length():0000}\n{tgt.Radius:0000}";
            }
            s.Write(t.AZ + "\n" +t.EL, 3);
            //s.Write(n, 2);
            s.Write(inf + $"\n{t.BlockF:X4}\n{t.Guns:0000}\n{t.Speed:0000}\n{t.Range:0000}\n{t.TrackRange:0000}\n{t.ARPM:+000;-000}\n{t.ERPM:+000;-000}", 5);
        }

        void CommandFire(MyCommandLine b, long id)
        {
            if (Targets.Count == 0 || _launchCt > 0 || !Targets.Exists(id)) return;
            
            _fireID = id;
            bool spr = b.Argument(1) == "spread";

            if (!spr)
            {
                bool arg = Launchers.ContainsKey(b.Argument(1));
                if (!arg && !Launchers.ContainsKey(_lnSel)) return;

                _lnFire = arg ? b.Argument(1) : _lnSel;
               
                var l = Launchers[_lnFire];
                for (_launchCt = 0; ++_launchCt <= l.Total;)
                    if (l.Get(_launchCt - 1).Inoperable) break;

                if (_launchCt <= 1)
                {
                    _launchCt = 0;
                    _lnFire = "";
                    return;
                }
            }
            else if (int.TryParse(b.Argument(2), out _launchCt)) _lnFire = "";
            else return;

            _nxtFireF = F;

        }

        void UpdateRotorTurrets()
        {
            if (AssignRR == null) return;

            RotorTurret tur;

            #region assignment
            if (Targets.Count > 0)
            {
                bool pk = Targets.Prioritized.Min?.PriorityKill ?? false;
                if (!GlobalPriorityUpdateSwitch)
                {
                    Target temp = null;
                    GlobalPriorityUpdateSwitch = AssignRR.Next(ref Turrets, out tur);

                    if (tur.TEID != -1)
                    {
                        if (tur.UseLidar && !pk) goto CYCLE;
                        if (tur.CanTarget(tur.TEID) && (tur.Status & AimState.Blocked) == 0) goto CYCLE;
                    }

                    foreach (var tgt in Targets.Prioritized)
                    {
                        int type = (int)tgt.Type;
                        if (tur.CanTarget(tgt.EID))
                        {
                            temp = tgt;
                            if (!tgt.Engaged && ((tur.IsPDT && type == 2) || (!tur.IsPDT && type == 3 && tgt.Radius > 20)))
                                break;
                        }
                    }

                    if (temp != null)
                    {
                        if (tur.UseLidar) tur.UseLidar = false;

                        tur.TEID = temp.EID;
                    }
                }
                else if (Targets.Count > TRACK_REQ_C && !pk)
                    AssignPDTScan(Targets.Prioritized.Max);
            }

        #endregion

        CYCLE:
            int i = 0;
            if (MainRR != null) MainRR.Next(ref Turrets).UpdateTurret();

            if (PDTRR != null)
                for (; i++ <= MaxRotorTurretUpdates;)
                {
                    while (PDTRR.Next(ref Turrets, out tur) && tur.UseLidar) ;
                    tur.UpdateTurret();
                }
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
                            if (t.PriorityKill && !ekvTracks.Contains(t.EID))
                                ekvTracks.Add(t.EID);
                        }
                        else if (F > m.LastActiveF + TGT_LOSS_T)
                            m.Hold();
                        else mslCull.Add(m.MEID);
                    }
                    else
                    {
                        m.Update(t);
                        if (m.Inoperable) mslCull.Add(m.MEID);
                        //else if (m.DistToTarget < TRACK_D && !auxTracks.Contains(m.TEID))
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
            if (ReloadRR == null) return;

            var l = ReloadRR.Next(ref Launchers);
            if (F >= l.NextUpdateF && l.Status != RackState.Offline)
                l.NextUpdateF = F + l.Update();

            if (Targets.Count == 0)
                return;

            if (_launchCt > 0 && F >= _nxtFireF)
            {
                bool ok = false;

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

                    ok = r.Fire(_fireID);
                    if (ok)
                    {
                        _launchCt--;
                        _nxtFireF += FIRE_T;
                    }
                }
                
                if (!ok)
                {
                    _fireID = _launchCt = 0;
                    FireRR.Reset();
                }
            }

            Target t;
            PDT tur;
            for (int i = 0; i < MaxTgtKillTracks && i < Targets.Prioritized.Count; i++)
            {
                t = Targets.Prioritized.Min;
                if (t.PriorityKill && !ekvTracks.Contains(t.EID))
                {
                    foreach (var n in AMSNames)
                        if (Launchers[n].Fire(t.EID))
                        {
                            ekvTracks.Add(t.EID);
                            Targets.Prioritized.Remove(t);
                            break;
                        }
                }
            }
            if (ekvTracks.Count > 0 && PDTRR != null)
            {
                foreach (var n in PDTRR.IDs)
                {
                    tur = (PDT)Turrets[n];
                    var id = ekvTracks.FirstElement();

                    t = Targets.Get(id);
                    if ((tur.TEID == -1 || (tur.Status & AimState.Blocked) != 0) && tur.AssignLidarTarget(t))
                    {
                        ekvTracks.Remove(id);
                        break;
                    }
                }
            }
        }
    }
}