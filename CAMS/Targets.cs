using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Threading;
using VRage;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ObjectBuilders.VisualScripting;
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

    public class Target
    {
        #region fields
        static public readonly int MAX_LIFETIME = 2; // s
        public readonly long EID, Source;
        public double Radius, Distance;
        public long Frame;
        public MatrixD Matrix;
        public Vector3D Position, Velocity, LastVelocity, Accel;
        public readonly MyDetectedEntityType Type;
        public readonly MyRelationsBetweenPlayerAndBlock IFF; // wham
        bool _isEngaged = false;
        #endregion
        public readonly string eIDString, eIDTag;


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

        public Target(MyTuple<MyTuple<long, long, long, int, bool>, MyTuple<Vector3D, Vector3D, MatrixD, double>> data)
        {
            EID = data.Item1.Item1;
            Source = data.Item1.Item2;
            Frame = data.Item1.Item3;
            Type = (MyDetectedEntityType)data.Item1.Item4;
            _isEngaged = data.Item1.Item5;
            Position = data.Item2.Item1;
            Matrix = data.Item2.Item3;
            Velocity = data.Item2.Item2;
            Radius = data.Item2.Item4;
        }

        public void Update(ref MyDetectedEntityInfo i, Vector3D cnr, long f)
        {
            var dT = Elapsed(f);
            Frame = f;
            Position = i.Position;
            LastVelocity = Velocity;
            Velocity = i.Velocity;
            Matrix = i.Orientation;
            Accel = Vector3D.Zero;
            var a = (Velocity - LastVelocity) * dT;
            // decide whether to keep this later
            if (a.LengthSquared() > 1)
                Accel = (Accel * 0.25) + (a * 0.75);
            Radius = i.BoundingBox.Size.Length();
            Distance = (cnr - i.Position).Length();
        }

        public bool IsExpired(long now) => Elapsed(now) >= MAX_LIFETIME;


        public double Elapsed(long f) => Lib.tickSec * Math.Max(f - Frame, 1);


        public Vector3D AdjustedPosition(long f, long offset = 0)
        {
            f += offset;
            var dT = Elapsed(f);
            if (Velocity.Length() < 0.5 && Accel.Length() <= 1)
                return Position;
            return Position + Velocity * dT + Accel * 0.5 * dT * dT;
        }
    }

    public class TargetProvider
    {
        Program _host;
        const double INV_MAX_D = 0.0001, R_SIDE_L = 154;
        public int Count => _indexEIDs.Count;
        Dictionary<long, Target> _targetsMaster = new Dictionary<long, Target>();
        List<long> _targetEIDs = new List<long>(), _indexEIDs = new List<long>();
        const float rad = (float)Math.PI / 180, X_MIN = 28, X_MAX = 308, Y_MIN = 96, Y_MAX = 362; // radar
        static Vector2 rdrCNR = Lib.V2(168, 228), rdrSZ = Lib.V2(308, 308), tgtSz = Lib.V2(12, 12);
        List<MySprite> _rdrBuffer = new List<MySprite>();
        string[] _rdrData = new string[14];
        // reused sprites
        MySprite[] _rdrStatic = new MySprite[]
        {
            new MySprite(Lib.SHP, Lib.SQS, rdrCNR, Lib.V2(12, 12), Lib.DRG, rotation: rad * 45),
            new MySprite(Lib.SHP, Lib.SQS, rdrCNR, Lib.V2(4, 428), Lib.DRG, rotation: rad * -45),
            new MySprite(Lib.SHP, Lib.SQS, rdrCNR, Lib.V2(4, 428), Lib.DRG, rotation: rad * 45),
            new MySprite(Lib.SHP, Lib.SQS, Lib.V2(320, 228), Lib.V2(8, 308), Lib.GRN),
            new MySprite(), // this one depends on font in use
            new MySprite(Lib.SHP, Lib.CHW, rdrCNR, Lib.V2(61.6f, 61.6f), Lib.DRG)
        };

        public HashSet<long> 
            ScannedIDs = new HashSet<long>(),
            Blacklist = new HashSet<long>();

        public TargetProvider(Program m)
        {
            _host = m;
            Blacklist.Add(m.Me.CubeGrid.EntityId);
            m.Terminal.GetBlocksOfType<IMyMotorStator>(null, b =>
            {
                if (b.TopGrid == null) return false;
                var i = b.TopGrid.EntityId;
                if (!Blacklist.Contains(i))
                    Blacklist.Add(i);
                return true;
            });
            m.CtrlScreens.Add("targets", new Screen(() => Count, new MySprite[]
            {
                new MySprite(Lib.TXT, "", Lib.V2(24, 112), null, Lib.GRN, Lib.VB, 0, 1.5f),
                new MySprite(Lib.TXT, "", Lib.V2(24, 192), null, Lib.GRN, Lib.VB, 0, 0.8195f),
            }, (p, s) =>
            {
                    if (Count == 0)
                    {
                        s.SetData("NO TARGET", 0);
                        s.SetData("SWITCH TO MASTS SCR\nFOR TGT ACQUISITION", 1);
                        return;
                    }
                    string ty = "NULL";
                    var t = _targetsMaster[_indexEIDs[p]];
                    s.SetData($"{t.eIDString}", 0);
                    if ((int)t.Type == 3) ty = "LARGE";
                    else if ((int)t.Type == 2) ty = "SMALL";
                    ty += " GRID";
                    s.SetData($"DIST {t.Distance / 1000:F2} KM\nASPD {t.Velocity.Length():F0} M/S\nRSPD {(_host.Velocity - t.Velocity).Length():F0} M/S\nSIZE {ty}\nNO TGT SELECTED", 1);
                
            }));
            // _rdrStatic[4]= new MySprite(Lib.TXT, "", Lib.V2(328, 084), null, Lib.GRN, m.Based ? "VCR" : "White", Lib.LFT, m.Based ? .425f : .8f);
            _rdrStatic[4] = new MySprite(Lib.TXT, "", Lib.V2(328, 84), null, Lib.GRN, Lib.V, Lib.LFT, !m.Based ? .425f : .8f);
            m.LCDScreens.Add("radar", new Screen(() => Count, null, (p, s) => CreateRadar(p, s)));
        }
        #region radar
        void CreateRadar(int p, Screen s)
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
                    RadarText(p - 1, false);
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
                
                var mat = _host.Controller.WorldMatrix;
                var rPos = _host.Center - _targetsMaster[_indexEIDs[i]].Position;
                _rdrBuffer.Add(DisplayTarget(ref rPos, ref mat, _targetsMaster[_indexEIDs[i]].eIDTag, p == i));
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
            if (next)
                p++;
            if (p >= Count) return;
            long eid = _indexEIDs[p];
            var rpos = _targetsMaster[eid].Position - _host.Controller.WorldMatrix.Translation;
            var up = _host.Controller.WorldMatrix.Up;
            var typ = (int)_targetsMaster[eid].Type == 3 ? "LARGE" : "SMALL";
            _rdrData[i] = _targetsMaster[eid].eIDString;
            _rdrData[i + 1] = $"DIST {_targetsMaster[eid].Distance / 1E3:F2} KM";
            _rdrData[i + 2] = $"{typ} GRID";
            _rdrData[i + 3] = $"RSPD {(_targetsMaster[eid].Velocity - _host.Velocity).Length():F0} M/S";
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
                scrY = R_SIDE_L  * yProj * INV_MAX_D + rdrCNR.Y;

            // clamp into a region that doesn't neatly correspond with screen size ( i have autism)
                scrX = MathHelper.Clamp(scrX, X_MIN, X_MAX);
                scrY = MathHelper.Clamp(scrY, Y_MIN, Y_MAX);

            // position vectors
            Vector2 
                pos = Lib.V2((float)scrX, (float)scrY),
                txt = (sel ? 2 : 1.5f) * tgtSz;

            _rdrBuffer.Add(new MySprite(Lib.TXT, eid, scrX > 2 * X_MIN ? pos - txt : pos + 0.5f * txt, null, Lib.GRN, Lib.V, rotation: 0.375f));
            return new MySprite(Lib.SHP, sel ? Lib.SQS : Lib.TRI, pos, (sel ? 1.5f : 1) * tgtSz, Lib.GRN, null); // Center
        }

        #endregion
        public ScanResult AddOrUpdate(ref MyDetectedEntityInfo i, long src)
        {
            var id = i.EntityId;
            Target t;
            if (_targetsMaster.ContainsKey(id))
            {
                t = _targetsMaster[id];
                t.Update(ref i, _host.Center, _host.F);
                return ScanResult.Update;
            }
            else
            {
                _targetsMaster[id] = new Target(i, _host.F, src, (_host.Center - i.Position).Length());
                _indexEIDs.Add(id);
                return ScanResult.Added;
            }
        }

        public List<Target> AllTargets()
        {
            var list = new List<Target>(Count);
            foreach (var t in _targetsMaster.Values)
                list.Add(t);
            return list;
        }

        public void Update(UpdateFrequency u)
        {
            ScannedIDs.Clear();
            if ((u & Lib.u100) != 0)
                RemoveExpired();
        }
        public bool isNew(long id) => _targetsMaster.ContainsKey(id) && !ScannedIDs.Contains(id);

        void RemoveExpired() // THIS GOES AFTER EVERYTHIGN ELSE
        {
            foreach (var k in _targetsMaster.Keys)
                _targetEIDs.Add(k);
            if (Count == 0)
                return;
            for (int i = Count - 1; i >= 0; i--)
                if (_targetsMaster[_targetEIDs[i]].IsExpired(_host.F))
                    RemoveID(_targetEIDs[i]);
            _targetEIDs.Clear();
        }
        public void RemoveID(long eid)
        {
            if (_targetsMaster.ContainsKey(eid))
            {
                ScannedIDs.Remove(eid);
                _targetsMaster.Remove(eid);
                _indexEIDs.Remove(eid);
            }
        }

        public void Clear()
        {
            _targetEIDs.Clear();
            _targetsMaster.Clear();
            ScannedIDs.Clear();
            _indexEIDs.Clear();
        }
    }
}