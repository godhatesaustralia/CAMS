using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage;
using VRage.Game;
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
        #endregion
        public string eIDString => EID.ToString("X").Remove(0, 6);


        public Target(MyDetectedEntityInfo i, long f, long id, double dist)
        {
            EID = i.EntityId;
            Source = id;
            Type = i.Type;
            IFF = i.Relationship;
            Position = i.Position;
            // var p = i.HitPosition;
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
        List<long> _targetEIDs = new List<long>(), _scrnEIDs = new List<long>();
        const double rad = 180 / Math.PI;
        public HashSet<long> ScannedIDs = new HashSet<long>();
        public HashSet<long> Blacklist = new HashSet<long>();
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
            m.Screens.Add("targets", new Screen(() => Count, new MySprite[]
            {
                new MySprite(Lib.TXT, "", new Vector2(24, 112), null, Lib.GRN, Lib.VB, 0, 1.75f),
                new MySprite(Lib.TXT, "", new Vector2(24, 200), null, Lib.GRN, Lib.VB, 0, 0.8195f),
                //new MySprite(Lib.TXT, "", new Vector2(24, 112), null, Lib.GRN, Lib.WH, 0, 2f),
                //new MySprite(Lib.TXT, "", new Vector2(24, 200), null, Lib.GRN, Lib.WH, 0, 1.5f)
            }, s =>
            {
                string ty = "NULL";
                var t = _targetsMaster[_scrnEIDs[s.ptr]];
                s.SetData($"{t.eIDString}", 0);
                if ((int)t.Type == 2) ty = "LARGE";
                else if ((int)t.Type == 3) ty = "SMALL";
                s.SetData($"DIST {t.Distance / 1000:#0.#} KM\nVL {t.Velocity.Length():000} M/S\nSZ {ty}", 1);
            }
                
                ));
        }

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
                _scrnEIDs.Add(id);
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
                _scrnEIDs.Remove(eid);
            }
        }

        public void Clear()
        {
            _targetEIDs.Clear();
            _targetsMaster.Clear();
            ScannedIDs.Clear();
            _scrnEIDs.Clear();
        }
    }
}