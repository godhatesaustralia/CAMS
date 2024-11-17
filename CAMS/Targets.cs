using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    public enum ScanResult
    {
        Failed,
        Hit,
        Update,
        Added = Hit | Update
    }

    public class Target : IComparable<Target>, IEquatable<Target>
    {
        #region fields
        public readonly long EID, Source;
        public double Radius, Distance;
        public long Frame;
        public int Priority = -1;
        public MatrixD Matrix;
        public Vector3D Position, Velocity, LastVelocity, Accel;
        public readonly MyDetectedEntityType Type;
        public readonly string eIDString, eIDTag;
        public bool PriorityKill, Engaged;
        #endregion

        public Target(MyDetectedEntityInfo i, long f, long id, double dist)
        {
            EID = i.EntityId;
            eIDString = EID.ToString("X").Remove(0, 5);
            eIDTag = EID.ToString("X").Remove(0, 11);
            Source = id;
            Type = i.Type;
            Position = i.Position;
            Matrix = i.Orientation;
            Velocity = i.Velocity;
            Radius = i.BoundingBox.Size.Length();
            Distance = dist;
        }

        public Target(ref MyTuple<MyTuple<long, long, long, int>, MyTuple<Vector3D, Vector3D, MatrixD, double>> d)
        {
            EID = d.Item1.Item1;
            eIDString = EID.ToString("X").Remove(0, 5);
            eIDTag = EID.ToString("X").Remove(0, 11);
            Source = d.Item1.Item2;
            Frame = d.Item1.Item3;
            Type = (MyDetectedEntityType)d.Item1.Item4;
            Position = d.Item2.Item1;
            Matrix = d.Item2.Item3;
            Velocity = d.Item2.Item2;
            Radius = d.Item2.Item4;
        }

        public double Elapsed(long f) => Lib.TPS * Math.Max(f - Frame, 1);

        public Vector3D AdjustedPosition(long f, long offset = 0)
        {
            f += offset;
            var dT = Elapsed(f);
            if (Velocity.Length() < 0.5 && Accel.Length() <= 1)
                return Position;

            return Position + Velocity * dT + Accel * 0.5 * dT * dT;
        }

        public int CompareTo(Target t) => t.Priority <= Priority ? -1 : 1;
        public bool Equals(Target t) => EID == t.EID;
    }

    public class TargetProvider
    {
        Program _p;
        IMyBroadcastListener _TGT;

        long ID, _nextPrioritySortF = 0, _nextIGCCheck = 0, _nextIGCSend = 0, _nextOffsetSend = 0;
        const double INV_MAX_D = 0.0001, R_SIDE_L = 154;
        const int MAX_LIFETIME = 2, OFS_TMIT = 256; // s
        const string IgcTgt = "[FLT-TG]", DSB = "\n\n>>DISABLED";
        public long Selected = -1;
        string selTag;
        public string Log { get; private set; }

        public int Count => _iEIDs.Count;   
        public SortedSet<Target> Prioritized = new SortedSet<Target>();
        List<long> _rmvEIDs = new List<long>(), _iEIDs = new List<long>();
        Dictionary<long, Target> _targets = new Dictionary<long, Target>();
        Dictionary<long, long> _offsets = new Dictionary<long, long>();
        public HashSet<long>
            ScannedIDs = new HashSet<long>(),
            Blacklist = new HashSet<long>();

        const float rad = (float)Math.PI / 180, X_MIN = 28, X_MAX = 484, Y_MIN = 28, Y_MAX = 484; // radar
        static Vector2 rdrCNR = Lib.V2(256, 256), tgtSz = Lib.V2(12, 12), env = Lib.V2(91.2f, 91.2f);
        List<MySprite> _rdrBuffer = new List<MySprite>();
        // reused sprites
        MySprite[] _rdr;

        public TargetProvider(Program m)
        {
            _p = m;
            ID = Program.ID;
            _TGT = m.IGC.RegisterBroadcastListener(IgcTgt);

            var sp = Program.SHP;
            _rdr = new MySprite[]
            {
                new MySprite(sp, Lib.SQS, rdrCNR, Lib.V2(90, 90), m.SDY),
                new MySprite(sp, Lib.SQS, rdrCNR, Lib.V2(4, 712), m.PMY, rotation: rad * -45),
                new MySprite(sp, Lib.SQS, rdrCNR, Lib.V2(4, 712), m.PMY, rotation: rad * 45),
                new MySprite(sp, Lib.SQH, rdrCNR, env, m.PMY),
                new MySprite(sp, Lib.SQH, rdrCNR, Lib.V2(512, 512), m.PMY),
            };

            UpdateBlacklist();
            m.Commands.Add(Lib.TG, b =>
            {
                if (b.ArgumentCount != 2)
                    return;
                switch (b.Argument(1))
                {
                    default:
                    case "clear":
                        {
                            Clear();
                            break;
                        }
                    case "reset_blacklist":
                        {
                            UpdateBlacklist();
                            break;
                        }
                }
            });

            m.LCDScreens.Add(Lib.RD, new Screen(() => Count, null, CreateRadar, null, null, false));
        }

        void UpdateBlacklist()
        {
            Blacklist.Clear();
            Blacklist.Add(_p.Me.CubeGrid.EntityId);
            _p.Terminal.GetBlocksOfType<IMyMotorStator>(null, b =>
            {
                if (b.TopGrid == null) return false;

                var i = b.TopGrid.EntityId;
                if (!Blacklist.Contains(i))
                    Blacklist.Add(i);
                return true;
            });
        }

        #region display
        public void UpdateRadarSettings(Program m)
        {
            _p = m;
            _rdr[0].Color = m.SDY;
            for (int i = 1; i < _rdr.Length; i++)
                _rdr[i].Color = m.PMY;

            m.CtrlScreens[Lib.TG] = new Screen(
              () => Count, new MySprite[]
              {
                new MySprite(Program.TXT, "", Lib.V2(24, 112), null, m.PMY, Lib.VB, 0, 1.5f),
                new MySprite(Program.TXT, "", Lib.V2(24, 192), null, m.PMY, Lib.VB, 0, 0.8195f),
              },
            List, Select, Deselect);
        }

        void List(int p, Screen s)
        {
            if (Count == 0)
            {
                s.Write("NO TARGET", 0);
                s.Write("SWITCH TO MASTS SCR\nFOR TGT ACQUISITION", 1);
                return;
            }

            var ty = "";
            var t = _targets[_iEIDs[p]];
            s.Write($"{t.eIDString}", 0);

            if ((int)t.Type == 3)
                ty = "LARGE GRID";
            else if ((int)t.Type == 2)
                ty = "SMALL GRID";

            s.Write($"DIST {t.Distance / 1000:F2} KM\nASPD {t.Velocity.Length():F0} M/S\nACCL {t.Accel.Length():F0} M/S\nSIZE {ty}\n{(Selected == -1 ? "NO TGT SELECTED" : "TGT " + selTag + " LOCKED")}", 1);
        }

        void Select(int p, Screen s)
        {
            Selected = _iEIDs[p];
            selTag = _targets[Selected].eIDTag;
            s.Data(p, s);
        }

        void Deselect(int p, Screen s)
        {
            Selected = -1;
            selTag = "";
            s.Data(p, s);
        }

        void CreateRadar(int p, Screen s)
        {
            if (Count == 0)
            {
                s.Sprites = _rdr;
                return;
            }

            int i = 0;
            _rdrBuffer.Clear();
            for (; i < Count; i++)
            {
                var mat = _p.Controller.WorldMatrix;
                var id = _iEIDs[i];
                var rPos = _p.Center - _targets[id].Position;
                _rdrBuffer.Add(DisplayTarget(ref rPos, ref mat, _targets[id].eIDTag, id == Selected));
            }

            int l = _rdrBuffer.Count + _rdr.Length;
            if (l != s.Sprites.Length)
            {
                s.Sprites = null;
                s.Sprites = new MySprite[l];
            }

            for (i = 0; i < l; i++)
            {
                if (i < _rdr.Length)
                    s.Sprites[i] = _rdr[i];
                else s.Sprites[i] = _rdrBuffer[i - _rdr.Length];
            }
        }

        /// <summary>
        /// Draw a Target Icon on screen
        /// </summary>
        /// <param name="relPos">relative position</param>
        /// <param name="mat">World Matrix of the reference</param>
        /// <param name="sel">if currently selected</param>
        MySprite DisplayTarget(ref Vector3D relPos, ref MatrixD mat, string eid, bool sel)
        {
            // Get the Left, Forward, and Up vectors from the mat
            // Project the relative position onto the radar's plane defined by Left and Forward vectors
            double
                xProj = Vector3D.Dot(relPos, mat.Left),
                yProj = Vector3D.Dot(relPos, mat.Forward),

            // Calculate the screen position
            // Adjust for screen center
                scrX = R_SIDE_L * xProj * INV_MAX_D + rdrCNR.X,
                scrY = R_SIDE_L * yProj * INV_MAX_D + rdrCNR.Y;

            // clamp into a region that doesn't neatly correspond with screen size ( i have autism)
            scrX = MathHelper.Clamp(scrX, X_MIN, X_MAX);
            scrY = MathHelper.Clamp(scrY, Y_MIN, Y_MAX);

            // position vectors
            Vector2
                pos = Lib.V2((float)scrX, (float)scrY),
                txt = (sel ? 2.375F : 1.75f) * tgtSz;

            _rdrBuffer.Add(new MySprite(Program.TXT, eid, scrX > 2 * X_MIN ? pos - txt : pos + 0.5f * txt, null, _p.PMY, Lib.V, rotation: 0.375f));
            return new MySprite(Program.SHP, sel ? Lib.SQS : Lib.TRI, pos, (sel ? 1.5f : 1) * tgtSz, _p.PMY, null); // Center
        }
        #endregion

        public ScanResult AddOrUpdate(ref MyDetectedEntityInfo i, long src, bool hits = false)
        {
            var id = i.EntityId;
            Target t;
            if (_targets.ContainsKey(id))
            {
                t = _targets[id];
                var dT = t.Elapsed(_p.F);
                t.Frame = _p.F;
                t.Position = i.Position;
                t.LastVelocity = t.Velocity;
                t.Velocity = i.Velocity;
                t.Matrix = i.Orientation;

                t.Accel = Vector3D.Zero;
                var a = (t.Velocity - t.LastVelocity) * dT;
                if (a.LengthSquared() > 1)
                    t.Accel = (t.Accel * 0.25) + (a * 0.75);

                t.Radius = i.BoundingBox.Size.Length();
                t.Distance = (_p.Center - i.Position).Length();

                SetPriority(t);

                return ScanResult.Update;
            }
            else
            {
                _targets[id] = new Target(i, _p.F, src, (_p.Center - i.Position).Length());
                _iEIDs.Add(id);

                SetPriority(_targets[id]);

                return ScanResult.Added;
            }
        }

        /// <summary>
        /// sets target priority uhhhhh
        /// </summary>
        void SetPriority(Target tgt)
        {
            var dir = _p.Center - tgt.Position;
            dir.Normalize();
            tgt.Priority = 0;
            bool uhoh = tgt.Velocity.Normalized().Dot(dir) > 0.9; // RUUUUUUUUUNNNNNNNNNNNNNN

            if (tgt.Radius > 15 || (int)tgt.Type != 2)
                tgt.Priority += uhoh ? 75 : 300;
            else if (uhoh)
            {
                tgt.Priority -= (int)Math.Round(tgt.Distance);
                tgt.PriorityKill = true;
                if (!tgt.Engaged)
                    tgt.Priority -= 100;
            }

            if (tgt.Distance > 2E3)
                tgt.Priority += 500;
            else if (tgt.Distance < 8E2)
                tgt.Priority -= uhoh ? 500 : 400;
        }

        public List<Target> AllTargets()
        {
            var list = new List<Target>(Count);
            foreach (var t in _targets.Values)
                list.Add(t);

            return list;
        }

        public void MarkEngaged(long eid)
        {
            if (_targets.ContainsKey(eid))
                _targets[eid].Engaged = true;
        }

        public void MarkLost(long eid)
        {
            if (_targets.ContainsKey(eid))
                _targets[eid].Engaged = false;
        }

        public void Update(UpdateType u, long f)
        {
            ScannedIDs.Clear();
            if ((u & Lib.u100) != 0)
            {
                foreach (var k in _targets.Keys)
                    _rmvEIDs.Add(k);

                if (Count == 0)
                    return;

                for (int i = Count - 1; i >= 0; i--)
                    if (_targets[_rmvEIDs[i]].Elapsed(f) >= MAX_LIFETIME)
                    {
                        ScannedIDs.Remove(_rmvEIDs[i]);
                        _targets.Remove(_rmvEIDs[i]);
                        _iEIDs.Remove(_rmvEIDs[i]);
                    }

                _rmvEIDs.Clear();
            }
            else if (_p.GlobalPriorityUpdateSwitch && f >= _nextPrioritySortF)
            {
                Prioritized.Clear();
                foreach (var t in _targets.Values)
                {
                    Prioritized.Add(t);
                }

                _nextPrioritySortF = f + _p.PriorityCheckTicks;
                _p.GlobalPriorityUpdateSwitch = false;
            }
            Log = $"\n>>{_nextPrioritySortF:X8}";

            #region igc target sharing
            bool rcv = _p.ReceiveIGCTicks > 0;
            if (rcv)
            {
                if (f >= _nextIGCCheck)
                {
                    while (_TGT.HasPendingMessage)
                    {
                        var m = _TGT.AcceptMessage();
                        if (m.Data is MyTuple<long, long>)
                        {
                            var d = (MyTuple<long, long>)m.Data;
                            _offsets[d.Item1] = f - (d.Item2 + 1);
                        }
                        else if (m.Data is MyTuple<MyTuple<long, long, long, int>, MyTuple<Vector3D, Vector3D, MatrixD, double>>)
                        {
                            var d = (MyTuple<MyTuple<long, long, long, int>, MyTuple<Vector3D, Vector3D, MatrixD, double>>)m.Data;
                            if (!_targets.ContainsKey(d.Item1.Item1) || _targets[d.Item1.Item1].Source != ID)
                                _targets[d.Item1.Item1] = new Target(ref d);
                        }
                    }
                    _nextIGCCheck = f + _p.ReceiveIGCTicks;
                }
                Log += $"\n\n>>{_nextIGCCheck:X8}";
            }
            else Log += DSB;

            if (_p.SendIGCTicks > 0)
            {
                if (f >= _nextOffsetSend)
                {
                    _p.IGC.SendBroadcastMessage(IgcTgt, MyTuple.Create(ID, f));
                    _nextOffsetSend = f + OFS_TMIT;
                }
                else if (f > _nextIGCSend && Count > 0)
                {
                    foreach (var tgt in _targets.Values)
                        _p.IGC.SendBroadcastMessage
                        (
                            IgcTgt, MyTuple.Create
                            (
                                MyTuple.Create(tgt.EID, tgt.Source, tgt.Frame, (int)tgt.Type),
                                MyTuple.Create(tgt.Position, tgt.Velocity, tgt.Matrix, tgt.Radius)
                            )
                        );
                    _nextIGCSend = f + _p.SendIGCTicks;
                }

                Log += $"\n\n>>{_nextIGCSend:X8}";
            }
            else Log += DSB;
            #endregion

            Log += $"\n\n>>{Count:00000000}";
            Log += rcv ? $"\n\n>>{_offsets.Count:00000000}" : DSB;
        }

        public Target Get(long eid) => _targets.ContainsKey(eid) ? _targets[eid] : null;
        public bool Exists(long id) => _targets.ContainsKey(id);

        public void Clear()
        {
            _rmvEIDs.Clear();
            _offsets.Clear();
            _targets.Clear();
            ScannedIDs.Clear();
            _iEIDs.Clear();
        }
    }
}