using Sandbox.Game;
using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Noise.Combiners;
using VRageMath;

namespace IngameScript
{
    public class RoundRobin<K, V>
    {
        public readonly K[] IDs;
        int start, current;

        public RoundRobin(K[] ks, int s = 0)
        {
            start = s;
            IDs = ks;
            Reset();
        }

        public V Next(ref Dictionary<K, V> dict)
        {
            if (current < IDs.Length)
                current++;
            if (current == IDs.Length)
                current = start;
            return dict[IDs[current]];
        }

        // checks whether end of the key collection has been reached+
        public bool Next(ref Dictionary<K, V> dict, out V val)
        {
            if (current < IDs.Length)
                current++;
            if (current == IDs.Length)
                current = start;
            val = dict[IDs[current]];
            return current < IDs.Length - (start + 1);
        }


        public void Reset() => start = current = 0;

    }

    public abstract class CompBase
    {
        public readonly string Name;
        public long ID => Main.Me.CubeGrid.EntityId;
        public virtual string Debug { get; protected set; }
        public IMyShipController Reference => Main.Controller;

        public Vector3D Velocity => Main.Controller.GetShipVelocities().LinearVelocity;
        public Dictionary<string, Action<MyCommandLine>> Commands = new Dictionary<string, Action<MyCommandLine>>();
        public Program Main;
        public double NextDbl() => 2 * Main.RNG.NextDouble() - 1; // from lamp
        public double GaussRNG() => (NextDbl() + NextDbl() + NextDbl()) / 3;
        public double Time => Main.RuntimeMS;
        public long F => Main.F;

        public bool PassTarget(MyDetectedEntityInfo info, bool m = false)
        {
            ScanResult fake;
            return PassTarget(info, out fake, m);
        }
        public bool PassTarget(MyDetectedEntityInfo info, out ScanResult r, bool m = false)
        {
            r = ScanResult.Failed;
            if (info.IsEmpty())
                return false;
            if (Targets.Blacklist.Contains(info.EntityId))
                return false;
            int rel = (int)info.Relationship, t = (int)info.Type;
            if (rel == 1 || rel == 5) // owner or friends
                return false;
            if (t != 2 && t != 3) // small grid and large grid respectively
                return false;
            if (info.BoundingBox.Size.Length() < 1.5)
                return false;
            r = Targets.AddOrUpdate(ref info, ID);
            if (!m)
                Targets.ScannedIDs.Add(info.EntityId);
            return true;
        }

        public TargetProvider Targets => Main.Targets; // IEnumerator sneed
        public UpdateFrequency Frequency;
        public CompBase(string n, UpdateFrequency u)
        {
            Name = n;
            Frequency = u;
        }
        public T Cast<T>()
        where T : CompBase
        {
            return (T)this;
        }

        public abstract void Setup(Program prog);
        public abstract void Update(UpdateFrequency u);
    }

    public partial class Program
    {
        public IMyGridTerminalSystem Terminal => GridTerminalSystem;
        public IMyShipController Controller;
        public DebugAPI Debug;
        public bool Based;
        public double PDSpray = -1;
        string _tag;
        public Vector3D Center => Controller.WorldMatrix.Translation;
        public Vector3D Velocity => Controller.GetShipVelocities().LinearVelocity;
        public Vector3D Gravity;
        public TargetProvider Targets;
        public Dictionary<string, Screen>
            CtrlScreens = new Dictionary<string, Screen>(),
            LCDScreens = new Dictionary<string, Screen>();
        public Dictionary<string, Display> Displays = new Dictionary<string, Display>();

        public Dictionary<string, CompBase> Components = new Dictionary<string, CompBase>();
        public Random RNG = new Random();
        MyCommandLine _cmd = new MyCommandLine();
        RoundRobin<string, Display> DisplayRR;
        double _totalRT = 0, _worstRT, _avgRT;
        long _frame = 0, _worstF;
        const int _rtMax = 10;
        Queue<double> _runtimes = new Queue<double>(_rtMax);
        public double RuntimeMS => _totalRT;
        public long F => _frame;

