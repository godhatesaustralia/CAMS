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
        public double Time;
        public HitPoint(Vector3D p, double t)
        {
            Position = p;
            Time = t;
        }
    }

    public enum ScanResult
    {
        Failed,
        Hit,
        Update,
        Added
    }


    public class Target
    {
        static readonly double MAX_LIFETIME = 2E3; // ms 
        public readonly long EID, Source;
        public double Radius, Distance, Timestamp;
        public Vector3D Position, Velocity, LastVelocity, Accel;
        public readonly MyDetectedEntityType Type;
        public bool isEngaged = false;
        public HashSet<HitPoint> Hits = new HashSet<HitPoint>();
        public string eIDString => EID.ToString("X").Remove(0, 6);

        public Target(MyDetectedEntityInfo i, double time, long id, double dist)
        {
            EID = i.EntityId;
            Timestamp = time;
            Source = id;
            Type = i.Type;
            Position = i.Position;
            var p = i.HitPosition;
            if (p != null)
                Hits.Add(new HitPoint(p.Value, time));
            Velocity = i.Velocity;
            Radius = i.BoundingBox.Size.Length();
            Distance = dist;
        }

        public Target(MyTuple<MyTuple<long, long, double, int, bool>, MyTuple<Vector3D, Vector3D, MatrixD, double>> data)
        {
            EID = data.Item1.Item1;
            Source = data.Item1.Item2;
            Timestamp = data.Item1.Item3;
            Type = (MyDetectedEntityType)data.Item1.Item4;
            isEngaged = data.Item1.Item5;
            Position = data.Item2.Item1;

            Velocity = data.Item2.Item2;
            Radius = data.Item2.Item4;
        }

        public void Update(ref MyDetectedEntityInfo i, Vector3D cnr, double time)
        {
            var prev = Timestamp;
            Timestamp = time;
            Position = i.Position;
            LastVelocity = Velocity;
            Velocity = i.Velocity;
            Accel = Vector3D.Zero;

            var a = (Velocity - LastVelocity) / (Timestamp - prev);
            // decide whether to keep this later
            if (a.LengthSquared() > 1)
                Accel = (Accel * 0.25) + (a * 0.75);

            Radius = i.BoundingBox.Size.Length();
            Distance = (cnr - i.Position).Length();
        }

        public bool IsExpired(double now) => now - Timestamp >= MAX_LIFETIME;

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

        public double Elapsed(double now)
        {
            return now - Timestamp;
        }

        public Vector3D AdjustedPosition(double now, double offset = 0)
        {
            var diff = now + offset + Lib.tick; // stupid
            if (Velocity.Length() < 0.5 || !IsExpired(diff))
                return Position;
            return Position + diff * Velocity + Accel * 0.5 * diff * diff;
        }
    }

    public class TargetProvider
    {
        CombatManager _host;
        public int Count => _targetsMaster.Count;
        Dictionary<long, Target> _targetsMaster = new Dictionary<long, Target>();
        SortedSet<Target> _targetsByTimestamp;
        List<long> _targetEIDs = new List<long>();
        public HashSet<long> Blacklist = new HashSet<long>();
        public TargetProvider(CombatManager m)
        {
            _host = m;
            Blacklist.Add(m.Program.Me.CubeGrid.EntityId);
            m.Terminal.GetBlocksOfType<IMyMotorStator>(null, (b) =>
            {
                if (b.TopGrid == null) return false;
                var i = b.TopGrid.EntityId;
                if (!Blacklist.Contains(i))
                    Blacklist.Add(i);
                return true;
            });
            _targetsByTimestamp = new SortedSet<Target>(new TargetComparer());
        }

        class TargetComparer : IComparer<Target> // modified dds comparer to use timestamp (dbl)
        {
            public int Compare(Target x, Target y)
            {
                if (x == null) return (y == null ? 0 : 1);
                else if (y == null) return -1;
                else return (x.Timestamp < y.Timestamp ? -1 : (x.Timestamp > y.Timestamp ? 1 : (x.EID < y.EID ? -1 : (x.EID > y.EID ? 1 : 0))));
            }
        }

        public ScanResult AddOrUpdate(ref MyDetectedEntityInfo i, long src)
        {
            var id = i.EntityId;
            Target t;
            if (_targetsMaster.ContainsKey(id))
            {
                t = _targetsMaster[id];
                if (i.HitPosition != null)
                    t.Hits.Add(new HitPoint(i.HitPosition.Value, _host.Runtime));

                if (!t.IsExpired(_host.Runtime + 1200))
                    return ScanResult.Hit;

                t.Hits.Clear();
                t.Update(ref i, _host.Center, _host.Runtime);
                return ScanResult.Update;
            }
            else
            {
                _targetsMaster[id] = new Target(i, _host.Runtime, src, (_host.Center - i.Position).Length());
                _targetsByTimestamp.Add(_targetsMaster[id]);
                return ScanResult.Added;
            }
        }

        public List<Target> AllTargets()
        {
            var list = new List<Target>(Count);
            foreach (var t in _targetsByTimestamp)
                list.Add(t);
            return list;
        }

        public void Update(UpdateFrequency u)
        {
            if ((u & Lib.u10) != 0)
            {
                _targetsByTimestamp.Clear();
                foreach (var t in _targetsMaster.Values)
                    _targetsByTimestamp.Add(t);
            }
            if ((u & Lib.u100) != 0)
                RemoveExpired();
        }

        public bool TryGetID(long id, out Target t)
        {
            t = null;
            if (!_targetsMaster.ContainsKey(id))
                return false;
            t = _targetsMaster[id];
            return true;
        }

        void RemoveExpired() // THIS GOES AFTER EVERYTHIGN ELSE
        {
            foreach (var k in _targetsMaster.Keys)
                _targetEIDs.Add(k);
            if (Count == 0)
                return;
            for (int i = Count - 1; i >= 0; i--)
                if (_targetsMaster[_targetEIDs[i]].IsExpired(_host.Runtime))
                    RemoveID(_targetEIDs[i]);
            _targetEIDs.Clear();
        }

        public Target Oldest => _targetsByTimestamp.Count != 0 ? _targetsByTimestamp.Min : null;
        public void RemoveID(long eid)
        {
            if (_targetsMaster.ContainsKey(eid))
            {
                _targetsByTimestamp.Remove(_targetsMaster[eid]);
                _targetsMaster.Remove(eid);
            }
        }

        public void Clear()
        {
            _targetEIDs.Clear();
            _targetsMaster.Clear();
            _targetsByTimestamp.Clear();
        }
    }
}