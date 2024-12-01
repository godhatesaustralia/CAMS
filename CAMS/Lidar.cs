using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.IO;
using VRageMath;

namespace IngameScript
{
    // all lidar here designed for top-mounted cameras - some special constraints
    public class LidarArray
    {
        public IMyCameraBlock First => _cameras[0];
        public IMyCameraBlock[] AllCameras => _cameras;
        SortedSet<IMyCameraBlock> _camerasByRange = new SortedSet<IMyCameraBlock>(new RangeComparer());
        IMyCameraBlock[] _cameras;
        ScanResult _lastScan = ScanResult.Hit;
        public readonly string tag;
        public double scanAVG;
        const float SCAT = 0.2f;
        public int Scans = 0;
        bool _useRand = false;
        public int Count => _cameras.Length;
        public LidarArray(List<IMyCameraBlock> c, string t = "")
        {
            if (c != null)
            {
                _cameras = new IMyCameraBlock[c.Count];
                for (int j = 0;  j < c.Count; j++)
                    _cameras[j] = c[j];
            }
            tag = t;
            foreach (var c2 in _cameras)
                c2.EnableRaycast = true;
        }
        class RangeComparer : IComparer<IMyCameraBlock>
        {
            public int Compare(IMyCameraBlock x, IMyCameraBlock y)
            {
                if (x.Closed) return y.Closed ? 0 : 1;
                else if (y.Closed) return -1;
                else return x.AvailableScanRange > y.AvailableScanRange ? -1 : (x.AvailableScanRange < y.AvailableScanRange ? 1 : (x.EntityId > y.EntityId ? -1 : (x.EntityId < y.EntityId ? 1 : 0)));
            }
        }

        Vector3D RaycastLead(Target t, Vector3D srcPos, Program p, double ofs = 5) // ofs is spread factor. whip left as 5 default
        {
            var dT = t.Elapsed(p.F);
            var tPos = t.AdjustedPosition(p.F);
            var tDir = (tPos - srcPos).Normalized();

            if (_lastScan == ScanResult.Failed)
            {
                _useRand = !_useRand;
                if (_useRand)
                {
                    var p1 = Vector3D.CalculatePerpendicularVector(tDir);
                    var p2 = p1.Cross(tDir);
                    p2.Normalize();
                    tPos += (p.GaussRNG() * p1 + p.GaussRNG() * p2) * dT * ofs;
                }
            }
            return tPos + tDir * 2 * t.Radius;
        }

        public ScanResult Scan(Program p, Target t, bool spread = false)
        {
            var r = _lastScan = ScanResult.Failed;
            int i = Scans = 0;

            if (p.Targets.ScannedIDs.Contains(t.EID))
                return r;

            _camerasByRange.Clear();
            scanAVG = 0;
            for (; i < _cameras.Length; i++)
            {
                var c = _cameras[i];
                if (c.Closed || !c.IsFunctional) continue;
            
                scanAVG += c.AvailableScanRange;
                _camerasByRange.Add(c);
            }
            scanAVG /= Count;

            foreach (var c in _camerasByRange)
            {
                if (!c.IsWorking || !c.CanScan(t.Distance))
                    continue;

                var pos = RaycastLead(t, c.WorldMatrix.Translation, p);
                pos += spread ? p.RandomOffset() * SCAT * t.Radius : Vector3D.Zero;
                if (!c.CanScan(pos) || pos == Vector3D.Zero)
                    continue;
                    
                if (p.PassTarget(c.Raycast(pos), out r))
                {
                    Scans++;
                    _lastScan = r;
                    break;
                }
                _lastScan = r;
            }
            return r;
        }
    }

    public class LidarMast
    {
        IMyMotorStator _az, _el;
        IMyTurretControlBlock _ctc;
        Program _p;
        public IMyCameraBlock Main;
        public string Name, RPM, TGT;
        public List<LidarArray> Lidars = new List<LidarArray>();
        bool _activeCTC => _ctc?.IsUnderControl ?? false;
        public bool Manual => _stopSpin || _activeCTC;
        double _maxAzD, _maxCamD;
        float _azR, _elR, _max = float.MaxValue, _min = float.MinValue; // rest angles - it's my code, i can name stuff as terribly as i want!!!!
        bool _stopSpin = false;
        int _aRPM = 20, _eRPM = 53;
        public int Scans;
    

        public LidarMast(Program p, IMyMotorStator azi)
        {
            _az = azi;
            _p = p;
            _aRPM = 29;
            _eRPM = 53;
        }

