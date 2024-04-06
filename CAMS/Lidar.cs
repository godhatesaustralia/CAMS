﻿using Sandbox.ModAPI.Ingame;
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
        const float scat = 0.25f;
        public LidarArray(List<IMyCameraBlock> c = null, string t = "")
        {
            Cameras = c ?? new List<IMyCameraBlock>();
            tag = t;
            foreach (var c2 in Cameras)
                c2.EnableRaycast = true;
        }

        public Vector3D ArrayDir => Camera.WorldMatrix.Forward.Normalized();
        public void TryScanUpdate(ScanComp h)
        {
            int scans = 0;
            if (scans == Cameras.Count) return;
            if (h.Targets.Count == 0)
                return;
            else
            {
                foreach (var t in h.Targets.Values)
                {
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
                        scans++;     
                        h.Debug += $"\n{scans}. {Cameras[i].CustomName}";
                        h.Manager.Debug.DrawLine(Cameras[i].WorldMatrix.Translation, t.Position, Lib.debug, 0.03f);
                        h.AddOrUpdateTGT(Cameras[i].Raycast(t.Position));
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

        public void Setup(ref CombatManager m)
        {
            var p = GetParts(ref m);
            if (Elevation != null)
            {
                var list = new List<IMyCameraBlock>();
                foreach (var tag in tags)
                {
                    list.Clear();
                    m.Terminal.GetBlocksOfType(list, (cam) =>
                    {
                        bool b = cam.CubeGrid.EntityId == Elevation.TopGrid.EntityId;
                        if (b && cam.CustomName.ToUpper().Contains("MAIN"))
                        {
                            MainCamera = cam;
                            MainCamera.EnableRaycast = true;
                            mainName = cam.CustomName;
                        }
                        return b && cam.CustomName.Contains(tag);
                    });
                    Lidars.Add(tag, new LidarArray(list, tag));
                }
            }
        }

        public void Designate()
        {
            if (!ActiveCTC || !MainCamera.CanScan(Scanner.maxRaycast)) 
                return;
            var v = MainCamera.WorldMatrix.Translation;
            Scanner.Manager.Debug.DrawLine(v, v + MainCamera.WorldMatrix.Forward * Scanner.maxRaycast, Lib.debug);
            var info = MainCamera.Raycast(Scanner.maxRaycast);
            if (info.IsEmpty()) return;
            Scanner.AddOrUpdateTGT(info);
        }

        public void Update()
        {

            if (ActiveCTC) return;
            Azimuth.TargetVelocityRPM = 30;
            Elevation.TargetVelocityRPM = 60;
            if (Scanner.Targets.Count > 0)
            foreach (var t in Scanner.Targets.Values)
            {
                foreach (var ldr in Lidars.Values)
                {
                    var mat = ldr.Camera.WorldMatrix;
                    var vect2TGT = mat.Translation - t.Position;
                    bool b = mat.Forward.Dot(vect2TGT) > 0.707;
                    if (b) // max limit = 45 deg
                        ldr.TryScanUpdate(Scanner);
                    //Scanner.Manager.Debug.PrintHUD($"{Name}, {ldr.tag}, {b}", seconds: 0.01f);
                }
            }
        }
    }
}