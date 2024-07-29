using Sandbox.Game.GameSystems;
using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using System;
using VRageMath;
using System.Runtime.Versioning;

namespace IngameScript
{
    public class Autoaim : CompBase
    {
        //static IMyGridTerminalSystem gridSystem;
        // replace with m

        static double 
            kP = 16,
            kI = 0,
            kD = 32,
            lowerBound = -1000,
            upperBound = 1000,
            decay = 0,
            timeStep = 1.0 / 60;
        Weapons _guns;
        Vector3D toPoint = new Vector3D();
        bool doPoint = false;
        List<IMyGyro> gyros = new List<IMyGyro>();
        PID yaw, pitch, roll;
        // implement for CompBase wip
        public override void Update(UpdateFrequency f)
        {
            // er what does this even do?
            // it is basically the set of stuff to run each _main loop (primary function of subsyeestem

            // old arg loop
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
                gyros.Turn(-Vector3D.Normalize(toPoint - Me.CubeGrid.GetPosition()), t.WorldMatrix.Up);*/
            // leftover bullshit fix later

            //todo: 
            //call set up
            return;
        }

        //under our glorious black sun... our beloved leader big vlad harkonenn... presiding over this spectacle of cringe, and debug
        public override void Setup(Program m)
        {
            Main = m;
            m.Terminal.GetBlocksOfType(gyros, b => b.IsSameConstructAs(m.Controller));
            using (var p = new iniWrap())
                if (p.CustomData(m.Me))
                {
                    var grp = m.Terminal.GetBlockGroupWithName(p.String(Lib.H, "wpnGroup", "CAMS Aim"));
                    if (grp != null)
                    {
                        var l = new List<IMyUserControllableGun>();
                        grp.GetBlocksOfType(l);
                        _guns = new Weapons(p.Int(Lib.H, "aimSalvoTicks"), l);
                    }
                    else Frequency = UpdateFrequency.None;
                }
            //intialize gyros 
            //initialize guns..?
            //grab remote control for GyroControl 

            return;
        }
        //end of implement for CompBase
        public Autoaim(string n) : base(n, Lib.u1 | Lib.u10 | Lib.u100)
        {
            //guar.....
            yaw = new PID(kP, kI, kD, lowerBound, upperBound, decay, timeStep);
            pitch = new PID(kP, kI, kD, lowerBound, upperBound, decay, timeStep);
            roll = new PID(kP, kI, kD, lowerBound, upperBound, decay, timeStep);
        }

        public void Turn(Vector3D forward, Vector3D up)
        {
            // In (pitch, yaw, roll)
            Vector3D
                error = -GetAngles(Main.Controller.WorldMatrix, ref forward, ref up),
                angles = new Vector3D(Control(ref error));
            ApplyOverride(Main.Controller.WorldMatrix, ref angles);
        }

        public void FaceDirection(ref Vector3D aim)
        {
            var grav = Main.Gravity != Vector3D.Zero;
            
        }

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

            yaw.Reset();
            pitch.Reset();
            roll.Reset();
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

        Vector3D Control(ref Vector3D err) => new Vector3D(yaw.Control(err.X), pitch.Control(err.Y), roll.Control(err.Z));

        Vector3D GetAngles(MatrixD current, ref Vector3D forward, ref Vector3D up)
        {
            var error = new Vector3D();
            if (forward != Vector3D.Zero)
            {
                Quaternion 
                    quat = Quaternion.CreateFromForwardUp(current.Forward, current.Up),
                    invQuat = Quaternion.Inverse(quat);
                var ReferenceFrameVector = Vector3D.Transform(forward, invQuat); //Target Vector In Terms Of RC Block

                //Convert To Local Azimuth And Elevation
                Vector3D.GetAzimuthAndElevation(ReferenceFrameVector, out error.Y, out error.X);
            }

            if (up != Vector3D.Zero)
            {
                Vector3D
                    temp = Vector3D.Normalize(Lib.Rejection(up, Main.Controller.WorldMatrix.Forward)),
                    rgt = Main.Controller.WorldMatrix.Right;
                double
                    dot = MathHelper.Clamp(Vector3D.Dot(Main.Controller.WorldMatrix.Up, temp), -1, 1),
                    rollAngle = Math.Acos(dot),
                    scalar = temp.Dot(rgt);
                    scalar = double.IsNaN(scalar) ? 0 : scalar;
                if (scalar > 0)
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

        void ApplyOverride(MatrixD current, ref Vector3D localAngles)
        {
            var worldAngles = Vector3D.TransformNormal(localAngles, current);
            foreach (IMyGyro g in gyros)
            {
                var transVect = Vector3D.TransformNormal(worldAngles, MatrixD.Transpose(g.WorldMatrix));  //Converts To Gyro Local
                if (!transVect.IsValid())
                    throw new Exception("Invalid trans vector. " + transVect.ToString());

                g.Pitch = (float)transVect.X;
                g.Yaw = (float)transVect.Y;
                g.Roll = (float)transVect.Z;
                g.GyroOverride = true;
            }
        }

    }

}