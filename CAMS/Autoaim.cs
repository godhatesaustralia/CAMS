using Sandbox.Game.GameSystems;
using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using System;
using VRageMath;
using System.Runtime.Versioning;

namespace IngameScript
{
    #if AIMASSIST
    public partial class Program : MyGridProgram
    {

        public class PID
        {
            double
                _kP = 0,
                _kI = 0,
                _kD = 0,
                _intDecayRatio = 0,
                _lower,
                _upper,
                _timestep = 0,
                _invTS = 0,
                _errorSum = 0,
                _lastError = 0;
            bool
                _first = true,
                _decay = false;

            public double Value { get; private set; }

            // aimbot
            public PID(double kP, double kI, double kD, double lBnd, double uBnd, double decay, double ts)
            {
                _kP = kP;
                _kI = kI;
                _kD = kD;
                _lower = lBnd;
                _upper = uBnd;
                _timestep = ts;
                _invTS = 1 / _timestep;
                _intDecayRatio = decay;
                _decay = true;
            }

            public double Control(double error)
            {
                if (double.IsNaN(error)) return 0;

                //Compute dI term
                var errorDerivative = (error - _lastError) * _invTS;

                if (_first)
                {
                    errorDerivative = 0;
                    _first = false;
                }

                //Compute integral term
                if (!_decay)
                    _errorSum += error * _timestep;
                else
                    _errorSum = _errorSum * (1.0 - _intDecayRatio) + error * _timestep;

                //Store this error as last error
                _lastError = error;

                //Construct output
                Value = _kP * error + _kI * _errorSum + _kD * errorDerivative;
                return Value;
            }

            public double Control(double error, double timeStep)
            {
                _timestep = timeStep;
                _invTS = 1 / _timestep;
                return Control(error);
            }

            public void Reset()
            {
                _errorSum = 0;
                _lastError = 0;
                _first = true;
            }
        }
    }
    #endif
    public class Autoaim
    {
        //static IMyGridTerminalSystem gridSystem;
        // replace with m


        Program _p;
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
        //PID yaw, pitch, roll;
        // implement for CompBase wip
        public void Update()
        {
            if (doPoint)
                toPoint = _p.Targets.Prioritized.Min.Position;
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
        public void Setup(Program m)
        {
            _p = m;
            m.Terminal.GetBlocksOfType(gyros, b => b.IsSameConstructAs(m.Controller));
            using (var p = new iniWrap())
                if (p.CustomData(m.Me))
                {
                    var grp = m.Terminal.GetBlockGroupWithName(p.String(Lib.H, "wpnGroup", "CAMS Aim"));
                    if (grp != null)
                    {
                        var l = new List<IMyUserControllableGun>();
                        grp.GetBlocksOfType(l);
                        _guns = new Weapons(l, p.Int(Lib.H, "aimSalvoTicks"));
                    }
                }
            //intialize gyros 
            //initialize guns..?
            //grab remote control for GyroControl 

            return;
        }
        //end of implement for CompBase
        public Autoaim(string n)
        {
            //guar.....
           // yaw = new PID(kP, kI, kD, lowerBound, upperBound, decay, timeStep);
            //pitch = new PID(kP, kI, kD, lowerBound, upperBound, decay, timeStep);
            //roll = new PID(kP, kI, kD, lowerBound, upperBound, decay, timeStep);
        }

        public void Turn(Vector3D forward, Vector3D up)
        {
            // In (pitch, yaw, roll)
            Vector3D
                error = -GetAngles(_p.Controller.WorldMatrix, ref forward, ref up);
                //angles = new Vector3D(Control(ref error));
            //ApplyOverride(_p.Controller.WorldMatrix, ref angles);
        }

        public void FaceDirection(ref Vector3D aim)
        {
            var grav = _p.Gravity != Vector3D.Zero;

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

            //yaw.Reset();
            //pitch.Reset();
           //roll.Reset();
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

        //Vector3D Control(ref Vector3D err) => new Vector3D(yaw.Control(err.X), pitch.Control(err.Y), roll.Control(err.Z));

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
                    temp = Vector3D.Normalize(Lib.Rejection(up, _p.Controller.WorldMatrix.Forward)),
                    rgt = _p.Controller.WorldMatrix.Right;
                double
                    dot = MathHelper.Clamp(Vector3D.Dot(_p.Controller.WorldMatrix.Up, temp), -1, 1),
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