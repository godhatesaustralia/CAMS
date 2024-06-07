﻿using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    // fuck it
    public abstract class TurretBase
    {
        #region you aint built for these fields son

        protected const float rad = (float)Math.PI / 180;
        public string Name; // yeah
        protected IMyMotorStator _azimuth, _elevation;
        public MatrixD aziMat => _azimuth.WorldMatrix;
        MatrixD _lastAzi, _lastEl;
        protected float
            _aMx,
            _aMn,
            _aRest,
            _aRPM,
            _eMx,
            _eMn,
            _eRest,
            _eRPM; // absolute max and min azi/el for basic check
        protected double _range, _speed, _tol; // last is aim tolerance
        public IMyTurretControlBlock _ctc;
        protected PID _aPID, _ePID;
        protected Weapons _weapons;
        protected Program _m;
        public long tEID = -1, lastUpdate = 0;

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
                if (p.CustomData(_azimuth))
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

                    _aMx = rad * p.Float(h, "azMax", 361);
                    _aMn = rad * p.Float(h, "azMin", -361);
                    _aRest = rad * p.Float(h, "azRst", 0);
                    _aRPM = p.Float(h, "azRPM", 20);
                    _eMx = rad * p.Float(h, "elMax", 90);
                    _eMn = rad * p.Float(h, "elMin", -90);
                    _eRest = rad * p.Float(h, "elRst", 0);
                    _eRPM = p.Float(h, "elRPM", 20);
                    _range = p.Double(h, "range", 800);
                    _speed = p.Double(h, "speed", 400);
                    _tol = p.Double(h, "tolerance", 1E-5);
                    _aPID = new PID(75, 0, 0, 0.25, 5);
                    _ePID = new PID(75, 0, 0, 0.25, 5);
                    var list = new List<IMyUserControllableGun>();
                    m.Terminal.GetBlocksOfType(list, b => b.CubeGrid == _elevation?.CubeGrid);
                    _weapons = new Weapons(p.Int(h, "salvo", -1), list);
                }
                else throw new Exception($"\nFailed to create turret using azimuth rotor {_azimuth.CustomName}.");
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

            aim = tgt.Accel.Length() < 0.1 ? aim + tgt.Velocity * t : aim + tgt.Velocity * t + 0.5 * tgt.Accel * t * t;
            return true;
        }

        // credit to https://forum.keenswh.com/threads/tutorial-how-to-do-vector-transformations-with-world-matricies.7399827/
        // and! his turret ai slaving script.
        // return value indicates whether turret is on target.
        protected bool AimAtTarget(ref Vector3D aim)
        {

            if (tEID == -1)
                return false; // maybe(?)
            double tgtAzi, tgtEl, tgtAzi2, checkEl, azi, el, aRPM, eRPM, aRate, eRate;//, errTime;
            aim.Normalize();

            var localToTGT = Vector3D.TransformNormal(aim, MatrixD.Transpose(aziMat));
            int dir = Math.Sign(localToTGT.Y);
            localToTGT.Y = 0; // flat

            tgtAzi = Lib.AngleBetween(Vector3D.Forward, localToTGT);
            if (localToTGT.Z > 0 && tgtAzi < 1E-5)
                tgtAzi = Lib.Pi;
            tgtAzi *= Math.Sign(localToTGT.X);

            CalcPitchAngle(aim, out tgtEl);
            CalcPitchAngle(aziMat.Forward, out checkEl);

            // angle domain constraint 
            el = (tgtEl - checkEl) * dir;
            tgtAzi2 = _azimuth.Angle + tgtAzi;

            azi = (tgtAzi2 < _aMn && tgtAzi2 + Lib.Pi2 < _aMx) || (tgtAzi2 > _aMx && tgtAzi2 - Lib.Pi2 < _aMn)
                ? -Math.Sign(tgtAzi) * (Lib.Pi2 - Math.Abs(tgtAzi))
                : tgtAzi;
            aRPM = _aPID.Control(azi);
            eRPM = _ePID.Control(el);
            CalcHdgError(_azimuth, ref _lastAzi, out aRate);
            CalcHdgError(_elevation, ref _lastEl, out eRate);

            _azimuth.TargetVelocityRPM = (float)(aRPM + aRate);
            _elevation.TargetVelocityRPM = (float)(eRPM + eRate);

            lastUpdate = _m.F;
            return _weapons.AimDir.Dot(aim - _weapons.AimPos) < _tol;
        }

        // combination of whip's functions, i quite honestly don't know anything about this
        void CalcHdgError(IMyMotorStator r, ref MatrixD p, out double a)
        {
            Vector3D
                fwd = r.WorldMatrix.Forward,
                up = p.Up, pFwd = p.Forward,
                flatFwd = Lib.Rejection(pFwd, up);
            a = Lib.AngleBetween(ref flatFwd, ref fwd) * Math.Sign(flatFwd.Dot(p.Left)) / ((_m.F - lastUpdate) * Lib.tickSec);
            p = r.WorldMatrix;
        }

        void CalcPitchAngle(Vector3D norm, out double p)
        {
            var dir = Vector3D.TransformNormal(norm, MatrixD.Transpose(aziMat));
            p = Math.Sign(dir.Y);
            var flat = dir;
            flat.Y = 0;
            p *= Vector3D.IsZero(flat) ? Lib.halfPi : Lib.AngleBetween(ref dir, ref flat);
        }

        public abstract void Update();
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

        public override void Update()
        {

        }

    }
}