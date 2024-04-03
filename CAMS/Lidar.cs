using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage;
using VRage.Game;
using VRageMath;

namespace IngameScript
{

    public class LidarArray
    {
        IMyCameraBlock[] Cameras;
        float scat = 0.25f;
        public Vector3D ArrayCenter
        {
            get
            {
                Vector3D r = Vector3D.Zero;
                if (Cameras.Length == 0) return r;
                for (int i = 0; i < Cameras.Length; i++)
                    r += Cameras[i].WorldMatrix.Translation;
                r /= Cameras.Length;
                return r;
            }
        }

        public Vector3D Aim => Vector3D.Normalize(Cameras[0].WorldMatrix.Forward);
        public Vector3D Right => Vector3D.Normalize(Cameras[0].WorldMatrix.Right);
        public Vector3D Up => Vector3D.Normalize(Cameras[0].WorldMatrix.Up);

        public LidarArray(List<IMyCameraBlock> a)
        {
            Cameras = a.ToArray();
        }

        public MyDetectedEntityInfo? TryScanTarget(Vector3D targetPosition, Target t, ref CombatManager m) // from sahraki
        {
            var offset = t.Radius * scat;

            int fails = 0;
            for (int i = 0; i < Cameras.Length; ++i)
            {
                if (!Cameras[i].IsWorking)
                    continue;

                var castPos = targetPosition;

                if (fails > 0)
                    castPos += new Vector3D(m.Random.NextDouble() - 0.5, m.Random.NextDouble() - 0.5, m.Random.NextDouble() - 0.5) * offset;

                if (!Cameras[i].CanScan(castPos))
                    continue;

                var info = Cameras[i].Raycast(castPos);
                if (!info.IsEmpty())
                {
                    if (info.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies ||
                    info.Relationship == MyRelationsBetweenPlayerAndBlock.NoOwnership ||
                    info.Relationship == MyRelationsBetweenPlayerAndBlock.Neutral)
                    {
                        if (t == null || info.EntityId == t.EID)
                            return info;
                    }
                    else
                        fails++;
                }
                else
                {
                    fails++;
                    if (t == null)
                        break;
                }
                if (fails > 5)
                    break;
            }
            return null;
        }
    }

    public class DynamicLidar : TurretDriver
    {
        public IMyCameraBlock MainCamera;
        public LidarArray Array;
        public DynamicLidar(IMyMotorStator azi, TurretComp c)
            : base(azi, c) 
        {

        }

        public override iniWrap Setup(ref CombatManager m)
        {
            base.Setup(ref m);
            if (Elevation != null)
            {
                var tid = Elevation.TopGrid.EntityId;
                var cml = new List<IMyCameraBlock>();
                m.Terminal.GetBlocksOfType(cml, (b)=> b.CubeGrid.EntityId == Elevation.TopGrid.EntityId);
                Array = new LidarArray(cml);
                for(int i = 0; i < cml.Count; i++)
                    if (cml[i].CustomName.ToUpper().Contains("MAIN"))
                    {
                        MainCamera = cml[i];
                        break;
                    }
            }
            return null;
        }

        public override void SelectTarget(ref Dictionary<long, Target> targets)
        {
            // Auto reset
            aziTgt = aziRest;
            elTgt = elRest;

        }
    }
}