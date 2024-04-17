using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Reflection;
using VRage;
using VRage.Game;
using VRageMath;

namespace IngameScript
{

    public class LidarArray // list group of c all with the same orientation
    {
        public IMyCameraBlock Camera => Cameras[0];
        private List<IMyCameraBlock> Cameras;
        public readonly string tag;
        const float scat = 0.2f;
        public int scans, index;
        public double avgDist;
        public int ct => Cameras.Count;
        public LidarArray(List<IMyCameraBlock> c = null, string t = "", int i = -1)
        {
            Cameras = c ?? new List<IMyCameraBlock>();
            tag = t;
            index = i;
            foreach (var c2 in Cameras)
                c2.EnableRaycast = true;
        }

        public Vector3D ArrayDir => Camera.WorldMatrix.Forward.Normalized();
        public void TryScanUpdate(ScanComp h, bool scatter = false)
        {
            scans = 0;
            foreach (var t in h.Targets.Values)
            {
                if (scans > h.Targets.Count) return;
                avgDist = 0;
                if (h.Manager.Runtime - t.Timestamp < Lib.maxTimeTGT)
                    continue;
                for (int i = 0; i < Cameras.Count; i++)
                {
                    avgDist += Cameras[i].AvailableScanRange;
                    if (!Cameras[i].IsWorking)
                        continue;
                    if (!Cameras[i].CanScan(t.Distance))
                        continue;
                    var pos = t.PositionUpdate(h.Manager.Runtime);
                    pos += scatter ? Lib.RandomOffset(ref h.Manager.Random, scat * t.Radius) : Vector3D.Zero;
                    if (!Camera.CanScan(pos))
                        continue;
                    scans++;
                    h.Debug += $"\n{scans}. {Cameras[i].CustomName}";
                    //h.Manager.Debug.DrawLine(Cameras[i].WorldMatrix.Translation, t.Position, Lib.Green, 0.03f);
                    h.AddOrUpdateTGT(Cameras[i].Raycast(pos));
                }
                avgDist /= Cameras.Count;
            }
        }
    }
    // tags = {"[A]", "[B]", "[C]", "[D]"}
    public class LidarTurret : TurretParts
    {
        public IMyCameraBlock MainCamera;
        public List<LidarArray> Lidars = new List<LidarArray>();
        private readonly string[] tags;
        private string mainName;
        ScanComp Scanner;

        // metrics
        public int[] scans;
        public double[] avgDists;

        public LidarTurret(ScanComp s, IMyMotorStator azi, string[] t = null)
            : base(azi)
        {
            Scanner = s;
            tags = t;
        }

        public void Setup(ref CombatManager m)
        {
            var p = GetParts(ref m);
            if (Elevation != null)
            {
                int i = 0;
                var list = new List<IMyCameraBlock>();
                for (; i < tags.Length; i++)
                {
                    list.Clear();
                    m.Terminal.GetBlocksOfType(list, (cam) =>
                    {
                        bool b = cam.CubeGrid.EntityId == Elevation?.TopGrid.EntityId;
                        if (b && cam.CustomName.ToUpper().Contains("MAIN"))
                        {
                            MainCamera = cam;
                            MainCamera.EnableRaycast = true;
                            mainName = cam.CustomName;
                        }
                        return b && cam.CustomName.Contains(tags[i]);
                    });
                    Lidars.Add(new LidarArray(list, tags[i], i));
                }
                scans = new int[Lidars.Count];
                avgDists = new double[Lidars.Count];
            }
        }

        public void Designate()
        {
            if (!ActiveCTC || !MainCamera.CanScan(Scanner.maxRaycast))
                return;
            Scanner.AddOrUpdateTGT(MainCamera.Raycast(Scanner.maxRaycast));
        }

        public void Update()
        {
            if (ActiveCTC || Scanner.Targets.Count == 0) return;
            Azimuth.TargetVelocityRPM = 30;
            Elevation.TargetVelocityRPM = 60;
            foreach (var t in Scanner.Targets.Values)
            {
                for (int i = 0; i < Lidars.Count; i++)
                {
                    //var mat = Lidars[i].Camera.WorldMatrix;
                    //var vect2TGT = mat.Translation - t.Position;
                    //bool b = mat.Forward.Dot(vect2TGT) > 0.707;
                    //if (b) // max limit = 45 deg
                        Lidars[i].TryScanUpdate(Scanner);
                    scans[i] = Lidars[i].scans;
                    avgDists[i] = Lidars[i].avgDist;

                    //Scanner.Manager.Debug.PrintHUD($"{Name}, {ldr.tag}, {b}", seconds: 0.01f);
                }
            }
            for (int i = 0; i < Lidars.Count; i++)
                avgDists[i] = Scanner.maxRaycast;
        }
    }
}