using Sandbox.Game.GameSystems;
using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using System;
using VRageMath;
using System.Linq;
using System.Runtime.Versioning;

namespace IngameScript
{
    public class Turnself : CompBase
    {
        //static IMyGridTerminalSystem gridSystem;
        // replace with m
        static long gridId;

        static double kP = 16;
        static double kI = 0;
        static double kD = 32;
        static double lowerBound = -1000;
        static double upperBound = 1000;
        static double decay = 0;
        static double timeStep = 1.0 / 60;

        VectorPID pid = new VectorPID(kP, kI, kD, lowerBound, upperBound, decay, timeStep);
        GyroControl gyros;

        Vector3D toPoint = new Vector3D();
        bool doPoint = false;
        IMyThrust t;

        // implement for CompBase wip
        public override void Update(UpdateFrequency f)
        {
            // er what does this even do?
            return;
        }

        //under our glorious black sun... our beloved leader big vlad harkonenn... presiding over this spectacle of cringe, and debug
        public override void Setup(Program m)
        {
            //intialize gyros 
            //initialize guns..?
            //grab remote control for GyroControl 

            return;
        }
        //end of implement for CompBase
        public Turnself(string n, UpdateFrequency f) : base(n, Lib.u1 | Lib.u10 | Lib.u100)
        {
            //guar.....

            /*if (argument != "")
            {
                if (argument == "toggle")
                {
                    doPoint = !doPoint;
                    if (!doPoint)
                        gyros.Disable();
                }

                if (argument.Substring(0, 3) == "GPS")
                {
                    string[] split = argument.Split(':');
                    toPoint = new Vector3D(double.Parse(split[2]), double.Parse(split[3]), double.Parse(split[4]));
                    Echo(toPoint.ToString());
                }
            }

            if (doPoint)
                gyros.FaceVectors(-Vector3D.Normalize(toPoint - Me.CubeGrid.GetPosition()), t.WorldMatrix.Up);*/ 
            // leftover bullshit fix later

            //todo: 
            //call set up
        }

        public void turn (Vector3D forward, Vector3D up)
        {
            //place holder, no idea if this is right
            gyros.FaceVectors(forward, up);
            return; 
            //end of place holder
        } 


    }

    public class VectorPID
    {
        private PID X;
        private PID Y;
        private PID Z;

        public VectorPID(double kP, double kI, double kD, double lowerBound, double upperBound, double decay, double timeStep)
        {
            X = new PID(kP, kI, kD, lowerBound, upperBound, decay, timeStep);
            Y = new PID(kP, kI, kD, lowerBound, upperBound, decay, timeStep);
            Z = new PID(kP, kI, kD, lowerBound, upperBound, decay, timeStep);
        }
        
        /*public VectorPID(double kP, double kI, double kD, double integralDecayRatio, double timeStep)
        {
            X = new PID(kP, kI, kD, integralDecayRatio, timeStep);
            Y = new PID(kP, kI, kD, integralDecayRatio, timeStep);
            Z = new PID(kP, kI, kD, integralDecayRatio, timeStep);
        }*/

        public Vector3D Control(Vector3D error)
        {
            return new Vector3D(X.Control(error.X), Y.Control(error.Y), Z.Control(error.Z));
        }

        public void Reset()
        {
            X.Reset();
            Y.Reset();
            Z.Reset();
        }
    }

    public class GyroControl
    {
        private List<IMyGyro> gyros;
        IMyTerminalBlock rc;

        public GyroControl(Program m, IMyTerminalBlock rc, double kP, double kI, double kD, double lowerBound, double upperBound, double timeStep)
        {
            this.rc = rc;

            //gyros = GetBlocks<IMyGyro>();
            gyros = new List<IMyGyro>();
            m.GridTerminalSystem.GetBlocksOfType(gyros);

            anglePID = new VectorPID(kP, kI, kD, lowerBound, upperBound, timeStep);

            Reset();
        }
        // In (pitch, yaw, roll)
        VectorPID anglePID;

        public void Reset()
        {
            for (int i = 0; i < gyros.Count; i++)
            {
                IMyGyro g = gyros[i];
                if (g == null)
                {
                    gyros.RemoveAtFast(i);
                    continue;
                }
                g.GyroOverride = false;
            }
            anglePID.Reset();
        }

        public void Disable()
        {
            foreach (var g in gyros)
            {
                g.Pitch = 0;
                g.Yaw = 0;
                g.Roll = 0;
                g.GyroOverride = false;
            }
        }