        public void Setup(Program m, ref string[] tags)
        {
            bool hasCTC = false;
            using (var p = new iniWrap())
            {
                if (p.CustomData(_az))
                {
                    var rad = (float)(Math.PI / 180);
                    Name = p.String(Lib.H, Lib.N, "ARY");
                    hasCTC = p.Bool(Lib.H, "ctc");

                    _maxAzD = p.Double(Lib.H, "limRayAzDown", 0.134);
                    _maxCamD = p.Double(Lib.H, "limRayCamDown", 0.6025);
                    _azR = rad * p.Float(Lib.H, "azR", 360);
                    _elR = rad * p.Float(Lib.H, "elR", 360);
                }
            }

            _az.UpperLimitRad = _max;
            _az.LowerLimitRad = _min;

            var azTop = _az.TopGrid;
            m.Terminal.GetBlocksOfType<IMyMotorStator>(null, b =>
            {
                if (b.CustomName.Contains(Name))
                    _el = b;

                if (b.CubeGrid == azTop)
                    _el = b;

                return false;
            });

            var elTop = _el?.TopGrid;
            if (hasCTC)
                m.Terminal.GetBlocksOfType<IMyTurretControlBlock>(null, b =>
                {
                    if (b.CustomName.Contains(Name))
                        _ctc = b;
                    if (b.CubeGrid == azTop)
                        _ctc = b;
                    else if (elTop != null && b.CubeGrid == elTop)
                        _ctc = b;
                    return false;
                });

            if (_el != null && tags != null)
            {
                foreach (var tg in tags)
                {
                    var list = new List<IMyCameraBlock>();
                    m.Terminal.GetBlocksOfType(list, (cam) =>
                    {
                        bool b = cam.CubeGrid == elTop;
                        if (b && cam.CustomName.ToUpper().Contains("MAIN"))
                            Main = cam;

                        return b && cam.CustomName.Contains(tg);
                    });
                    Lidars.Add(new LidarArray(list, tg));
                }

                _el.LowerLimitRad = _min;
                _el.UpperLimitRad = _max;
            }
        }

        public void Retvrn()
        {
            _az.UpperLimitRad = _azR;
            _el.UpperLimitRad = _elR;
            _stopSpin = true;
        }

        public void Designate()
        {
            if (_activeCTC && Main.IsActive)
                if (Main.CanScan(_p.ScanDistLimit))
                    _p.PassTarget(Main.Raycast(_p.ScanDistLimit), true);
        }

        public void Update()
        {
            if (_activeCTC)
            {
                if (_stopSpin)
                {
                    _az.LowerLimitRad = _el.LowerLimitRad = _min;
                    _az.UpperLimitRad = _el.UpperLimitRad = _max;
                    _stopSpin = false;
                }
                return;
            }
            else if (!_stopSpin)
            {
                _az.TargetVelocityRPM = _aRPM;
                _el.TargetVelocityRPM = _eRPM;
            }
            else
            {
                if (Math.Abs(_az.Angle - _azR) < 0.05)
                    _az.TargetVelocityRPM = 0;

                if (Math.Abs(_el.Angle - _elR) < 0.05)
                    _el.TargetVelocityRPM = 0;
                
            }
            RPM = $"\n{_az.TargetVelocityRPM:000}\n{_el.TargetVelocityRPM:000}";
            
            double min = _max;
            Scans = 0;
            if (_p.Targets.Count == 0)
            {
                TGT = "NO TRGTS";
                return;
            }

            foreach (var t in _p.Targets.AllTargets())
                if (!_p.Targets.ScannedIDs.Contains(t.EID))
                    for (int i = 0; i < Lidars.Count; i++)
                    {
                        var icpt = t.Position + t.Elapsed(_p.F) * t.Velocity - Main.WorldMatrix.Translation;
                        icpt.Normalize();
  

                        if (icpt.Dot(_az.WorldMatrix.Down) > _maxAzD ||
                            icpt.Dot(Lidars[i].First.WorldMatrix.Backward) > 0 ||
                            icpt.Dot(Lidars[i].First.WorldMatrix.Down) > _maxCamD)
                            continue;

                        if (Lidars[i].Scan(_p, t) != ScanResult.Failed)
                        {
                            TGT = $"TGT {t.eIDTag}";
                            Scans+= Lidars[i].Scans;
                        }

                        if (Lidars[i].scanAVG < min)
                            min = Lidars[i].scanAVG;             
                    }
        }
    }

}