using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage;
using VRage.Game;
using VRageMath;

namespace IngameScript
{
    public struct HitPoint // stolen
    {
        public Vector3D Position;
        public long Frame;
        public HitPoint(Vector3D p = default(Vector3D), long f = -1)
        {
            Position = p;
            Frame = f;
        }
    }

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
        public Vector3D Position, Velocity, LastVelocity, Accel;
        public readonly MyDetectedEntityType Type;
        public readonly MyRelationsBetweenPlayerAndBlock IFF; // wham
        bool _isEngaged = false;
        public HashSet<HitPoint> Hits = new HashSet<HitPoint>();
        #endregion
        public string eIDString => EID.ToString("X").Remove(0, 6);


        public Target(MyDetectedEntityInfo i, long f, long id, double dist)
        {
            EID = i.EntityId;
            Source = id;
            Type = i.Type;
            IFF = i.Relationship;
            Position = i.Position;
            var p = i.HitPosition;
            if (p != null)
                Hits.Add(new HitPoint(p.Value, f));
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
            Accel = Vector3D.Zero;
            var a = (Velocity - LastVelocity) * dT;
            if (i.HitPosition.HasValue)
                Hits.Add(new HitPoint(i.HitPosition.Value, f));
            // decide whether to keep this later
             if (a.LengthSquared() > 1)
                Accel = (Accel * 0.25) + (a * 0.75);
            Radius = i.BoundingBox.Size.Length();
            Distance = (cnr - i.Position).Length();
        }

        public bool IsExpired(long now) => Elapsed(now) >= MAX_LIFETIME;
        

        public Vector3D GetOffsetOrPosition()
        {
            if (Hits.Count == 0)
                return Position;
            else
            {
                var h = Hits.FirstElement();
                Hits.Remove(h);
                return h.Position;
            }
        }

        public double Elapsed(long f) => Lib.tickSec * Math.Max(f - Frame, 1);


        public Vector3D AdjustedPosition(long f, long offset = 0)
        {
            f += offset;
            var dT = Elapsed(f);
            if (Velocity.Length() < 0.5  && Accel.Length() <= 1)
                return Position;
            return Position + Velocity * dT + Accel * 0.5 * dT * dT;
        }
    }

    public class TargetProvider
    {
        Program _host;
        public int Count => _targetsMaster.Count;
        Dictionary<long, Target> _targetsMaster = new Dictionary<long, Target>();
        List<long> _targetEIDs = new List<long>();
        List<HitPoint> _expiredHits = new List<HitPoint>();
        public HashSet<long> ScannedIDs = new HashSet<long>();
        public HashSet<long> Blacklist = new HashSet<long>();
        public TargetProvider(Program m)
        {
            _host = m;
            Blacklist.Add(m.Me.CubeGrid.EntityId);
            m.Terminal.GetBlocksOfType<IMyMotorStator>(null, (b) =>
            {
                if (b.TopGrid == null) return false;
                var i = b.TopGrid.EntityId;
                if (!Blacklist.Contains(i))
                    Blacklist.Add(i);
                return true;
            });
        }

        public ScanResult AddOrUpdate(ref MyDetectedEntityInfo i, long src)
        {
            var id = i.EntityId;
            Target t;
            if (_targetsMaster.ContainsKey(id))
            {
                t = _targetsMaster[id];
                if (t.Hits.Count > 0)
                {
                    _expiredHits.Clear();
                    foreach (var h in t.Hits)
                        if ((_host.F - h.Frame) * Lib.tickSec >= Target.MAX_LIFETIME)
                            _expiredHits.Add(h);
                    foreach (var h in _expiredHits)
                        t.Hits.Remove(h);
                }
                t.Update(ref i, _host.Center, _host.F);
                return ScanResult.Update;
            }
            else
            {
                _targetsMaster[id] = new Target(i, _host.F, src, (_host.Center - i.Position).Length());
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
            }
        }

        public void Clear()
        {
            _targetEIDs.Clear();
            _targetsMaster.Clear();
            ScannedIDs.Clear();
        }
    }
}