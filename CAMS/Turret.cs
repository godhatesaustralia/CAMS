using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    // fuck it
    public partial class Program : MyGridProgram
    {
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
            protected double _range, _speed;
            public IMyTurretControlBlock _ctc;
            protected PID _aPID, _ePID;
            protected TurretWeapons _weapons;
            protected Program _m;
            public long tEID = -1, lastUpdate = 0;

            #endregion

            protected TurretBase(IMyMotorStator a, Program m)
            {
                _m = m;
                if (_azimuth.Top == null)
                    return;
                _azimuth = a;
                long g1 = _azimuth.TopGrid.EntityId, g2 = -1;
                m.Terminal.GetBlocksOfType<IMyMotorStator>(null, b =>
                {
                    if (b.EntityId == g1)
                    {
                        _elevation = b;
                        g2 = b.EntityId;
                    }
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
                                var e = b.CubeGrid.EntityId;
                                if (b.CustomName.Contains(Name) || e == g1 || e == g2)
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

                        var list = new List<IMyUserControllableGun>();
                        m.Terminal.GetBlocksOfType(list, b => b.CubeGrid.EntityId == g2);
                        _weapons = new TurretWeapons(p.Int(h, "salvo", -1), list);
                    }          
            }
            // aim is an internal copy of CURRENT(!) target position
            // because in some cases (PDLR assistive targeting) we will only want to point at
            // the target directly and not lead (i.e. we will not be using this)
            protected bool Interceptable(Target tgt, ref Vector3D aim)
            {
                // thanks alysius <4
                Vector3D
                    rP = aim - _weapons.AimRef,
                    rV = tgt.Velocity - _m.Velocity;
                double
                    a = _speed * _speed - rV.LengthSquared(),
                    b = -2 * rV.Dot(rP),
                    d = (b * b) - (4 * a * -rP.LengthSquared());
                if (d < 0) return false; // bad determinant for quadratic formula
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
            protected bool AimAtTarget(ref Vector3D aim, bool inRange = true)
            {
                if (tEID == -1)
                    return false; // maybe(?)
                double tgtAzi, tgtEl, tgtAzi2, checkEl, azi, el, aRPM, eRPM, aRate, eRate, errTime;
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
                errTime = (_m.F - lastUpdate) * Lib.tickSec;
                GetHeadingError(_azimuth, ref _lastAzi, out aRate);
                GetHeadingError(_elevation, ref _lastEl, out eRate);



                lastUpdate = _m.F;
                return true;
            }


            // combination of whip's functions, i quite honestly don't know anything about this
            void GetHeadingError(IMyMotorStator r, ref MatrixD p, out double a)
            {
                Vector3D 
                    fwd = r.WorldMatrix.Forward,
                    up = p.Up, pFwd = p.Forward,
                    flatFwd = Lib.Rejection(pFwd, up);
                a = Lib.AngleBetween(flatFwd, fwd) * Math.Sign(flatFwd.Dot(p.Left));
                p = r.WorldMatrix;
            }

            void CalcPitchAngle(Vector3D norm, out double p)
            {
                var dir = Vector3D.TransformNormal(norm, MatrixD.Transpose(aziMat));
                p = Math.Sign(dir.Y);
                var flat = dir;
                flat.Y = 0;
                p *= Vector3D.IsZero(flat) ? Lib.halfPi : Lib.AngleBetween(dir, flat);
            }

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
}