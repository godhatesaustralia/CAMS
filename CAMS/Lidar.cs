using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Reflection;
using VRage;
using VRage.Game;
using VRageMath;

namespace IngameScript
{

    public class LidarArray // a group of c all with the same orientation
    {
        public IMyCameraBlock Camera => Cameras[0];
        private List<IMyCameraBlock> Cameras;
        public readonly string tag;
        const float scat = 0.25f;
        public LidarArray(List<IMyCameraBlock> c = null, string t = "")
        {
            Cameras = c ?? new List<IMyCameraBlock>();
            tag = t;
            foreach (var c2 in Cameras)
                c2.EnableRaycast = true;
        }

        public Vector3D ArrayDir => Camera.WorldMatrix.Forward.Normalized();
        public void TryScanUpdate(ref ScanComp h)
        {
            int scans = 0;
            if (scans == Cameras.Count) return;
            Target nT;
            if (h.Targets.Count == 0)
                return;
            else
            {
                foreach (var t in h.Targets.Values)
                {
                    nT = null;
                    if (h.Manager.Runtime - t.Timestamp < Lib.maxTimeTGT) 
                        continue;
                    for (int i = 0; i < Cameras.Count; i++)
                    {
                        if (!Cameras[i].IsWorking)
                            continue;
                        if (!Cameras[i].CanScan(t.Distance)) 
                            continue;
                        if (!Camera.CanScan(t.Position)) 
                            continue;
                        nT = new Target(Cameras[i].Raycast(t.Position), h.Time, h.ID);
                        scans++;
                        h.AddOrUpdateTGT(ref nT);
                    }

                }
            }
        }
    }
    // tags = {"[A]", "[B]", "[C]", "[D]"}
    public class LidarTurret : TurretParts
    {
        public IMyCameraBlock MainCamera;
        public Dictionary<string, LidarArray> Lidars = new Dictionary<string, LidarArray>();
        private readonly string[] tags;
        private string mainName;
        ScanComp Scanner;
        int nCt, tCt;

        public LidarTurret(ScanComp s, IMyMotorStator azi, string[] t = null)
            : base(azi) 
        {
            Scanner = s;
            tags = t;
        }
        private LidarArray GetTaggedCameras(ref CombatManager m, string t)
        {
            var list = new List<IMyCameraBlock>();
            m.Terminal.GetBlocksOfType(list, (cam) =>
            {
                if (cam.CustomName.ToUpper().Contains("MAIN"))
                {
                    MainCamera = cam;
                    MainCamera.EnableRaycast = true;
                    mainName = cam.CustomName;
                }
                return cam.CubeGrid.EntityId == Elevation.TopGrid.EntityId && cam.CustomName.Contains(t); 
            });
            return new LidarArray(list, t);
        }
        public void Setup(ref CombatManager m)
        {
            var p = GetParts(ref m);
            if (Elevation != null)
            {
                var a = new List<IMyCameraBlock>();
                foreach (var tag in tags)
                    Lidars.Add(tag, GetTaggedCameras(ref m, tag));
            }
        }

        public void Designate()
        {
            if (!ActiveCTC || !MainCamera.CanScan(Scanner.maxDistance)) 
                return;
            var info = MainCamera.Raycast(Scanner.maxDistance);
            if (info.IsEmpty()) return;
            var t = new Target(info, Scanner.Time, Scanner.ID);
            Scanner.AddOrUpdateTGT(ref t);
        }

        public void TryScanUpdate()
        {
            if (Scanner.Targets.Count == 0) return;
            foreach (var t in Scanner.Targets.Values)
            {
                foreach (var ldr in Lidars.Values)
                {
                    var mat = ldr.Camera.WorldMatrix;
                    var vect2TGT = mat.Translation - t.Position;
                    if (mat.Forward.Dot(vect2TGT) > 0.8) // todo - what is max pitch/yaw and how do i figure out. need basis for this number
                        ldr.TryScanUpdate(ref Scanner);
                }
            }
        }

        public void Update()
        {
            if (ActiveCTC)
                return;
            
        }

    }
}