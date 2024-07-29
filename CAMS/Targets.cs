using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using VRage;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame.Utilities;
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
        public readonly MyRelationsBetweenPlayerAndBlock IFF; // wham
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
            IFF = i.Relationship;
            Position = i.Position;
            // var p = i.HitPosition;
            Matrix = i.Orientation;
            Velocity = i.Velocity;
            Radius = i.BoundingBox.Size.Length();
            Distance = dist;
        }

        public Target(MyTuple<MyTuple<long, long, long, int>, MyTuple<Vector3D, Vector3D, MatrixD, double>> data)
        {
            EID = data.Item1.Item1;
            Source = data.Item1.Item2;
            Frame = data.Item1.Item3;
            Type = (MyDetectedEntityType)data.Item1.Item4;
            Position = data.Item2.Item1;
            Matrix = data.Item2.Item3;
            Velocity = data.Item2.Item2;
            Radius = data.Item2.Item4;
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
        public bool Equals(Target t)
        {
            return EID == t.EID;
        }
    }

    public class TargetProvider
    {
        Program _p;
        long _nextPrioritySortF = 0;
        const double INV_MAX_D = 0.0001, R_SIDE_L = 154;
        const int MAX_LIFETIME = 2; // s
        public int Count => _iEIDs.Count;
        public SortedSet<Target> Prioritized = new SortedSet<Target>();
        Dictionary<long, Target> _targets = new Dictionary<long, Target>();

        List<long> _rmvEIDs = new List<long>(), _iEIDs = new List<long>();
        const float rad = (float)Math.PI / 180, X_MIN = 28, X_MAX = 308, Y_MIN = 96, Y_MAX = 362; // radar
        static Vector2 rdrCNR = Lib.V2(168, 228), tgtSz = Lib.V2(12, 12);
        List<MySprite> _rdrBuffer = new List<MySprite>();
        string[] _rdrData = new string[14];
        // reused sprites
        MySprite[] _rdrStatic;


        public HashSet<long>
            ScannedIDs = new HashSet<long>(),
            Blacklist = new HashSet<long>();

        public TargetProvider(Program m)
        {
            _p = m;
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

            // _rdrStatic[4]= new MySprite(Lib.TXT, "", Lib.V2(328, 084), null, Lib.GRN, m.Based ? "VCR" : "White", Lib.LFT, m.Based ? .425f : .8f);
            m.LCDScreens.Add("radar", new Screen(() => Count, null, (p, s) => CreateRadar(p, s)));
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

        #region radar

        public void UpdateRadarSettings(Program m)
        {
            _p = m;
            _rdrStatic = new MySprite[]
            {
                new MySprite(Program.SHP, Lib.SQS, rdrCNR, Lib.V2(12, 12), m.SDY, rotation: rad * 45),
                new MySprite(Program.SHP, Lib.SQS, rdrCNR, Lib.V2(4, 428), m.SDY, rotation: rad * -45),
                new MySprite(Program.SHP, Lib.SQS, rdrCNR, Lib.V2(4, 428), m.SDY, rotation: rad * 45),
                new MySprite(Program.SHP, Lib.SQS, Lib.V2(320, 228), Lib.V2(8, 308), m.PMY),
                new MySprite(Program.TXT, "", Lib.V2(328, 84), null, m.PMY, Lib.V, Lib.LFT, !m.Based ? .425f : .8f),
                new MySprite(Program.SHP, Lib.SQH, rdrCNR, Lib.V2(61.6f, 61.6f), m.PMY)
            };

            if (m.CtrlScreens.Remove(Lib.TG))
                m.CtrlScreens.Add(Lib.TG, new Screen(
                  () => Count, new MySprite[]
                  {
                    new MySprite(Program.TXT, "", Lib.V2(24, 112), null, m.PMY, Lib.VB, 0, 1.5f),
                    new MySprite(Program.TXT, "", Lib.V2(24, 192), null, m.PMY, Lib.VB, 0, 0.8195f),
                  },
                  (p, s) =>
                  {
                      if (Count == 0)
                      {
                          s.SetData("NO TARGET", 0);
                          s.SetData("SWITCH TO MASTS SCR\nFOR TGT ACQUISITION", 1);
                          return;
                      }

                      var ty = "";
                      var t = _targets[_iEIDs[p]];
                      s.SetData($"{t.eIDString}", 0);

                      if ((int)t.Type == 3)
                          ty = "LARGE GRID";
                      else if ((int)t.Type == 2)
                          ty = "SMALL GRID";

                      s.SetData($"DIST {t.Distance / 1000:F2} KM\nASPD {t.Velocity.Length():F0} M/S\nACCL {t.Accel.Length():F0} M/S\nSIZE {ty}\nNO TGT SELECTED", 1);
                  }));

        }

        public void CreateRadar(int p, Screen s)
        {
            if (Count == 0)
            {
                var d = DateTime.Now;
                _rdrStatic[4].Data = $"CAMS RADAR\nNO TARGETS";
                s.sprites = _rdrStatic;
                return;
            }
            int i = 0;
            for (; i < _rdrData.Length; i++)
                _rdrData[i] = "";
            i = 1;
            // todo: fix
            if (Count > 1)
            {
                _rdrData[6] = "↑ PREV TGT ↑";
                _rdrData[7] = "↓ NEXT TGT ↓";
                if (p == Count - 1)
                {
                    RadarText(p, false);
                    RadarText(p - 1, true);
                }
                else RadarText(p, true);
            }
            RadarText(p, false);

            _rdrStatic[4].Data = _rdrData[0];
            for (; i < _rdrData.Length; i++)
                _rdrStatic[4].Data += $"\n{_rdrData[i]}";
            _rdrBuffer.Clear();
            for (i = 0; i < Count; i++)
            {
                var mat = _p.Controller.WorldMatrix;
                var rPos = _p.Center - _targets[_iEIDs[i]].Position;
                _rdrBuffer.Add(DisplayTarget(ref rPos, ref mat, _targets[_iEIDs[i]].eIDTag, p == i));
            }
            s.sprites = null;
            s.sprites = new MySprite[_rdrBuffer.Count + _rdrStatic.Length];
            for (i = 0; i < s.sprites.Length; i++)
            {
                if (i < _rdrStatic.Length)
                    s.sprites[i] = _rdrStatic[i];
                else s.sprites[i] = _rdrBuffer[i - _rdrStatic.Length];
            }
        }

        //shitty thing to reuse code for wc text
        void RadarText(int p, bool next)
        {
            int i = next ? 9 : 0;
            if (next) p++;
            if (p >= Count) return;

            long eid = _iEIDs[p];
            Vector3D
                rpos = _targets[eid].Position - _p.Controller.WorldMatrix.Translation,
                up = _p.Controller.WorldMatrix.Up;
            var typ = (int)_targets[eid].Type == 3 ? "LARGE" : "SMALL";

            _rdrData[i] = _targets[eid].eIDString;
            _rdrData[i + 1] = $"DIST {_targets[eid].Distance / 1E3:F2} KM";
            _rdrData[i + 2] = $"{typ} GRID";
            _rdrData[i + 3] = $"RSPD {(_targets[eid].Velocity - _p.Velocity).Length():F0} M/S";
            _rdrData[i + 4] = $"ELEV {(Math.Sign(rpos.Dot(up)) < 0 ? "-" : "+")}{Lib.Projection(rpos, up).Length():F0} M";
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
            var dir = tgt.Position - _p.Center;
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

        public Target Get(long eid) => _targets.ContainsKey(eid) ? _targets[eid] : null;

        public void Update(UpdateFrequency u)
        {
            ScannedIDs.Clear();
            if ((u & Lib.u100) != 0)
            {
                foreach (var k in _targets.Keys)
                    _rmvEIDs.Add(k);
                if (Count == 0)
                    return;
                for (int i = Count - 1; i >= 0; i--)
                    if (_targets[_rmvEIDs[i]].Elapsed(_p.F) >= MAX_LIFETIME)
                    {
                        ScannedIDs.Remove(_rmvEIDs[i]);
                        _targets.Remove(_rmvEIDs[i]);
                        _iEIDs.Remove(_rmvEIDs[i]);
                    }
                _rmvEIDs.Clear();
            }
            else if (_p.GlobalPriorityUpdateSwitch && _p.F >= _nextPrioritySortF)
            {
                Prioritized.Clear();
                foreach (var t in _targets.Values)
                {
                    Prioritized.Add(t);
                }
                _nextPrioritySortF = _p.F + _p.PriorityCheckTicks;
                _p.GlobalPriorityUpdateSwitch = false;
            }
        }
        public bool Exists(long id) => _targets.ContainsKey(id);

        public void Clear()
        {
            _rmvEIDs.Clear();
            _targets.Clear();
            ScannedIDs.Clear();
            _iEIDs.Clear();
        }
    }
}