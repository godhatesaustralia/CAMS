using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Reflection;
using VRage;
using VRage.Game;
using VRageMath;

namespace IngameScript
{
    // all lidar here designed for top-mounted cameras - some special constraints
    public class LidarArray 
    {
        public IMyCameraBlock Camera => _cameras[0];
        public IMyCameraBlock[] AllCameras => _cameras;
        public LidarArray _prev;
        SortedSet<IMyCameraBlock> _camerasByRange = new SortedSet<IMyCameraBlock>(new RangeComparer());
        IMyCameraBlock[] _cameras;
        public readonly string tag;
        const float SCAT = 0.2f;
        public int Scans = 0;
        bool _isMast;
        public IMyCameraBlock _pRef;
        public int ct => _camerasByRange.Count;
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

        public ScanResult ScanUpdate(ScanComp h, Target t, bool offset = false)
        {
            var r = ScanResult.Failed;
            int i = Scans = 0;
            if (h.ScannedIDs.Contains(t.EID))
                return r;
            _camerasByRange.Clear();
            for (; i < _cameras.Length; i++)
                _camerasByRange.Add(_cameras[i]);

            foreach (var c in _camerasByRange)
            {
                if (!t.IsExpired(h.Time + Lib.tick) && t.Distance < h.BVR)
                    offset = true;
                if (c.Closed)
                {
                    _camerasByRange.Remove(c);
                    continue;
                }
                if (!c.IsWorking)
                    continue;
                if (!c.CanScan(t.Distance))
                    continue;
                var pos = t.AdjustedPosition(h.Manager.Runtime);        
                pos += offset ? Lib.RandomOffset(ref h.Manager.RNG, SCAT * t.Radius) : Vector3D.Zero;
                if (!c.CanScan(pos))
                    continue;
                if (_isMast)
                {
                    var dir = c.WorldMatrix.Translation - pos;
                    dir.Normalize();
                    // to do - this not working

                }
                if (h.PassTarget(c.Raycast(pos), out r))
                {
                    Scans++;
                    foreach (var cprev in _prev.AllCameras)
                        h.Manager.Debug.DrawAABB(cprev.WorldAABB, Lib.Green);
                    return r;
                }
            }
            return r;
        }
    }
    // _tags = {"[A]", "[B]", "[C]", "[D]"}
    public class LidarMast
    {
        IMyMotorStator _azimuth, _elevation;
        IMyTurretControlBlock _ctc;
        public IMyCameraBlock MainCamera;
        public string Name;
        public List<LidarArray> Lidars = new List<LidarArray>();
        readonly string[] _tags;
        string _mainName;
        ScanComp _scan;
        bool _activeCTC => _ctc?.IsUnderControl ?? false;
        bool _override = false;
        public int[] Scans;

        public void DumpAllCameras(ref List<IMyCameraBlock> l)
        {
            foreach (var lidar in Lidars)
                foreach (var cam in lidar.AllCameras)
                    l.Add(cam);
        }

        public LidarMast(ScanComp s, IMyMotorStator azi, string[] t = null)
        {
            _azimuth = azi;
            _scan = s;
            _tags = t;
        }

        public void Setup(ref CombatManager m)
        {
            bool hasCTC = false;
            using (var p = new iniWrap())
            {
                if (p.CustomData(_azimuth))
                {
                    Name = p.String(Lib.hdr, "Name");
                    hasCTC = p.Bool(Lib.hdr, "CTC");
                }
            }

            long azTop = _azimuth.TopGrid.EntityId;
            m.Terminal.GetBlocksOfType<IMyMotorStator>(null, b =>
            {
                if (b.CubeGrid.EntityId == azTop)
                    _elevation = b;
                return false;
            });
            long? elTop = _elevation?.TopGrid.EntityId;
            if (hasCTC)
                m.Terminal.GetBlocksOfType<IMyTurretControlBlock>(null, b =>
                {
                    if (b.CubeGrid.EntityId == azTop || b.CubeGrid.EntityId == elTop)
                        _ctc = b;
                    return false;
                });
            if (_elevation != null)
            {
                int i = 0;
                if (_tags != null)
                {
                    for (; i < _tags.Length; i++)
                    {
                        var list = new List<IMyCameraBlock>();
                        m.Terminal.GetBlocksOfType(list, (cam) =>
                    {
                        bool b = cam.CubeGrid.EntityId == elTop;
                        if (b && cam.CustomName.ToUpper().Contains("MAIN"))
                        {
                            MainCamera = cam;
                            MainCamera.EnableRaycast = true;
                            _mainName = cam.CustomName;
                        }
                        return b && cam.CustomName.Contains(_tags[i]);
                    });
                        Lidars.Add(new LidarArray(list, _tags[i], true));
                    }
                    for (i = 0; i < _tags.Length; i++)
                    {
                        Lidars[i]._pRef = i != 0 ? Lidars[i - 1].Camera : Lidars[3].Camera;
                        Lidars[i]._prev = i != 0 ? Lidars[i - 1] : Lidars[3]; // temp
                    }
                }
                Scans = new int[Lidars.Count];
            }
        }

        public void Designate()
        {
            if (!_activeCTC || !MainCamera.CanScan(_scan.maxRaycast))
                return;
            if (MainCamera.IsActive)
            {
                _scan.PassTarget(MainCamera.Raycast(_scan.maxRaycast), true);
            }
        }

        public void Update()
        {
            int i = 0;
            if (_activeCTC) return;
            _azimuth.TargetVelocityRPM = 30;
            _elevation.TargetVelocityRPM = 60;

            foreach (var t in _scan.Targets.AllTargets())
                if (!_scan.ScannedIDs.Contains(t.EID))
                    for (; i < Lidars.Count; i++)
                    {

                        var icpt = t.AdjustedPosition(_scan.Manager.Runtime) - MainCamera.WorldMatrix.Translation;
                        icpt.Normalize();
                        if (icpt.Dot(_azimuth.WorldMatrix.Down) > 0.138)
                                continue;
                        if (Lidars[i].ScanUpdate(_scan, t) != ScanResult.Failed)                   
                            Scans[i] = Lidars[i].Scans;
                        Scans[i] = 0;
                    }
        }
    }

}