        Vector3D GetAngles(MatrixD current, Vector3D forward, Vector3D up)
        {
            Vector3D error = new Vector3D();
            if (forward != Vector3D.Zero)
            {
                Quaternion quat = Quaternion.CreateFromForwardUp(current.Forward, current.Up);
                Quaternion invQuat = Quaternion.Inverse(quat);
                Vector3D RCReferenceFrameVector = Vector3D.Transform(forward, invQuat); //Target Vector In Terms Of RC Block

                //Convert To Local Azimuth And Elevation
                Vector3D.GetAzimuthAndElevation(RCReferenceFrameVector, out error.Y, out error.X);
            }

            if (up != Vector3D.Zero)
            {
                Vector3D temp = Vector3D.Normalize(VectorRejection(up, rc.WorldMatrix.Forward));
                double dot = MathHelper.Clamp(Vector3D.Dot(rc.WorldMatrix.Up, temp), -1, 1);
                double rollAngle = Math.Acos(dot);
                double scaler = ScalerProjection(temp, rc.WorldMatrix.Right);
                if (scaler > 0)
                    rollAngle *= -1;
                error.Z = rollAngle;
            }

            if (Math.Abs(error.X) < 0.001)
                error.X = 0;
            if (Math.Abs(error.Y) < 0.001)
                error.Y = 0;
            if (Math.Abs(error.Z) < 0.001)
                error.Z = 0;

            return error;
        }

        // turn ship to align with provided vectors.
        public void FaceVectors(Vector3D forward, Vector3D up)
        {
            // In (pitch, yaw, roll)
            Vector3D error = -GetAngles(rc.WorldMatrix, forward, up);
            Vector3D angles = new Vector3D(anglePID.Control(error));
            ApplyGyroOverride(rc.WorldMatrix, angles);
        }
        void ApplyGyroOverride(MatrixD current, Vector3D localAngles)
        {
            Vector3D worldAngles = Vector3D.TransformNormal(localAngles, current);
            foreach (IMyGyro gyro in gyros)
            {
                Vector3D transVect = Vector3D.TransformNormal(worldAngles, MatrixD.Transpose(gyro.WorldMatrix));  //Converts To Gyro Local
                if (!transVect.IsValid())
                    throw new Exception("Invalid trans vector. " + transVect.ToString());

                gyro.Pitch = (float)transVect.X;
                gyro.Yaw = (float)transVect.Y;
                gyro.Roll = (float)transVect.Z;
                gyro.GyroOverride = true;
            }
        }

        /// <summary>
        /// Projects a value onto another vector.
        /// </summary>
        /// <param name="guide">Must be of length 1.</param>
        public static double ScalerProjection(Vector3D value, Vector3D guide)
        {
            double returnValue = Vector3D.Dot(value, guide);
            if (double.IsNaN(returnValue))
                return 0;
            return returnValue;
        }

        /// <summary>
        /// Projects a value onto another vector.
        /// </summary>
        /// <param name="guide">Must be of length 1.</param>
        public static Vector3D VectorPojection(Vector3D value, Vector3D guide)
        {
            return ScalerProjection(value, guide) * guide;
        }

        /// <summary>
        /// Projects a value onto another vector.
        /// </summary>
        /// <param name="guide">Must be of length 1.</param>
        public static Vector3D VectorRejection(Vector3D value, Vector3D guide)
        {
            return value - VectorPojection(value, guide);
        }
    }

    // not using these
    /*static T GetBlock<T>(string name, bool useSubgrids = false) where T : class, IMyTerminalBlock
    {
        if (useSubgrids)
        {
            return (T)gridSystem.GetBlockWithName(name);
        }
        else
        {
            List<T> blocks = GetBlocks<T>(false);
            foreach (T block in blocks)
            {
                if (block.CustomName == name)
                    return block;
            }
            return null;
        }
    }
    static T GetBlock<T>(bool useSubgrids = false) where T : class, IMyTerminalBlock
    {
        List<T> blocks = GetBlocks<T>(useSubgrids);
        return blocks.FirstOrDefault();
    }
    static List<T> GetBlocks<T>(string groupName, bool useSubgrids = false) where T : class, IMyTerminalBlock
    {
        if (string.IsNullOrWhiteSpace(groupName))
            return GetBlocks<T>(useSubgrids);

        IMyBlockGroup group = gridSystem.GetBlockGroupWithName(groupName);
        List<T> blocks = new List<T>();
        group.GetBlocksOfType(blocks);
        if (!useSubgrids)
            blocks.RemoveAll(block => block.CubeGrid.EntityId != gridId);
        return blocks;

    }
    static List<T> GetBlocks<T>(bool useSubgrids = false) where T : class, IMyTerminalBlock
    {
        List<T> blocks = new List<T>();
        gridSystem.GetBlocksOfType(blocks);
        if (!useSubgrids)
            blocks.RemoveAll(block => block.CubeGrid.EntityId != gridId);
        return blocks;
    }*/
}