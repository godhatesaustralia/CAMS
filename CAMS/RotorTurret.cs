using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage.Extensions;
using VRageMath;

namespace IngameScript
{
    // fuck it
    public enum AimState
    {
        Offline,
        Manual,
        Rest,
        Blocked, // target out of bounds (sorry)
        Moving,
        OnTarget
    }

    // rotor hinge turret. fuck yopu. DYCJ YOIU
    public class RotorTurret
    {
        #region you aint built for these fields son

        const float RAD = (float)Math.PI / 180, DEG = 1 / RAD;
        const int RST_TKS = 23;
        public string Name; // yeah
        protected IMyMotorStator _azimuth, _elevation;
        public AimState Status { get; protected set; }
        protected float
            _aMx,
            _aMn,
            _aRest,
            _eMx,
            _eMn,
            _eRest; // absolute max and min azi/el for basic check
        protected double _tol; // aim tolerance
        public readonly double Range, TrackRange, Speed;
        public IMyTurretControlBlock _ctc;
        PCtrl _aPCtrl, _ePCtrl;
        SectorCheck[] _limits;
        protected IWeapons _weapons;
        protected Program _p;
        public long tEID = -1, lastUpdate = 0, _oobF = 0;
        public bool Inoperable = false, IsPDT, TgtSmall;
        public bool ActiveCTC => _ctc?.IsUnderControl ?? false;
        #endregion

        #region debugFields
        public double aRPM => _azimuth.TargetVelocityRPM;
        public double eRPM => _elevation.TargetVelocityRPM;
        public string AZ, EL, TGT;
        #endregion

        class SectorCheck
        {
            public readonly double
                aMn, aMx, eMn, eMx;

            public SectorCheck(string s)
            {
                var ar = s.Split(',');
                if (ar.Length != 4)
                    return;
                foreach (var st in ar) st.Trim();

                aMn = int.Parse(ar[0]) * RAD;
                aMx = int.Parse(ar[1]) * RAD;
                eMn = int.Parse(ar[2]) * RAD;
                eMx = int.Parse(ar[3]) * RAD;
            }
        }

        public RotorTurret(IMyMotorStator mtr, Program m)
        {
            _p = m;
            _azimuth = mtr;
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
                    var h = Lib.H;
                    Name = p.String(h, Lib.N, inv);
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
                    _azimuth.UpperLimitRad = _aMx = RAD * p.Float(h, "azMax", 361);
                    _azimuth.LowerLimitRad = _aMn = RAD * p.Float(h, "azMin", -361);
                    _aRest = RAD * p.Float(h, "azRst", 0);
                    _elevation.UpperLimitRad = _eMx = RAD * p.Float(h, "elMax", 90);
                    _elevation.LowerLimitRad = _eMn = RAD * p.Float(h, "elMin", -90);
                    _eRest = RAD * p.Float(h, "elRst", 0);
                    Range = p.Double(h, "range", 800);
                    TrackRange = 1.625 * Range;
                    Speed = p.Double(h, "speed", 400);
                    _tol = p.Double(h, "tolerance", 5E-4);
                    TgtSmall = p.Bool(h, "tgtSM", true);

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
                    _weapons = new Weapons(list, p.Int(h, "salvo"), p.Int(h, "offset"));

                    double a, e;
                    GetStatorAngles(out a, out e);
                    Status = MoveToRest(a, e);
                }
                else throw new Exception($"\nFailed to create turret using azimuth rotor {_azimuth.CustomName}.");
        }

        static void AdjustElevation(ref double val)
        {
            if (val < -Lib.HALF_PI)
                val += Lib.PI;
            else if (val > Lib.HALF_PI)
                val -= Lib.PI;
        }

        static void AdjustAzimuth(ref double val)
        {
            if (val < -Lib.PI)
            {
                val += Lib.PI2X;
                if (val < -Lib.PI) val += Lib.PI2X;
            }
            else if (val > Lib.PI)
            {
                val -= Lib.PI2X;
                if (val > Lib.PI) val -= Lib.PI2X;
            }
        }

        #region core-methods
        protected bool Interceptable(Target tgt, ref Vector3D aim, bool test = false)
        {
            if (!test)
                aim -= _p.Gravity;

            // okay so does acceleration even fucking work
            Vector3D
                rP = aim - _elevation.WorldMatrix.Translation,
                rV = tgt.Velocity - _p.Velocity,
                rA = tgt.Accel - _p.Acceleration;

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

            if (!test)
                aim += rA.LengthSquared() > 0.1 ? rV * t + 0.5 * rA * t * t : rV * t;

            return true;
        }

