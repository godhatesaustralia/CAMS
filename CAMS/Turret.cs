using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRageMath;
using VRageRender;

namespace IngameScript
{
    // fuck it
    public enum AimState
    {
        Default,
        Rest,
        TargetOOB, // target out of bounds (sorry)
        Moving,
        OnTarget
    }

    // rotor hinge turret. fuck yopu. DYCJ YOIU
    public abstract class TurretBase
    {
        #region you aint built for these fields son

        protected const float rad = (float)Math.PI / 180;
        public string Name; // yeah
        protected IMyMotorStator _azimuth, _elevation;
        public MatrixD aziMat => _azimuth.WorldMatrix;
        public AimState aimState = AimState.Default;
        protected float
            _aMx,
            _aMn,
            _aRest,
            _eMx,
            _eMn,
            _eRest; // absolute max and min azi/el for basic check
        protected double _range, _speed, _tol; // last is aim tolerance
        public IMyTurretControlBlock _ctc;
        public double lastAzTgt, lastElTgt;
        PCtrl _aPCtrl, _ePCtrl;
        protected Weapons _weapons;
        protected Program _m;
        public long tEID = -1, lastUpdate = 0, oobF = 0;

        #endregion

        #region debugFields

        // public double _aTgt, _eTgt, _aCur, _eCur, aRPM, eRPM;
        public double aRPM => _azimuth.TargetVelocityRPM;
        public double eRPM => _elevation.TargetVelocityRPM;
        public double aCur => _azimuth.Angle / rad;
        public double eCur => _elevation.Angle / rad;
        #endregion

        protected TurretBase(IMyMotorStator a, Program m)
        {
            _m = m;
            _azimuth = a;
            if (_azimuth.Top == null)
                return;
            m.Terminal.GetBlocksOfType<IMyMotorStator>(null, b =>
            {
                if (b.CubeGrid == _azimuth.TopGrid)
                    _elevation = b;
                return true;
            });
            var inv = "~";
            using (var p = new iniWrap())
                if (p.CustomData(_azimuth) && _elevation != null)
                {
                    var h = Lib.HDR;
                    Name = p.String(h, "name", inv);
                    if (p.Bool(h, "ctc"))
                        m.Terminal.GetBlocksOfType<IMyTurretControlBlock>(null, b =>
                        {
                            if (b.CustomName.Contains(Name) ||
                            b.CubeGrid == _azimuth.CubeGrid ||
                            b.CubeGrid == _elevation.CubeGrid)
                                _ctc = b;
                            return true;
                        });

                    _azimuth.UpperLimitRad = _aMx = rad * p.Float(h, "azMax", 361);
                    _azimuth.LowerLimitRad = _aMn = rad * p.Float(h, "azMin", -361);
                    _aRest = rad * p.Float(h, "azRst", 0);
                    //                    _aRPM = p.Float(h, "azRPM", 20);
                    _elevation.UpperLimitRad = _eMx = rad * p.Float(h, "elMax", 90);
                    _elevation.LowerLimitRad = _eMn = rad * p.Float(h, "elMin", -90);
                    _eRest = rad * p.Float(h, "elRst", 0);
                    //                    _eRPM = p.Float(h, "elRPM", 20);
                    _range = p.Double(h, "range", 800);
                    _speed = p.Double(h, "speed", 400);
                    _tol = p.Double(h, "tolerance", 7.5E-4);

                    _aPCtrl = new PCtrl(AdjustAzimuth, 60, 1.125, p.Double(h, "azRLim", 60));
                    _ePCtrl = new PCtrl(AdjustElevation, 60, 1.125, p.Double(h, "elRLim", 60));

                    var list = new List<IMyUserControllableGun>();
                    m.Terminal.GetBlocksOfType(list, b => b.CubeGrid == _elevation.CubeGrid || b.CustomName.Contains(Name));
                    _weapons = new Weapons(p.Int(h, "salvo", -1), list);
                    _azimuth.TargetVelocityRad = _elevation.TargetVelocityRad = 0;
                }
                else throw new Exception($"\nFailed to create turret using azimuth rotor {_azimuth.CustomName}.");
        }

