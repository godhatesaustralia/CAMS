using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{

    [Flags]
    public enum AimState
    {
        Offline,
        Holding,
        Resting,
        Blocked,
        Moving,
        OnTarget = 8 // when i uh. er
    }

    // rotor hinge turret. fuck yopu. DYCJ YOIU
    public class RotorTurret
    {
        #region you aint built for these fields son

        const float RAD = (float)Math.PI / 180, DEG = 1 / RAD, OFS_TAN_HZ = 0.0091387f;
        // https://github.com/wellstat/SpaceEngineers/blob/master/IngameScripts/DiamondDomeDefense.cs#L3903

        public string Name, AZ, EL, TGT; // yeah
        protected IMyMotorStator _azimuth, _elevation;
        public AimState Status { get; protected set; }

        protected int _ofsIdx;
        protected float
            _aMx,
            _aMn,
            _aRest,
            _eMx,
            _eMn,
            _eRest; // absolute max and min azi/el for basic check
        protected double _tol, _ofsAmt, _ofsMov; // aim tolerance, offset amount
        public readonly double Range, TrackRange, Speed;
        public IMyTurretControlBlock _ctc;
        PCtrl _aPCtrl, _ePCtrl;
        SectorCheck[] _limits;
        protected Weapons _weapons;
        protected Program _p;
        protected Vector3D _ofsStart, _ofsEnd;
        public long TEID = -1, LastTEID, LastF, BlockF;
        public bool Inoperable = false, IsPDT, TgtSmall, UseLidar = false;
        public bool ActiveCTC => _ctc?.IsUnderControl ?? false;
        #endregion

        #region debugFields
        public double ARPM => _azimuth.TargetVelocityRPM;
        public double ERPM => _elevation.TargetVelocityRPM;
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
                    Status = Idle(a, e, true);
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

            t += tgt.Elapsed(_p.F);
            if (!test)
                aim += rA.LengthSquared() > 0.1 ? rV * t + 0.25 * rA * t * t : rV * t;

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

        protected void SetAndMoveStators(double ac, double at, double ec, double et)
        {
            _azimuth.UpperLimitRad = _aMx;
            _azimuth.LowerLimitRad = _aMn;
            _elevation.UpperLimitRad = _eMx;
            _elevation.LowerLimitRad = _eMn;

            if (at - ac > 0.01)
                _azimuth.UpperLimitRad = (float)(at + 0.01);
            else if (at - ac < -0.01)
                _azimuth.LowerLimitRad = (float)(at - 0.01);

            if (et - ec > 0.01)
                _elevation.UpperLimitRad = (float)(et + 0.01);
            else if (et - ec < -0.01)
                _elevation.LowerLimitRad = (float)(et - 0.01);

            _azimuth.TargetVelocityRad = _aPCtrl.Filter(ac, at, _p.F);
            _elevation.TargetVelocityRad = _ePCtrl.Filter(ec, et, _p.F);
        }

        protected AimState Idle(double aCur, double eCur, bool reset = false)
        {
            LastF = _p.F;

            if ((Status & AimState.Blocked) != 0)
                return Status;
            else if (Inoperable)
                return AimState.Offline;

            _weapons.Hold();
             _p.Targets.MarkLost(TEID);

            if (reset)
            {
                LastTEID = TEID;
                TEID = -1;
                BlockF = 0;
            }

            AZ = $"{_aRest * DEG:+000;-000}°\n{aCur * DEG:+000;-000}°";
            EL = $"{_eRest * DEG:+000;-000}°\n{eCur * DEG:+000;-000}°";
            TGT = "CLEAR";

            if (!reset || (Math.Abs(aCur - _aRest) < _tol && Math.Abs(eCur - _eRest) < _tol))
            {
                _azimuth.TargetVelocityRad = _elevation.TargetVelocityRad = 0;
                return !reset ? AimState.Holding : AimState.Resting;
            }

            SetAndMoveStators(aCur, _aRest, eCur, _eRest);

            return AimState.Moving;
        }

        protected AimState AimAtTarget(ref MatrixD azm, ref Vector3D aim, double aCur, double eCur, bool test = false)
        {
            if (Status == AimState.Blocked)
            {
                BlockF += _p.F - LastF;
                _azimuth.TargetVelocityRad = _elevation.TargetVelocityRad = 0;
            }
            LastF = _p.F;
            aim.Normalize();
            Vector3D
                eTgtV = Lib.Projection(aim, azm.Up), // projection of target pos on z axis (elevation)
                aTgtV = aim - eTgtV; // projection of target vector on xy plane (azimuth)

            double aTgt, aChk, eTgt;

            // azimuth target angle
            aTgt = aChk = Lib.AngleBetween(ref aTgtV, azm.Backward) * Math.Sign(aTgtV.Dot(azm.Left));

            if (aTgt > _aMx || aTgt < _aMn)
                return AimState.Blocked;

            if (aTgt < 0)
                aChk += Lib.PI2X;
            else if (aTgt > Lib.PI2X)
                aChk -= Lib.PI2X;

            // elevation target angle
            eTgt = Lib.AngleBetween(ref aTgtV, ref aim) * Math.Sign(aim.Dot(azm.Up));

            if (eTgt > _eMx || eTgt < _eMn)
                return AimState.Blocked;

            // check whether these are prohibited angleS
            for (int i = 0; i < (_limits?.Length ?? 0); i++)
                if (_limits[i].aMn < aChk && _limits[i].aMx > aChk && _limits[i].eMn < eTgt && _limits[i].eMx > eTgt)
                {
                    _azimuth.TargetVelocityRad = _elevation.TargetVelocityRad = 0;
                    return AimState.Blocked;
                }

            if (test) return 0;

            AZ = $"{aTgt * DEG:+000;-000}°\n{aCur * DEG:+000;-000}°";
            EL = $"{eTgt * DEG:+000;-000}°\n{eCur * DEG:+000;-000}°";

            BlockF = 0;

            SetAndMoveStators(aCur, aTgt, eCur, eTgt);
            //bool r = Math.Abs(aTgt - aCur) < _tol && Math.Abs(eTgt - eCur) < _tol;
            bool r = _weapons.AimDir.Dot(aim) > 0.995;
            return r ? AimState.OnTarget : AimState.Moving;
        }

        protected Vector3D GetAimPoint(Target t, ref MatrixD az)
        {
            if ((int)t.Type == 2 || t.HitPoints.Count == 0) return t.Hit;

            bool reset = false;

            if (LastTEID != TEID)
            {
                LastTEID = TEID;
                _ofsIdx = 0;

                _ofsStart = Vector3D.TransformNormal(t.Hit - t.Center, MatrixD.Transpose(t.Matrix));
                _ofsEnd = t.HitPoints[0].Hit;

                reset = true;
            }
            else if (_ofsAmt > 1)
            {
                Lib.Next(ref _ofsIdx, t.HitPoints.Count);

                _ofsStart = _ofsEnd;
                _ofsEnd = t.HitPoints[_ofsIdx].Hit;

                reset = true;
            }

            if (reset)
            {
                var dst = (_ofsStart - _ofsEnd).Length();
                _ofsMov = dst < 1 ? Lib.TPS : Lib.TPS * dst * OFS_TAN_HZ * (t.Center - az.Translation).Length();
                _ofsAmt = 0;
            }

            _ofsAmt += (_p.F - LastF) * _ofsMov;
            return t.Center + Vector3D.TransformNormal(Vector3D.Lerp(_ofsStart, _ofsEnd, _ofsAmt), t.Matrix);
        }

        #endregion

        public bool CanTarget(long eid)
        {
            if (Inoperable || ActiveCTC || !_p.Targets.Exists(eid))
                return false;

            var tgt = _p.Targets.Get(eid);
            if ((!UseLidar && tgt.Distance > TrackRange) || (!TgtSmall && (int)tgt.Type == 2))
                return false;

            return Interceptable(tgt, ref tgt.Center, true);
        }

        public virtual void UpdateTurret()
        {
            if (ActiveCTC || Status == 0) return;

            double a, e;
            GetStatorAngles(out a, out e);

            if (_p.Targets.Count != 0)
            {
                var tgt = TEID != -1 ? _p.Targets.Get(TEID) : null;
                Inoperable = !_azimuth.IsAttached || !_elevation.IsAttached || !_azimuth.IsFunctional || !_elevation.IsFunctional;
                if (Inoperable || tgt == null)
                {
                    Status = Inoperable ? AimState.Offline : Idle(a, e, true);
                    return;
                }

                var azm = _azimuth.WorldMatrix;
                var aim = GetAimPoint(tgt, ref azm);
                if (Interceptable(tgt, ref aim))
                {      
                    aim -= _elevation.WorldMatrix.Translation; // ????? i have no fucking idea at this point is it htis????
                    var tgtDst = aim.Length();

                    if (tgtDst > TrackRange)
                    {
                        Status = Idle(a, e);
                        return;
                    }

                    Status = AimAtTarget(ref azm, ref aim, a, e);
                    TGT = tgt.eIDTag;

                    if (tgtDst < Range && Status == AimState.OnTarget)
                    {
                        if (!tgt.Engaged) _p.Targets.MarkEngaged(TEID);

                        _weapons.Fire(_p.F);
                    }
                    else _weapons.Hold();
                }
                else Status = Idle(a, e);
            }
            else Status = Idle(a, e, true);
        }
    }

    public class PDT : RotorTurret
    {
        IMyCameraBlock[] _designators;
        LidarArray _lidar;
        double _spray;
        Vector3D _sprayOfs;
        bool _ofsLidar;
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
                {
                    _designators = new IMyCameraBlock[d.Count];
                    for (int i = 0; i < d.Count; i++)
                    {
                        d[i].EnableRaycast = true;
                        _designators[i] = d[i];
                    }
                }
            }
            _spray = m.PDSpray;
        }

        public bool AssignLidarTarget(Target t, bool offset = false)
        {
            if (t == null || Inoperable) return false;

            double a, e;
            GetStatorAngles(out a, out e);

            var azm = _azimuth.WorldMatrix;
            var aim = t.Center - azm.Translation;

            bool r = AimAtTarget(ref azm, ref aim, a, e, true) == 0;
            if (r)
            {
                UseLidar = r;
                _ofsLidar = offset;
                LastTEID = TEID;
                TEID = t.EID;
            }
            return r;
        }

        public void Designate(bool track = false)
        {
            if (Inoperable || _designators == null) return;
            
            for (int i = 0; i < _designators.Length; i++)
                if (_designators[i].CanScan(_scanMx))
                {
                    var t = _designators[i].Raycast(_scanMx);
                    if (_p.PassTarget(t, true))
                    {
                        if (track) AssignLidarTarget(_p.Targets.Get(t.EntityId));
                        break;
                    }
                }
        }

        public override void UpdateTurret()
        {
            if (ActiveCTC || Status == 0) return;

            double a, e;
            GetStatorAngles(out a, out e);

            if (_p.Targets.Count != 0)
            {
                var tgt = TEID != -1 ? _p.Targets.Get(TEID) : null;
                Inoperable = !_azimuth.IsAttached || !_elevation.IsAttached || !_azimuth.IsFunctional || !_elevation.IsFunctional;
                if (Inoperable || tgt == null)
                {
                    Status = Inoperable ? AimState.Offline : Idle(a, e, true);
                    return;
                }

                var azm = _azimuth.WorldMatrix;
                var aim = GetAimPoint(tgt, ref azm);

                if (UseLidar || Interceptable(tgt, ref aim)) // admittedly not the best way to do this
                {     
                    aim -= _weapons.AimPos;
                    var tgtDst = aim.Length();

                    if (!UseLidar)
                    {
                        if (tgtDst > TrackRange)
                        {
                            Status = Idle(a, e);
                            return;
                        }
                        else if (_spray != -1)
                        {
                            if (_weapons.SwitchOffset)
                                _sprayOfs = _p.RandomOffset() * _spray * tgt.Radius * 0.5;

                            aim += _sprayOfs;
                        }
                    }

                    Status = AimAtTarget(ref azm, ref aim, a, e);
                    TGT = tgt.eIDTag;

                    if (Status == AimState.OnTarget)
                    {
                        if (UseLidar)
                        {
                            var r = ScanResult.Failed;
                            for (_scanCtr = 0; _scanCtr++ < _scanMx && r == ScanResult.Failed;)
                                r = _lidar.Scan(_p, tgt, _ofsLidar);
                        }

                        if (tgtDst < Range)
                        {
                            if (!tgt.Engaged) _p.Targets.MarkEngaged(TEID);

                            _weapons.Fire(_p.F);
                        }
                    }
                    else _weapons.Hold();
                }
                else Status = Idle(a, e);
            }
            else Status = Idle(a, e, true);
        }
    }
}