        void Start()
        {
            Targets.Clear();
            var r = new MyIniParseResult();
            var dspGrp = new List<IMyTerminalBlock>();
            using (var p = new iniWrap())
                if (p.CustomData(Me, out r))
                {
                    Based = p.Bool(Lib.HDR, "vcr");
                    _tag = p.String(Lib.HDR, "tag", Lib.HDR);
                    PDSpray = p.Double(Lib.HDR, "spray", -1);
                    string
                        ctrl = p.String(Lib.HDR, "controller", "Helm");
                    GridTerminalSystem.GetBlocksOfType<IMyShipController>(null, (b) =>
                    {
                        if (b.CubeGrid == Me.CubeGrid && b.CustomName.Contains(ctrl))
                            Controller = b;
                        return true;
                    });

                    foreach (var c in Components)
                        c.Value.Setup(this);

                    GridTerminalSystem.GetBlockGroupWithName(_tag + ' ' + p.String(Lib.HDR, "displays", "MFD Users")).GetBlocks(dspGrp);
                    if (dspGrp.Count > 0)
                    {
                        foreach (var b in dspGrp)
                        {
                            Display d;
                            if (b is IMyTextPanel)
                            {
                                d = new Display(this, b, LCDScreens.First().Key, Based);
                                Displays.Add(d.Name, d);
                            }
                            else if (b is IMyTextSurfaceProvider)
                            {
                                d = new Display(this, b, CtrlScreens.First().Key, Based);
                                Displays.Add(d.Name, d);
                            }
                        }
                        DisplayRR = new RoundRobin<string, Display>(Displays.Keys.ToArray());
                    }

                }
            else throw new Exception($"\n{r.Error} at line {r.LineNo} of {Me} custom data.");
        }

        static Vector3D 
            lastVel = Vector3D.Zero,
            lastAccel = Vector3D.Zero;
        static long lastVelT = 0;

        public Vector3D Acceleration
        {
            get 
            {
                if (F - lastVelT > 0)
                {
                    lastAccel = 0.5 * (lastAccel + (Velocity - lastVel) / (F - lastVelT));
                    lastVelT = F;
                    lastVel = Velocity;
                }
                return lastAccel;
            }
        }
        public void SendWhamTarget(ref Vector3D hitPos, ref Vector3D tPos, ref Vector3D tVel, ref Vector3D preciseOffset, ref Vector3D myPos, double elapsed, long tEID, long keycode)
        {
            Matrix3x3 mat1 = new Matrix3x3();
            FillMatrix(ref mat1, ref hitPos, ref tPos, ref tVel);

            Matrix3x3 mat2 = new Matrix3x3();
            FillMatrix(ref mat2, ref preciseOffset, ref myPos, ref Vector3D.Zero);

            var msg = new MyTuple<Matrix3x3, Matrix3x3, float, long, long>
            {
                Item1 = mat1,
                Item2 = mat2,
                Item3 = (float)elapsed,
                Item4 = tEID,
                Item5 = keycode,
            };
            IGC.SendBroadcastMessage(Lib.IgcHoming, msg);
        }

        public void SendParams(bool kill, bool stealth, bool spiral, bool topdown, bool precise, bool retask, long keycode)
        {
            byte packed = 0;
            packed |= BoolToByte(kill);
            packed |= (byte)(BoolToByte(stealth) << 1);
            packed |= (byte)(BoolToByte(spiral) << 2);
            packed |= (byte)(BoolToByte(topdown) << 3);
            packed |= (byte)(BoolToByte(precise) << 4);
            packed |= (byte)(BoolToByte(retask) << 5);

            var msg = new MyTuple<byte, long>
            {
                Item1 = packed,
                Item2 = keycode
            };

            IGC.SendBroadcastMessage(Lib.IgcParams, msg);
        }

        static byte BoolToByte(bool value) => value ? (byte)1 : (byte)0;
        static void FillMatrix(ref Matrix3x3 mat, ref Vector3D col0, ref Vector3D col1, ref Vector3D col2)
        {
            mat.M11 = (float)col0.X;
            mat.M21 = (float)col0.Y;
            mat.M31 = (float)col0.Z;

            mat.M12 = (float)col1.X;
            mat.M22 = (float)col1.Y;
            mat.M32 = (float)col1.Z;

            mat.M13 = (float)col2.X;
            mat.M23 = (float)col2.Y;
            mat.M33 = (float)col2.Z;
        }

    }
}