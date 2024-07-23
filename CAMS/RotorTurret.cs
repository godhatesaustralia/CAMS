using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        Blocked, // target out of bounds (sorry)
        Moving,
        OnTarget
    }

    // rotor hinge turret. fuck yopu. DYCJ YOIU
    public class RotorTurret
    {
        #region you aint built for these fields son

        const float rad = (float)Math.PI / 180;
        public string Name; // yeah
        protected IMyMotorStator _azimuth, _elevation;
        public MatrixD aziMat => _azimuth.WorldMatrix;
        public AimState Status { get; protected set; }
        protected float
            _aMx,
            _aMn,
            _aRest,
            _eMx,
            _eMn,
            _eRest; // absolute max and min azi/el for basic check
        protected double _tol; // last is aim tolerance
        public readonly double Range, TrackRange, Speed;
        public IMyTurretControlBlock _ctc;
        PCtrl _aPCtrl, _ePCtrl;
        SectorCheck[] _limits;
        protected Weapons _weapons;
        protected Program _m;
        public long tEID = -1, lastUpdate = 0, oobF = 0;
        public bool Inoperable = false, IsPDT;
        #endregion

        #region debugFields
        public double aRPM => _azimuth.TargetVelocityRPM;
        public double eRPM => _elevation.TargetVelocityRPM;
        public double aCur => _azimuth.Angle / rad;
        public double eCur => _elevation.Angle / rad;
        #endregion

        class SectorCheck
        {
            public double
                aMn, aMx, eMn, eMx;

            public SectorCheck(string s)
            {
                var ar = s.Split(',');
                if (ar.Length != 4)
                    return;
                foreach (var st in ar) st.Trim();

                aMn = int.Parse(ar[0]) * rad;
                aMx = int.Parse(ar[1]) * rad;
                eMn = int.Parse(ar[2]) * rad;
                eMx = int.Parse(ar[3]) * rad;
            }
        }

        public RotorTurret(IMyMotorStator a, Program m)
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

                    IsPDT = this is PDT;
                    _azimuth.UpperLimitRad = _aMx = rad * p.Float(h, "azMax", 361);
                    _azimuth.LowerLimitRad = _aMn = rad * p.Float(h, "azMin", -361);
                    _aRest = rad * p.Float(h, "azRst", 0);
                    _elevation.UpperLimitRad = _eMx = rad * p.Float(h, "elMax", 90);
                    _elevation.LowerLimitRad = _eMn = rad * p.Float(h, "elMin", -90);
                    _eRest = rad * p.Float(h, "elRst", 0);
                    Range = p.Double(h, "range", 800);
                    TrackRange = 1.5 * Range;
                    Speed = p.Double(h, "speed", 400);
                    _tol = p.Double(h, "tolerance", 5E-4);

                    _aPCtrl = new PCtrl(AdjustAzimuth, p.Int(h, "azGainOut", 60), 1.0625, p.Double(h, "azRLim", 60));
                    _ePCtrl = new PCtrl(AdjustElevation, p.Int(h, "elGainOut", 60), 1.0625, p.Double(h, "elRLim", 60));

                    var sct = p.String(h, "sectors");
                    if (sct != "")
                    {
                        var ary = sct.Split('\n');
                        _limits = new SectorCheck[ary.Length];
                        for (int i = 0; i < ary.Length; i++)
                            try
                            {
                                _limits[i] = new SectorCheck(ary[i].Trim('|'));
                            }
                            catch
                            {
                                throw new Exception($"failed sector creation : {sct} not valid on turret {Name}");
                            }
                    }

                    var list = new List<IMyUserControllableGun>();
                    m.Terminal.GetBlocksOfType(list, b => b.CubeGrid == _elevation.CubeGrid || b.CustomName.Contains(Name));
                    _weapons = new Weapons(p.Int(h, "salvo", -1), list);
                    ResetTurret();
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

        #region core-methods

        // aim is an internal copy of CURRENT(!) target position
        // because in some cases (PDLR assistive targeting) we will only want to point at
        // the target directly and not lead (i.e. we will not be using this)
        protected virtual bool Interceptable(Target tgt, ref Vector3D aim, bool testOnly = false)
        {
            Vector3D
                rP = aim - _weapons.AimPos,
                rV = tgt.Velocity - _m.Velocity,
                rA = tgt.Accel - _m.Acceleration;

            double
                a = Speed * Speed - rV.LengthSquared(),
                b = -2 * rV.Dot(rP),
                d = (b * b) - (4 * a * -rP.LengthSquared());

            if (d < 0) return false; // this indicates bad determinant for quadratic formula

            d = Math.Sqrt(d);
            double
                t1 = (-b + d) / (2 * a),
                t2 = (-b - d) / (2 * a),
                t = t1 > 0 ? (t2 > 0 ? (t1 < t2 ? t1 : t2) : t1) : t2;

            if (double.IsNaN(t)) return false;

            if (testOnly)
                return true;

            aim += rV * t + 0.375 * rA * t * t;
            return true;
        }

        // makes azimuth BEHAVE!!!!
        protected void Lim2PiLite(ref double a)
        {
            if (a < 0)
            {
                if (a <= -Lib.Pi2) a += MathHelperD.FourPi;
                else a += Lib.Pi2;
            }
            else if (a >= Lib.Pi2)
            {
                if (a >= MathHelperD.FourPi) a -= MathHelperD.FourPi;
                else a -= Lib.Pi2;
            }
        }
        protected AimState MoveToRest()
        {
            double
                a = _azimuth.Angle,
                e = (_elevation.Angle + Lib.Pi) % Lib.Pi2 - Lib.Pi;
            Lim2PiLite(ref a);
            if (Math.Abs(a - _aRest) < _tol && Math.Abs(e - _eRest) < _tol)
            {
                _azimuth.TargetVelocityRad = _elevation.TargetVelocityRad = 0;
                return AimState.Rest;
            }
            _m.Debug.DrawGPS($"{Name}\naCur {aCur / rad}°\neCur {eCur / rad}°\n{Status}", aziMat.Translation, Lib.YEL);
            _azimuth.TargetVelocityRad = _aPCtrl.Filter(a, _aRest, _m.F);
            _elevation.TargetVelocityRad = _ePCtrl.Filter(e, _eRest, _m.F);

            return AimState.Moving;
        }

        protected AimState AimAtTarget(ref MatrixD azm, ref Vector3D aim, double aCur, double eCur)
        {
            if (Status == AimState.Blocked)
                oobF++;

            aim.Normalize();
            Vector3D
                eTgtV = Lib.Projection(aim, azm.Up), // projection of target pos on z axis (elevation)
                aTgtV = aim - eTgtV; // projection of target vector on xy plane (azimuth)

            // azimuth target angle
            var aTgt = Lib.AngleBetween(ref aTgtV, azm.Backward) * Math.Sign(aTgtV.Dot(azm.Left));
            Lim2PiLite(ref aTgt);

            if (aTgt > _aMx || aTgt < _aMn)
                return AimState.Blocked;

            // elevation target angle
            var eTgt = Lib.AngleBetween(ref aTgtV, ref aim) * Math.Sign(aim.Dot(azm.Up));
            eCur = (eCur + Lib.Pi) % Lib.Pi2 - Lib.Pi;
            //_m.Debug.DrawGPS($"{Name}\naTgt/Cur {aTgt / rad}°|{aCur / rad}°\neTgt/Cur {eTgt / rad}°|{eCur / rad}°\n{Status}", azm.Translation, Lib.YEL);
            if (eTgt > _eMx || eTgt < _eMn)
                return AimState.Blocked;

            // check whether these are prohibited angleS
            for (int i = 0; i < (_limits?.Length ?? 0); i++)
                if (_limits[i].aMn < aTgt && _limits[i].aMx > aTgt && _limits[i].eMn < eTgt && _limits[i].eMx > eTgt)
                {
                    return AimState.Blocked;
                }
            oobF = 0;
            _azimuth.TargetVelocityRad = _aPCtrl.Filter(aCur, aTgt, _m.F);
            _elevation.TargetVelocityRad = _ePCtrl.Filter(eCur, eTgt, _m.F);

            return Math.Abs(aTgt - aCur) < _tol && Math.Abs(eTgt - eCur) < _tol ? AimState.OnTarget : AimState.Moving;
        }

        #endregion


        public AimState ResetTurret()
        {
            if (Status != AimState.Rest)
            {
                tEID = 0;
                _weapons.Hold();
                var azm = _azimuth.WorldMatrix;
                Status = MoveToRest();
            }
            return Status;
        }

        public bool CanTarget(long eid)
        {
            if (Inoperable)
                return false;
            var tgt = _m.Targets.Get(eid);
            if (tgt == null || tgt.Distance > TrackRange)
                return false;

            return Interceptable(tgt, ref tgt.Position, true);
        }

        public virtual void UpdateTurret()
        {
            if (_m.Targets.Count != 0)
            {
                var tgt = _m.Targets.Get(tEID);
                Inoperable = !_azimuth.IsAttached || !_elevation.IsAttached || !_azimuth.IsFunctional || !_elevation.IsFunctional;
                if (tgt == null || Inoperable)
                    return;
                var aim = tgt.Position;
                var tgtDst = -1d;
                bool icpt = Interceptable(tgt, ref aim);
                if (icpt)
                {
                    var azm = aziMat;
                    aim -= azm.Translation;
                    tgtDst = aim.Length();

                    double
                        a = _azimuth.Angle,
                        e = (_elevation.Angle + Lib.Pi) % Lib.Pi2 - Lib.Pi;
                    Lim2PiLite(ref a);
                    Status = AimAtTarget(ref azm, ref aim, a, e);
                    if (oobF > 50 && Status == AimState.Blocked)
                    {
                        Status = ResetTurret();
                        return;
                    }
                    if (tgtDst < Range && Status == AimState.OnTarget)
                        _weapons.Fire(_m.F);
                    else _weapons.Hold();
                }
                else
                {
                    _weapons.Hold();
                    Status = MoveToRest();
                }
                return;
            }
            if (Status != AimState.Rest)
                Status = ResetTurret();
        }
    }

    public class PDT : RotorTurret
    {
        public LidarArray Lidar;
        CompBase _base;
        double _spray, _sprayTol = 7.5E-4;
        Vector3D _sprayOfs;
        bool switchOfs = true, lidarTarget = false;
        int scanCtr, scanMx;

        public PDT(IMyMotorStator a, Program m, CompBase b, int sMx) : base(a, m)
        {
            var g = _elevation.TopGrid;
            _base = b;
            scanMx = sMx;
            if (g != null)
            {
                var l = new List<IMyCameraBlock>();
                m.Terminal.GetBlocksOfType(l, c => c.CubeGrid == g && c.CustomName.Contains(Lib.ARY));
                Lidar = new LidarArray(l);
            }
            _spray = m.PDSpray;
            _tol += _spray != -1 ? _sprayTol : 0;
        }

        public void AssignLidarTarget(long eid)
        {
            lidarTarget = true;
            tEID = eid;
        }

        public override void UpdateTurret()
        {
            if (_m.Targets.Count != 0)
            {
                var tgt = _m.Targets.Get(tEID);
                Inoperable = !_azimuth.IsAttached || !_elevation.IsAttached || !_azimuth.IsFunctional || !_elevation.IsFunctional;
                if (tgt == null || Inoperable)
                    return;

                var aim = tgt.Position;
                if (!lidarTarget && _spray != -1)
                {
                    if (switchOfs)
                        _sprayOfs = Lib.RandomOffset(ref _m.RNG, _spray) * tgt.Radius / tgt.Distance;
                    aim += _sprayOfs;
                }
                var tgtDst = -1d;
                bool icpt = Interceptable(tgt, ref aim);
                if (icpt)
                {
                    var azm = aziMat;
                    aim -= azm.Translation;
                    tgtDst = aim.Length();

                    double
                        a = _azimuth.Angle,
                        e = (_elevation.Angle + Lib.Pi) % Lib.Pi2 - Lib.Pi;
                    Lim2PiLite(ref a);
                    Status = AimAtTarget(ref azm, ref aim, a, e);
                    if ((oobF > 50 && Status == AimState.Blocked) || tgtDst > TrackRange)
                    {
                        Status = ResetTurret();
                        return;
                    }
                    if (lidarTarget)
                    {
                        var r = ScanResult.Failed;
                        if (Status == AimState.OnTarget)
                            for (scanCtr = 0; scanCtr < scanMx && r == ScanResult.Failed; scanCtr++)
                                r = Lidar.Scan(_base, tgt, true);
                    }
                    else if (tgtDst < Range && Status == AimState.OnTarget)
                    {
                        _weapons.Fire(_m.F);
                        switchOfs = _weapons.offsetTicks == 0;
                    }
                    else _weapons.Hold();
                }
                else
                {
                    _weapons.Hold();
                    Status = MoveToRest();
                }
            }
            else if (Status != AimState.Rest)
                Status = ResetTurret();
        }
    }
}