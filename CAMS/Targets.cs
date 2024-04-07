using Sandbox.ModAPI.Ingame;
using System;
using VRage;
using VRage.Game;
using VRageMath;

namespace IngameScript
{
    public class Target
    {
        public long EID, Source;
        public double Radius, Distance, Timestamp;
        public Vector3D Position, Velocity;
        public BoundingBoxD Box;
        public MyDetectedEntityType type;
        public bool isEngaged = false;
        

        public Target(MyDetectedEntityInfo threat, double time, long id, double dist)
        {
            EID = threat.EntityId;
            Timestamp = time;
            Source = id;
            type = threat.Type;
            Position = threat.Position;
            Velocity = threat.Velocity;
            Box = threat.BoundingBox;
            Radius = threat.BoundingBox.Size.Length();
            Distance = dist;
        }

        public Target(MyTuple<MyTuple<long, long, double, int, bool>, MyTuple<Vector3D, Vector3D, MatrixD, BoundingBoxD>> data)
        {
            EID = data.Item1.Item1;
            Source = data.Item1.Item2;
            Timestamp = data.Item1.Item3;
            type = (MyDetectedEntityType)data.Item1.Item4;
            isEngaged = data.Item1.Item5;
            Position = data.Item2.Item1;
            Velocity = data.Item2.Item2;
            Box = data.Item2.Item4;
            Radius = Box.Size.Length();
        }

        public double Elapsed(double now)
        {
            return now - Timestamp;
        }

        public Vector3D PositionUpdate(double now, double offset = 0)
        {
            return Position + (now + offset - Timestamp) * Velocity;       
        }
    }
}