        static void AdjustElevation(ref double val)
        {
            if (val < -Lib.halfPi)
                val += Lib.Pi;
            else if (val > Lib.halfPi)
                val -= Lib.Pi;
        }
        static void AdjustAzimuth(ref double val)
        {
            if (val < -Lib.Pi)
            {
                val += Lib.Pi2;
                if (val < -Lib.Pi) val += Lib.Pi2;
            }
            else if (val > Lib.Pi)
            {
                val -= Lib.Pi2;
                if (val > Lib.Pi) val -= Lib.Pi2;
            }
        }

        bool WithinDeadzone(IMyMotorStator rotor, float min, float max)
        {
            if (max <= min)
            {
                return false;
            }

            float angleDeg = MathHelper.ToDegrees(rotor.Angle);

            float delta = 0;
            if (angleDeg < min)
            {
                delta = min - angleDeg;
            }
            else if (angleDeg > max)
            {
                delta = max - angleDeg;
            }

            float wraps = (float)Math.Round(delta / 360f);
            float wrappedAngle = angleDeg + wraps * 360f;

            return wrappedAngle > min && wrappedAngle < max;
        }


        // aim is an internal copy of CURRENT(!) target position
        // because in some cases (PDLR assistive targeting) we will only want to point at
        // the target directly and not lead (i.e. we will not be using this)
        protected bool Interceptable(Target tgt, ref Vector3D aim)
        {
            // thanks alysius <4
            Vector3D
                rP = aim - _weapons.AimPos,
                rV = tgt.Velocity - _m.Velocity;
            double
                a = _speed * _speed - rV.LengthSquared(),
                b = -2 * rV.Dot(rP),
                d = (b * b) - (4 * a * -rP.LengthSquared());
            if (d < 0) return false; // this indicates bad determinant for quadratic formula
            d = Math.Sqrt(d);
            double
                t1 = (-b + d) / (2 * a),
                t2 = (-b - d) / (2 * a),
                t = t1 > 0 ? (t2 > 0 ? (t1 < t2 ? t1 : t2) : t1) : t2;
            if (double.IsNaN(t)) return false;

            //aim = tgt.Accel.Length() < 0.5 ? aim + tgt.Velocity * t : aim + tgt.Velocity * t + 0.0625 * tgt.Accel * t;
            aim += rV * t;
            return true;
        }

        protected void GetCurrentAngles(ref MatrixD azm, out double aCur, out double eCur)
        {
            Vector3D
                guns = _weapons.AimPos.Normalized(),
                aVec = guns - Lib.Projection(guns, azm.Up);

            aCur = _azimuth.Angle;
            if (aCur < 0)
            {
                if (aCur <= -Lib.Pi2) aCur += MathHelperD.FourPi;
                else aCur += Lib.Pi2;
            }
            else if (aCur >= Lib.Pi2)
            {
                if (aCur >= MathHelperD.FourPi) aCur -= MathHelperD.FourPi;
                else aCur -= Lib.Pi2;
            }
            eCur = (_elevation.Angle + Lib.Pi) % Lib.Pi2 - Lib.Pi; ;
        }

        public void Reset()
        {
            if (aimState == AimState.Rest)
                return;
            if (_weapons.Active)
            {
                _weapons.Hold();
                _weapons.Update();
            }
            var azm = _azimuth.WorldMatrix;
            aimState = Rest(ref azm);
            lastAzTgt = lastElTgt = 0;
        }

        protected AimState Rest(ref MatrixD azm)
        {
            double aCur, eCur;
            GetCurrentAngles(ref azm, out aCur, out eCur);
            if (Math.Abs(aCur - _aRest) < _tol && Math.Abs(eCur - _eRest) < _tol)
            {
                _azimuth.TargetVelocityRad = _elevation.TargetVelocityRad = 0;
                return AimState.Rest;
            }

            _azimuth.TargetVelocityRad = _aPCtrl.Filter(aCur, _aRest, _m.F);
            _elevation.TargetVelocityRad = _ePCtrl.Filter(eCur, _eRest, _m.F);
            return AimState.Moving;
        }