        // makes azimuth BEHAVE!!!!
        protected void GetStatorAngles(out double a, out double e)
        {
            a = _azimuth.Angle;
            if (a < -Lib.PI)
                a += Lib.PI2X;
            else if (a > Lib.PI)
                a -= Lib.PI2X;
            e = (_elevation.Angle + Lib.PI) % Lib.PI2X - Lib.PI;
        }

        protected AimState MoveToRest(double aCur, double eCur, bool reset = false)
        {
            if (Status == AimState.Rest)
                return Status;
            else if (ActiveCTC)
                return AimState.Manual;
            else if (Inoperable)
                return AimState.Offline;

            _weapons.Hold();

            if (reset)
            {
                tEID = -1;
                _oobF = 0;
                _p.Targets.MarkLost(tEID);
            }

            AZ = $"T{_aRest * DEG:+000;-000}°\nC{aCur * DEG:+000;-000}°";
            EL = $"T{_eRest * DEG:+000;-000}°\nC{eCur * DEG:+000;-000}°";
            TGT = "NONE";

            if (Math.Abs(aCur - _aRest) < _tol && Math.Abs(eCur - _eRest) < _tol)
            {
                _azimuth.TargetVelocityRad = _elevation.TargetVelocityRad = 0;
                return AimState.Rest;
            }
            _azimuth.TargetVelocityRad = _aPCtrl.Filter(aCur, _aRest, _p.F);
            _elevation.TargetVelocityRad = _ePCtrl.Filter(eCur, _eRest, _p.F);

            return AimState.Moving;
        }

        protected AimState AimAtTarget(ref MatrixD azm, ref Vector3D aim, double aCur, double eCur, bool test = false)
        {
            if (Status == AimState.Blocked)
            {
                _oobF++;
                _azimuth.TargetVelocityRad = _elevation.TargetVelocityRad = 0;
            }

            aim.Normalize();
            Vector3D
                eTgtV = Lib.Projection(aim, azm.Up), // projection of target pos on z axis (elevation)
                aTgtV = aim - eTgtV; // projection of target vector on xy plane (azimuth)

            // azimuth target angle
            var aTgt = Lib.AngleBetween(ref aTgtV, azm.Backward) * Math.Sign(aTgtV.Dot(azm.Left));

            if (aTgt > _aMx || aTgt < _aMn)
                return AimState.Blocked;

            // elevation target angle
            var eTgt = Lib.AngleBetween(ref aTgtV, ref aim) * Math.Sign(aim.Dot(azm.Up));

            if (eTgt > _eMx || eTgt < _eMn)
                return AimState.Blocked;

            // check whether these are prohibited angleS
            for (int i = 0; i < (_limits?.Length ?? 0); i++)
                if (_limits[i].aMn < aTgt && _limits[i].aMx > aTgt && _limits[i].eMn < eTgt && _limits[i].eMx > eTgt)
                {
                    _azimuth.TargetVelocityRad = _elevation.TargetVelocityRad = 0;
                    return AimState.Blocked;
                }

            if (test) return AimState.OnTarget;

            AZ = $"T{aTgt * DEG:+000;-000}°\nC{aCur * DEG:+000;-000}°";
            EL = $"T{eTgt * DEG:+000;-000}°\nC{eCur * DEG:+000;-000}°";

            _oobF = 0;
            _azimuth.TargetVelocityRad = _aPCtrl.Filter(aCur, aTgt, _p.F);
            _elevation.TargetVelocityRad = _ePCtrl.Filter(eCur, eTgt, _p.F);

            return Math.Abs(aTgt - aCur) < _tol && Math.Abs(eTgt - eCur) < _tol ? AimState.OnTarget : AimState.Moving;
        }

        #endregion

        public bool CanTarget(long eid)
        {
            if (Inoperable || ActiveCTC || !_p.Targets.Exists(eid))
                return false;

            var tgt = _p.Targets.Get(eid);
            if (tgt.Distance > TrackRange || (!TgtSmall && (int)tgt.Type == 2))
                return false;

            return Interceptable(tgt, ref tgt.Position, true);
        }

