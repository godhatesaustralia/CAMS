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
        bool _isMast, _useRand = false;
        public int _ct => _cameras.Length;
        const int BVR = 1900;
        public LidarArray(List<IMyCameraBlock> c, string t = "", bool m = false)
        {
            if (c != null)
            {
                _cameras = new IMyCameraBlock[c.Count];
                for (int j = 0; j < c.Count; j++)
                    _cameras[j] = c[j];
            }
            tag = t;
            _isMast = m;
            foreach (var c2 in _cameras)
                c2.EnableRaycast = true;
        }
        class RangeComparer : IComparer<IMyCameraBlock>
        {
            public int Compare(IMyCameraBlock x, IMyCameraBlock y)
            {
                if (x.Closed) return (y.Closed ? 0 : 1);
                else if (y.Closed) return -1;
                else return x.AvailableScanRange > y.AvailableScanRange ? -1 : (x.AvailableScanRange < y.AvailableScanRange ? 1 : (x.EntityId > y.EntityId ? -1 : (x.EntityId < y.EntityId ? 1 : 0)));
            }
        }

        Vector3D RaycastLead(ref Target t, Vector3D srcPos, Program p, double ofs = 5) // ofs is spread factor. whip left as 5 default
        {
            var dT = t.Elapsed(p.F);
            var tPos = t.AdjustedPosition(p.F);
            //tPos += t.Velocity * dT;
            var tDir = (tPos - srcPos).Normalized();
            if (_lastScan == ScanResult.Failed)
            {
                _useRand = !_useRand;
                if (_useRand)
                {
                    var p1 = Vector3D.CalculatePerpendicularVector(tDir);
                    var p2 = p1.Cross(tDir);
                    p2.Normalize();
                    return (p.GaussRNG() * p1 + p.GaussRNG() * p2) * dT * ofs;
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
                scanAVG += _cameras[i].AvailableScanRange;
                _camerasByRange.Add(_cameras[i]);
            }
            scanAVG /= _ct;

            foreach (var c in _camerasByRange)
            {
                if (!c.IsWorking)
                    continue;
                if (!c.CanScan(t.Distance))
                    continue;
                var pos = RaycastLead(ref t, c.WorldMatrix.Translation, p);
                pos += spread ? Lib.RandomOffset(ref p.RNG, SCAT * t.Radius) : Vector3D.Zero;
                if (!c.CanScan(pos) || pos == Vector3D.Zero)
                    continue;
                if (_isMast)
                {
                    var dir = pos - c.WorldMatrix.Translation;
                    dir.Normalize();
                    if (c.WorldMatrix.Down.Dot(dir) > 0.58)
                        continue;
                }
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
    // _tags = {"[A]", "[B]", "[C]", "[D]"}
    public class LidarMast
    {
        IMyMotorStator _azimuth, _elevation;
        IMyTurretControlBlock _ctc;
        Program _p;
        public IMyCameraBlock Main;
        public string Name;
        public List<LidarArray> Lidars = new List<LidarArray>();
        readonly string[] _tags;
        bool _activeCTC => _ctc?.IsUnderControl ?? false;
        public bool Manual => _stopSpin || _activeCTC;
        double _maxAzD, _maxCamD;
        float _azR, _elR, _max = float.MaxValue, _min = float.MinValue; // rest angles - it's my code, i can name stuff as terribly as i want!!!!
        bool _stopSpin = false;
        public int[] Scans;

        public LidarMast(Program p, IMyMotorStator azi, string[] t = null)
        {
            _azimuth = azi;
            _p = p;
            _tags = t;
        }

        public void Setup(ref Program m)
        {
            bool hasCTC = false;
            using (var p = new iniWrap())
            {
                if (p.CustomData(_azimuth))
                {
                    var rad = (float)(Math.PI / 180);
                    Name = p.String(Lib.HDR, "name", "ARY");
                    hasCTC = p.Bool(Lib.HDR, "ctc");

                    _maxAzD = p.Double(Lib.HDR, "limRayAzDown", 0.134);
                    _maxCamD = p.Double(Lib.HDR, "limRayCamDown", 0.64);
                    _azR = rad * p.Float(Lib.HDR, "azR", 360);
                    _elR = rad * p.Float(Lib.HDR, "elR", 360);
                }
            }
            _azimuth.UpperLimitRad = _max;
            _azimuth.LowerLimitRad = _min;
            var azTop = _azimuth.TopGrid;
            m.Terminal.GetBlocksOfType<IMyMotorStator>(null, b =>
            {
                if (b.CustomName.Contains(Name))
                    _elevation = b;
                if (b.CubeGrid == azTop)
                    _elevation = b;
                return false;
            });
            var elTop = _elevation?.TopGrid;
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
            if (_elevation != null)
            {           
                if (_tags != null)
                {
                    foreach (var tg in _tags)
                    {
                        var list = new List<IMyCameraBlock>();
                        m.Terminal.GetBlocksOfType(list, (cam) =>
                        {
                            bool b = cam.CubeGrid == elTop;
                            if (b && cam.CustomName.ToUpper().Contains("MAIN"))
                            {
                                Main = cam;
                                Main.EnableRaycast = true;
                                //_mainName = cam.CustomName;
                            }
                            return b && cam.CustomName.Contains(tg);
                        });
                        Lidars.Add(new LidarArray(list, tg, true));
                    }
                }
                _elevation.LowerLimitRad = _min;
                _elevation.UpperLimitRad = _max;
                Scans = new int[Lidars.Count];
            }
        }

        public void Retvrn()
        {
            _azimuth.UpperLimitRad = _azR;
            _elevation.UpperLimitRad = _elR;
            _stopSpin = true;
        }

        public void Designate()
        {
            if (!_activeCTC || !Main.CanScan(_p.maxRaycast))
                return;
            if (Main.IsActive)
            {
                _p.PassTarget(Main.Raycast(_p.maxRaycast), true);
            }
        }

        public void Update()
        {
            if (_activeCTC)
            {
                if (_stopSpin)
                {
                    _azimuth.LowerLimitRad = _elevation.LowerLimitRad = _min;
                    _azimuth.UpperLimitRad = _elevation.UpperLimitRad = _max;
                    _stopSpin = false;
                }
                return;
            }
            else if (!_stopSpin)
            {
                _azimuth.TargetVelocityRPM = 29;
                _elevation.TargetVelocityRPM = 53;
            }
            else
            {
                bool 
                    ar = Math.Abs(_azimuth.Angle - _azR) < 0.05, 
                    er = Math.Abs(_elevation.Angle - _elR) < 0.05;
                if (ar)
                    _azimuth.TargetVelocityRPM = 0;
                if (er)
                    _elevation.TargetVelocityRPM = 0;
            }

            foreach (var t in _p.Targets.AllTargets())
                if (!_p.Targets.ScannedIDs.Contains(t.EID))
                    for (int i = 0; i < Lidars.Count; i++)
                    {
                        var icpt = t.Position + t.Elapsed(_p.F) * t.Velocity - Main.WorldMatrix.Translation;
                        icpt.Normalize();
                        if (icpt.Dot(_azimuth.WorldMatrix.Down) > _maxAzD)
                            continue;
                        if (icpt.Dot(Lidars[i].First.WorldMatrix.Backward) > 0 || icpt.Dot(Lidars[i].First.WorldMatrix.Down) > _maxCamD)
                            continue;
                        if (Lidars[i].Scan(_p, t) != ScanResult.Failed)
                            Scans[i] = Lidars[i].Scans;
                        Scans[i] = 0;
                    }
        }
    }

}