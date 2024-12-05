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
        Update,
        Added
    }

    public class Offset
    {
        public long Frame;
        public Vector3D Hit;
    }

    // =<MyDetectedEntityType>=
    // None = 0, Unknown = 1, 
    // Small = 2, Large = 3,
    // Human = 4, Other = 5
    // Object = 6, Asteroid = 7
    // Planet = 8, Meteor = 9
    // Missile = 10

    public class Target : IComparable<Target>, IEquatable<Target>
    {
        #region fields
        public readonly long EID, Source;
        public double Radius, Distance;
        public long Frame;
        public int Priority = -1;
        public MatrixD Matrix;
        public Vector3D Hit, Center, Velocity, LastVelocity, Accel;
        public List<Offset> HitPoints = new List<Offset>(5);
        public readonly MyDetectedEntityType Type;
        public readonly string eIDString, eIDTag;
        public bool 
        PriorityKill, Engaged;
        #endregion

        public Target(MyDetectedEntityInfo i, long f, long id, double dist)
        {
            EID = i.EntityId;
            eIDString = EID.ToString("X").Remove(0, 5);
            eIDTag = EID.ToString("X").Remove(0, 10);
            Source = id;
            Frame = f;
            Type = i.Type;
            Center = i.Position;
            Matrix = MatrixD.Transpose(i.Orientation);
            Velocity = i.Velocity;
            Radius = i.BoundingBox.Size.Length();
            Distance = dist;
            Hit = i.HitPosition ?? Center;
        }

        public Target(ref MyTuple<MyTuple<long, long, long, int>, MyTuple<Vector3D, Vector3D, MatrixD, double>> d)
        {
            EID = d.Item1.Item1;
            eIDString = EID.ToString("X").Remove(0, 5);
            eIDTag = EID.ToString("X").Remove(0, 11);
            Source = d.Item1.Item2;
            Frame = d.Item1.Item3;
            Type = (MyDetectedEntityType)d.Item1.Item4;
            Hit = Center = d.Item2.Item1;
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
                return Hit;

            return Hit + Velocity * dT + Accel * 0.5 * dT * dT;
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
        const int MAX_LIFE_SEC = 2, HIT_T = 23, OFS_T = 256; // s
        const SpriteType TXT = SpriteType.TEXT, SHP = SpriteType.TEXTURE;
        const string IgcTgt = "[FLT-TG]", N = "▮▮▮▮▮";
        public long Selected = -1;
        bool _showMsls;
        double _invD = INV_MAX_D;
        public string Log, SelTag;

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
                switch (b.Argument(1))
                {
                    case Lib.RD:
                        {
                            if (b.Argument(2) == "close")
                                _invD = 0.0004;
                            else if (b.Argument(2) == "reset")
                                _invD = INV_MAX_D;
                            else if (b.Argument(2) == Lib.ML)
                                _showMsls = !_showMsls;
                            break;
                        }
                    default:
                    case "clear":
                        {
                            Clear();
                            break;
                        }
                    case "reset":
                        {
                            UpdateBlacklist();
                            break;
                        }
                }
            });

            m.LCDScreens.Add(Lib.RD, new Screen(() => Count, _rdr, CreateRadar, null, null, false));
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
        }

        public void TargetData(int p, ref Screen s)
        {
            if (Count == 0)
            {
                s.Write($"CLEAR\n{N}\n{N}\n{N}\n{N}\n{N}\n{N}\n{N}\n{N}", 5);
                return;
            }

            var t = _targets[_iEIDs[p]];
            var r = t.EID == Selected ? "SLCTD" : $"{p + 1:00}/{Count:00}";
            var e = Lib.Projection(_p.Center - t.Center, _p.Controller.WorldMatrix.Up).Length();

            r += $"\n{t.eIDTag}\n{t.Distance:00000}\n{e:+0000;-0000}";
            r += $"\n{t.Velocity.Length():00000}\n{t.Accel.Length():00000}\n{t.Radius:0000}M\n{t.Priority:000}PT\n{t.HitPoints.Count} LOC";

            s.Write(r, 5);
        }

        public void TargetMode(int p, Screen s)
        {
            s.Max = () => Count;
            s.Enter = Enter;
        }

        void Enter(int p, Screen s)
        {
            Selected = _iEIDs[p];
            SelTag = _targets[Selected].eIDTag;
        }

        void CreateRadar(int p, int x, bool b, Screen s)
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
                var rPos = _p.Center - _targets[id].Center;
                _rdrBuffer.Add(DisplayTarget(ref rPos, ref mat, _targets[id].eIDTag, i == x));
            }

            if (_showMsls)
            {
                foreach (var m in _p.Missiles.Values)
                {
                    var mat = _p.Controller.WorldMatrix;
                    var id = m.IDTG;
                    var rPos = _p.Center - m.Controller.WorldMatrix.Translation;
                    _rdrBuffer.Add(DisplayTarget(ref rPos, ref mat, id, false, true));
                }

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
        MySprite DisplayTarget(ref Vector3D relPos, ref MatrixD mat, string eid, bool sel, bool msl = false)
        {
            // Get the Left, Forward, and Up vectors from the mat
            // Project the relative position onto the radar's plane defined by Left and Forward vectors
            double
                xProj = Vector3D.Dot(relPos, mat.Left),
                yProj = Vector3D.Dot(relPos, mat.Forward),

            // Calculate the screen position
            // Adjust for screen center
                scrX = R_SIDE_L * xProj * _invD + rdrCNR.X,
                scrY = R_SIDE_L * yProj * _invD + rdrCNR.Y;

            // clamp into a region that doesn't neatly correspond with screen size ( i have autism)
            scrX = MathHelper.Clamp(scrX, X_MIN, X_MAX);
            scrY = MathHelper.Clamp(scrY, Y_MIN, Y_MAX);

            // position vectors
            Vector2
                pos = Lib.V2((float)scrX, (float)scrY),
                txt = (sel ? 2.375F : 1.75f) * tgtSz;

            if (!msl) _rdrBuffer.Add(new MySprite(TXT, eid, scrX > 2 * X_MIN ? pos - txt : pos + 0.5f * txt, null, _p.PMY, Lib.V, rotation: 0.375f));
            else return new MySprite(TXT, "M", pos, null, _p.PMY, Lib.VB, rotation: 0.425f);
            return new MySprite(SHP, sel ? Lib.SQS : Lib.TRI, pos, (sel ? 1.5f : 1) * tgtSz, _p.PMY, null); // Center
        }
        #endregion

        public ScanResult AddOrUpdate(ref MyDetectedEntityInfo i, long src, bool hits = false)
        {
            var id = i.EntityId;
            var dist = (_p.Center - i.Position).Length();

            if (_targets.ContainsKey(id))
            {
                var t = _targets[id];
                var dT = (_p.F - t.Frame) * Lib.TPS;

                t.Frame = _p.F;
                t.Center = i.Position;
                t.Hit = i.HitPosition ?? t.Center;
                t.LastVelocity = t.Velocity;
                t.Velocity = i.Velocity;
                t.Matrix = MatrixD.Transpose(i.Orientation);
                t.Accel = Vector3D.Zero;
                t.Radius = i.BoundingBox.Size.Length();
                t.Distance = dist;
                
                var a = (t.Velocity - t.LastVelocity) / dT;
                if (a.LengthSquared() > 1)
                    t.Accel = (t.Accel * 0.25) + (a * 0.75);

                if (t.HitPoints.Count > 0)
                {
                    for (int j = t.HitPoints.Count - 1; j >= 0; j--)
                        if (_p.F - t.HitPoints[j].Frame > HIT_T)
                            t.HitPoints.RemoveAtFast(j);
                }

                SetPriority(t);

                return ScanResult.Update;
            }
            else
            {
                _targets[id] = new Target(i, _p.F, src, dist);
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
            var dir = _p.Center - tgt.Center;
            dir.Normalize();
            tgt.Priority = 0;
            bool uhoh = tgt.Velocity.Normalized().Dot(dir) > 0.9; // RUUUUUUUUUNNNNNNNNNNNNNN

            if (tgt.Radius > 20 || (int)tgt.Type != 2)
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
                if (Count == 0)
                    return;

                _rmvEIDs.Clear();

                foreach (var t in _targets.Values)
                    if (t.Elapsed(f) >= MAX_LIFE_SEC)
                        _rmvEIDs.Add(t.EID);

                foreach (var id in _rmvEIDs)
                {
                    _targets.Remove(id);
                    ScannedIDs.Remove(id);
                    _iEIDs.Remove(id);
                }
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

            Log = $"NX_TGT_PRI - {_nextPrioritySortF:X}";

            #region igc target sharing
            bool rcv = _p.ReceiveIGCTicks > 0;
            if (rcv && f >= _nextIGCCheck)
            {
                while (_TGT.HasPendingMessage)
                {
                    var m = _TGT.AcceptMessage();
                    if (m.Data is MyTuple<long, long>)
                    {
                        var d = (MyTuple<long, long>)m.Data;
                        var id = d.Item1;
                        _offsets[id] = f - (d.Item2 + 1);

                        if (!Blacklist.Contains(id))
                            Blacklist.Add(id);
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

            Log += $"\nNX_IGC_RCV - {_nextIGCCheck:X}";

            if (_p.SendIGCTicks > 0)
            {
                if (f >= _nextOffsetSend)
                {
                    _p.IGC.SendBroadcastMessage(IgcTgt, MyTuple.Create(ID, f));
                    _nextOffsetSend = f + OFS_T;
                }
                else if (f > _nextIGCSend && Count > 0)
                {
                    foreach (var tgt in _targets.Values)
                        _p.IGC.SendBroadcastMessage
                        (
                            IgcTgt, MyTuple.Create
                            (
                                MyTuple.Create(tgt.EID, tgt.Source, tgt.Frame, (int)tgt.Type),
                                MyTuple.Create(tgt.Center, tgt.Velocity, tgt.Matrix, tgt.Radius)
                            )
                        );
                    _nextIGCSend = f + _p.SendIGCTicks;
                }
            }

            Log += $"\nNX_IGC_SND - {_nextIGCSend:X}\nSYS_TGT_CT - {Count}\nSYS_OFS_CT - {_offsets.Count}";
            #endregion
        }
        public Target Get(long eid) => _targets.ContainsKey(eid) ? _targets[eid] : null;
        public bool Exists(long id) => _targets.ContainsKey(id);
        public bool AddHit(long id, long f, Vector3D h)
        {
            var t = _targets[id];
            if (t.HitPoints.Count < 5) t.HitPoints.Add(new Offset { Frame = f, Hit = h });
            return t.HitPoints.Count < 5;
        }

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