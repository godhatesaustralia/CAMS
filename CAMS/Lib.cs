using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using System;
using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    // bunch of different global fields and methjods and stuff
    public static class Lib
    {
        public static string hdr = "CAMS", array = "ARY", tr = "Turrets", sn = "Scanner";

        public static readonly double maxTimeTGT = 43; //ms
        static Dictionary<string, ITerminalProperty> _terminalPropertyDict = new Dictionary<string, ITerminalProperty>();
        public static Color debug = new Color(100, 250, 100);
        public static UpdateFrequency UpdateConverter(UpdateType src)
        {
            var updateFrequency = UpdateFrequency.None; //0000
            if ((src & UpdateType.Update1) != 0) updateFrequency |= UpdateFrequency.Update1; //0001
            if ((src & UpdateType.Update10) != 0) updateFrequency |= UpdateFrequency.Update10; //0010
            if ((src & UpdateType.Update100) != 0) updateFrequency |= UpdateFrequency.Update100;//0100
            return updateFrequency;
        }

        public static Vector3D GetAttackPoint(Vector3D relVel, Vector3D relPos, double projSpd)
        {
            if (relVel == Vector3D.Zero) return relPos;

            var P0 = relPos;
            var V0 = relVel;

            double
                s1 = projSpd,
                a = V0.Dot(V0) - (s1 * s1),
                b = 2 * P0.Dot(V0),
                c = P0.Dot(P0),

                det = (b * b) - (4 * a * c),
                max = double.MaxValue;

            if (det < 0 || a == 0) return Vector3D.Zero;

            var t1 = (-b + Math.Sqrt(det)) / (2 * a);
            var t2 = (-b - Math.Sqrt(det)) / (2 * a);

            if (t1 <= 0) t1 = max;
            if (t2 <= 0) t2 = max;

            var t = Math.Min(t1, t2);

            if (t == max) return Vector3D.Zero;

            return relPos + relVel * t;
        }
        public static double AngleBetween(Vector3D a, Vector3D b)
        {
            if (Vector3D.IsZero(a) || Vector3D.IsZero(b))
                return 0;
            else
                return Math.Acos(MathHelper.Clamp(a.Dot(b) / Math.Sqrt(a.LengthSquared() * b.LengthSquared()), -1, 1));
        }

        public static void SetValue<T>(IMyTerminalBlock block, string propertyName, T value)
        {
            ITerminalProperty prop;
            if (_terminalPropertyDict.TryGetValue(propertyName, out prop))
            {
                prop.Cast<T>().SetValue(block, value);
                return;
            }

            prop = block.GetProperty(propertyName);
            _terminalPropertyDict[propertyName] = prop;
            prop.Cast<T>().SetValue(block, value);
        }

        public static int Next(ref int p, int max)
        {
            if (p < max)
                p++;
            if (p == max)
                p = 0;
            return p;
        }

        public static Vector3D Projection(Vector3D a, Vector3D b)
        {
            if (Vector3D.IsZero(b))
                return Vector3D.Zero;
            return a.Dot(b) / b.LengthSquared() * b;
        }
    }
}