        public virtual void UpdateTurret()
        {
            double a, e;
            GetStatorAngles(out a, out e);

            if (_p.Targets.Count != 0 && !ActiveCTC)
            {
                var tgt = tEID != -1 ? _p.Targets.Get(tEID) : null;
                Inoperable = !_azimuth.IsAttached || !_elevation.IsAttached || !_azimuth.IsFunctional || !_elevation.IsFunctional;
                if (Inoperable || tgt == null)
                {
                    Status = Inoperable ? AimState.Offline : MoveToRest(a, e, true);
                    return;
                }

                var aim = tgt.Position;
                if (Interceptable(tgt, ref aim))
                {
                    var azm = _azimuth.WorldMatrix;
                    aim -= _weapons.AimPos;
                    var tgtDst = aim.Length();

                    if ((_oobF > RST_TKS && Status == AimState.Blocked) || tgtDst > TrackRange)
                    {
                        Status = MoveToRest(a, e);
                        return;
                    }

                    Status = AimAtTarget(ref azm, ref aim, a, e);
                    TGT = tgt.eIDTag;

                    if (tgtDst < Range && Status == AimState.OnTarget)
                    {
                        if (!tgt.Engaged)
                            _p.Targets.MarkEngaged(tEID);

                        _weapons.Fire(_p.F);
                    }
                    else _weapons.Hold();
                }
                else Status = MoveToRest(a, e);
            }
            else Status = MoveToRest(a, e, true);
        }
    }

    public class PDT : RotorTurret
    {
        IMyCameraBlock[] _designators;
        LidarArray _lidar;
        double _spray, _sprayTol = 7.5E-4;
        Vector3D _sprayOfs;
        bool _switchOfs = true, _useLidar = false;
        int _scanCtr, _scanMx;

        public PDT(IMyMotorStator a, Program m, int sMx) : base(a, m)
        {
            var g = _elevation.TopGrid;
            _scanMx = sMx;
            if (g != null)
            {
                var l = new List<IMyCameraBlock>();
                var d = new List<IMyCameraBlock>();
                m.Terminal.GetBlocksOfType(l, c =>
                {
                    if (c.CubeGrid == g)
                    {
                        if (c.CustomName.Contains(Lib.ARY))
                            return true;
                        else d.Add(c);
                    }
                    return false;
                });
                _lidar = new LidarArray(l);
                if (d.Count > 0)
                    _designators = d.ToArray();
                l.Clear();
            }
            _spray = m.PDSpray;
            //_tol += _spray != -1 ? _sprayTol : 0;
        }

        public bool AssignLidarTarget(Target t)
        {
            if (t == null || Inoperable)
                return false;

            double a, e;
            GetStatorAngles(out a, out e);

            var azm = _azimuth.WorldMatrix;
            var aim = t.Position - azm.Translation;

            bool r = AimAtTarget(ref azm, ref aim, a, e, true) == AimState.OnTarget;
            if (r)
            {
                _useLidar = r;
                tEID = t.EID;
            }
            return r;
        }

        public void Designate(bool track = false)
        {
            if (Inoperable || !ActiveCTC || _designators == null)
                return;
            for (int i = 0; i < _designators.Length; i++)
                if (_designators[i].CanScan(_scanMx))
                {
                    var t = _designators[i].Raycast(_scanMx);
                    if (_p.PassTarget(t))
                    {
                        tEID = track ? t.EntityId : tEID;
                        break;
                    }
                }
        }

        public override void UpdateTurret()
        {
            double a, e;
            GetStatorAngles(out a, out e);

            if (_p.Targets.Count != 0 && !ActiveCTC)
            {
                var tgt = tEID != -1 ? _p.Targets.Get(tEID) : null;
                Inoperable = !_azimuth.IsAttached || !_elevation.IsAttached || !_azimuth.IsFunctional || !_elevation.IsFunctional;
                if (Inoperable || tgt == null)
                {
                    Status = Inoperable ? AimState.Offline : MoveToRest(a, e, true);
                    return;
                }

                var aim = tgt.Position;
                if (_useLidar || Interceptable(tgt, ref aim)) // admittedly not the best way to do this
                {
                    var azm = _azimuth.WorldMatrix;
                    aim -= azm.Translation;
                    var tgtDst = aim.Length();

                    if (!_useLidar && _spray != -1)
                    {
                        if (_switchOfs)
                            _sprayOfs = _p.RandomOffset() * _spray;

                        aim += _sprayOfs * tgt.Radius / tgt.Distance;
                    }

                    if ((_oobF > 31 && Status == AimState.Blocked) || tgtDst > TrackRange)
                    {
                        Status = MoveToRest(a, e);
                        return;
                    }

                    Status = AimAtTarget(ref azm, ref aim, a, e);
                    TGT = tgt.eIDTag;

                    if (Status == AimState.OnTarget)
                    {
                        if (_useLidar)
                        {
                            var r = ScanResult.Failed;
                            for (_scanCtr = 0; _scanCtr++ < _scanMx && r == ScanResult.Failed;)
                                r = _lidar.Scan(_p, tgt, true);
                        }

                        if (tgtDst < Range)
                        {
                            if (!tgt.Engaged)
                                _p.Targets.MarkEngaged(tEID);

                            _weapons.Fire(_p.F);
                        }
                    }
                    else _weapons.Hold();
                }
                else Status = MoveToRest(a, e);
            }
            else Status = MoveToRest(a, e, true);
        }
    }
}