        protected AimState AimAtTarget(ref MatrixD azm, ref Vector3D aim, double aCur, double eCur)
        {
            var a = aim;
            aim.Normalize();
            oobF++;
            Vector3D
                eTgtV = Lib.Projection(aim, azm.Up),
                aTgtV = aim - eTgtV;
            var aTgt = Lib.AngleBetween(aTgtV, azm.Backward) * Math.Sign(aTgtV.Dot(azm.Left));

            #region debugapi
            if (_m.F % 19 == 0)
            {
                var azmt = aziMat.Translation;
                var tg = azmt + a;
                _m.Debug.DrawPoint(tg, Lib.RED, 1);
                _m.Debug.DrawLine(azmt, tg, Lib.RED, 0.075f);
                //_m.Debug.DrawLine(azmt, azmt + aTgtV * 5, Lib.RED, 0.075f);
                //_m.Debug.DrawLine(azmt, azmt + eTgtV * 5, Lib.YEL, 0.075f);
            }
            #endregion

            if (aTgt > _aMx || aTgt < _aMn)
                return AimState.TargetOOB;

            var eTgt = Lib.AngleBetween(aTgtV, aim) * Math.Sign(aim.Dot(azm.Up));
            eCur = (eCur + Lib.Pi) % Lib.Pi2 - Lib.Pi;

            if (eTgt > _eMx || eTgt < _eMn)
                return AimState.TargetOOB;

            oobF = 0;
            _azimuth.TargetVelocityRad = _aPCtrl.Filter(aCur, aTgt, _m.F);
            _elevation.TargetVelocityRad = _ePCtrl.Filter(eCur, eTgt, _m.F);
            return Math.Abs(aTgt - aCur) < _tol && Math.Abs(eTgt - eCur) < _tol ? AimState.OnTarget : AimState.Moving;
        }
        public virtual void Update(ref List<Target> tgts)
        {
            if ((int)aimState == 1 && _m.Targets.Count == 0)
                return;
            var azm = aziMat;
            var aim = Vector3D.Zero;
            double aCur, eCur;
            GetCurrentAngles(ref azm, out aCur, out eCur);
            if (tEID == -1)
                foreach (var t in tgts)
                {
                    aim = t.Position;
                    if (Interceptable(t, ref aim))
                    {
                        tEID = t.EID;
                        aim -= azm.Translation;
                        aimState = AimAtTarget(ref azm, ref aim, aCur, eCur);
                        break;
                    }
                }
            else
            {
                var tgt = _m.Targets.Get(tEID);
                aim = tgt.Position;
                if (!Interceptable(tgt, ref aim))
                {
                    tEID = -1;
                    aimState = Rest(ref azm);
                }
                else
                {
                    aim -= azm.Translation;
                    aimState = AimAtTarget(ref azm, ref aim, aCur, eCur);
                }
            }
            if (oobF > 50 && aimState == AimState.TargetOOB)
            {
                aimState = Rest(ref azm);
                return;
            }
            if (aim.Length() < _range && aimState == AimState.OnTarget)
                _weapons.Fire();
            else _weapons.Hold();
            _weapons.Update();
        }
        // public abstract void Update(ref List<Target> tgts);
    }
    public class Turret : TurretBase
    {
        public Turret(IMyMotorStator a, Program m) : base(a, m)
        { }
    }
    public class PDC : TurretBase
    {
        public LidarArray Lidar;
        public PDC(IMyMotorStator a, Program m) : base(a, m)
        {
            var g = _elevation.TopGrid;
            if (g != null)
            {
                var l = new List<IMyCameraBlock>();
                m.Terminal.GetBlocksOfType(l, c => c.CubeGrid == g && c.CustomName.Contains(Lib.ARY));
                Lidar = new LidarArray(l);
            }

        }

    }
}