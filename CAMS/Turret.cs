using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using VRageMath;

namespace IngameScript
{
    // fuck it
    public partial class Program : MyGridProgram
    {
        public abstract class TurretBase
        {
            protected const float rad = (float)Math.PI / 180;
            public string Name; // yeah
            protected IMyMotorStator _azimuth, _elevation;
            public MatrixD aziMat => _azimuth.WorldMatrix;
            protected float _aMx, _aMn, _aRest, _aRPM, _eMx, _eMn, _eRest, _eRPM; // absolute max and min azi/el for basic check
            protected double _range, _speed;
            public IMyTurretControlBlock _ctc;
            protected PID _aPID, _ePID;
            protected TurretWeapons _weapons;
            protected Program _m;
            public long tEID = -1;
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

            protected bool Interceptable(Target tgt, ref Vector3D aim)
            {
                // alysisus wrote this bc im fuckgin too stupid
                Vector3D
                    rP = aim - _weapons.AimRef,
                    rV = tgt.Velocity - _m.Velocity;
                double
                    a = _speed * _speed - rV.LengthSquared(),
                    b = -2 * rV.Dot(rP),
                    d = (b * b) - (4 * a * -rP.LengthSquared());
                if (d < 0) return false;
                d = Math.Sqrt(d);
                double 
                    t1 = (-b + d) / (2 * a),
                    t2 = (-b - d) / (2 * a),
                    t = t1 > 0 ? (t2 > 0 ? (t1 < t2 ? t1 : t2) : t1) : t2;
               if (double.IsNaN(t)) return false;
               aim = tgt.Accel.Length() < 0.1 ? aim + tgt.Velocity * t : aim + tgt.Velocity * t + 0.5 * tgt.Accel * t * t;
               return true;
            }


        }
    }
}