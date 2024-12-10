﻿using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        const int TUR_REST_T = 37, TGT_LOSS_T = 91, FIRE_T = 7, TRACK_REQ_C = 5;
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
            int i = s.Sel ? s.Index : p, ct = 12;
            var t = Turrets[AssignRR.IDs[i]];
            var n = t.Name;

            ct -= t.Name.Length;

            for (; ct-- > 0;)
                n += " ";

            s.Write(n + $"{i + 1:00}/{AssignRR.IDs.Length:00}", 0);

            n = "ST " + t.Status.ToString().ToUpper();
            ct = 17 - n.Length;
            for (; ct-- > 5;)
                n += " ";

            n += $"{t.TGT}";
            s.Write(t.AZ + "\n" + t.EL, 3);
            s.Write(n, 4);
            s.Write($"{t.BlockF:X4}\n{t.Speed:0000}\n{t.Range:0000}\n{t.TrackRange:0000}\n{t.ARPM:+000;-000}\n{t.ERPM:+000;-000}", 6);

        }

        // this sucks lol
        void CommandFire(MyCommandLine b)
        {
            if (Targets.Count == 0 || _launchCt > 0) return;

            bool spr = b.Argument(0) == "spread", pri = b.Argument(1) == P;

            _lnFire = spr ? "" : (!pri && b.Argument(1) == "sel" ? _lnSel ?? "" : b.Argument(1));

            if (spr && !pri && b.Argument(2) != P)
            {
                _fireID = Targets.Selected;
                if (!Targets.Exists(_fireID)) return;
            }
            else _fireID = Targets.Prioritized.Min.EID;

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

        public bool AddLidarTrack(Target t)
        {
            if (PDTRR == null) return false;

            if (auxTracks.Contains(t.EID))
                return true;

            foreach (var k in PDTRR.IDs)
            {
                var tur = (PDT)Turrets[k];
                if ((tur.TEID == -1 || (tur.Status & AimState.Blocked) != 0 && tur.BlockF > TUR_REST_T) && tur.AssignLidarTarget(t, true))
                {
                    auxTracks.Add(t.EID);
                    return true;
                }
            }
            return false;
        }

        string tu;
        void UpdateRotorTurrets()
        {
            if (AssignRR == null) return;

            RotorTurret tur;

            #region assignment
            if (Targets.Count > 0)
            {
                if (!GlobalPriorityUpdateSwitch)
                {
                    Target temp = null;
                    GlobalPriorityUpdateSwitch = AssignRR.Next(ref Turrets, out tur);

                    if (tur.TEID != -1)
                    {
                        bool ok = tur.UseLidar || tur.CanTarget(tur.TEID);
                        if (ok || (tur.Status & AimState.Blocked) == 0) goto CYCLE;

                        //else if (!ok || tur.BlockF > TUR_REST_T) tur.TEID = -1;
                    }

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
                        tur.TEID = temp.EID;
                }
                else if (Targets.Count > TRACK_REQ_C && !Targets.Prioritized.Min.PriorityKill)  
                    AddLidarTrack(Targets.Prioritized.Max);
            }

        #endregion

        CYCLE:
            int i = 0;
            if (MainRR != null)
            {
                MainRR.Next(ref Turrets).UpdateTurret();
            }

            if (PDTRR != null)
                for (; i++ <= MaxRotorTurretUpdates;)
                    PDTRR.Next(ref Turrets).UpdateTurret();
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
                        _nxtFireF += FIRE_T;
                    }
                }
                else
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
                    var id = ekvTracks.GetEnumerator().Current;
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