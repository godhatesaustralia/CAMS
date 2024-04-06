using Sandbox.Game.AI.Navigation;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace IngameScript
{
    public class TurretComp : CompBase
    {
        public Dictionary<string, GunTurret> Turrets = new Dictionary<string, GunTurret>();
        public Random rnd = new Random();
        public IMyGridTerminalSystem GTS => Manager.Terminal;
        public TurretComp(string n, CombatManager m) : base(n)
        {
        }
        public override void Setup(CombatManager m, ref iniWrap p)
        {
            Turrets.Clear();
            Manager = m;
            Manager.Terminal.GetBlocksOfType<IMyMotorStator>(null, (b) =>
            {
                if (b.CubeGrid.EntityId == m.Controller.CubeGrid.EntityId && b.CustomName.Contains("Azimuth") && !b.CustomName.Contains(Lib.array))
                {
                    var t = new GunTurret(b, this);
                    t.Setup(ref m);
                    Turrets.Add(t.Name, t);
                }
                return true;
            });
        }
        public override void Update(UpdateFrequency u)
        {
            if (Manager.Targets.Count == 0) return;
            foreach (var tur in Turrets.Values)
            {
                if (tur.ActiveCTC) continue;
                tur.SelectTarget(ref Manager.Targets);
                tur.AimAndTrigger();
                tur.Update();
            }
        }
    }

    public class TurretParts
    {
        public string Name;
        protected double
            aziRest = 0,
            elRest = 0;
        public const double rad = Math.PI / 180;
        protected readonly string elv = "Elevation", el = "EL";
        public IMyMotorStator Azimuth;
        public IMyMotorStator Elevation;
        protected IMyTurretControlBlock CTC;
        public bool ActiveCTC
        {
            get
            {
                if (CTC == null) return false;
                return CTC.AIEnabled || CTC.IsUnderControl;
            }
        }

        protected TurretParts(IMyMotorStator az)
        {
            Azimuth = az;
        }

        public iniWrap GetParts(ref CombatManager m)
        {
            var top = Azimuth.TopGrid;
            top.CustomName = Name + " " + elv;
            var id = top.EntityId;
            var p = new iniWrap();
            var res = new MyIniParseResult();
            if (p.CustomData(Azimuth, out res))
            {
                Name = p.String(Lib.hdr, "Name");
                aziRest = rad * p.Double(Lib.hdr, "aRest");
                elRest = rad * p.Double(Lib.hdr, "eRest");
            }
            else throw new Exception(res.Error);
            m.Terminal.GetBlocksOfType<IMyMotorStator>(null, (b) =>
            {
                if (b.CubeGrid.EntityId != id) return false;
                if (b.CustomName.Contains(elv) || b.CustomName.Contains(el))
                    Elevation = b as IMyMotorStator;
                return true;
            });
            if (Elevation != null)
            {
                var n = Elevation.TopGrid.CustomName;
                id = Elevation.TopGrid.EntityId;
                Elevation.TopGrid.CustomName = n.Contains("Grid") ?  Name + " Arm" : n;
            }
            m.Terminal.GetBlocksOfType<IMyTurretControlBlock>(null, (b) =>
            {
                if (b.CustomName.Contains(Name) || b.CubeGrid.EntityId == Elevation.CubeGrid.EntityId)
                    CTC = b;
                return true;
            });
            return p;
        }

    }

        public abstract class TurretDriver : TurretParts
        {
            protected bool aziWrap = false, hasTarget = false;

            protected CompBase Host;
        protected double
            aziMin,
            aziMax,
            elMin,
            elMax,
            //    aimOffset,
            aziTgt = 0,
            elTgt = 0;

            protected int
                range,
                projSpd,
                aziRPM,
                elRPM;
            protected float aimTolerance = 0.05f;

            public TurretDriver(IMyMotorStator azi, CompBase c)
            : base(azi)
            {
                Host = c;
            }

            public virtual iniWrap Setup(ref CombatManager m)
            {
                var p = GetParts(ref m);
                aziMax = rad * p.Double(Lib.hdr, "aMax");
                aziMin = rad * p.Double(Lib.hdr, "aMin", -aziMax);
                elMax = rad * p.Double(Lib.hdr, "eMax", 90);
                elMin = rad * p.Double(Lib.hdr, "eMin", -elMax);
                if (Elevation != null)
                {
                    // here is where i would put occlusion parse if 
                }
                return p;
            }
            public abstract void SelectTarget(ref Dictionary<long, Target> targets);

            protected bool GetTurretAngles(Vector3D aimPoint)
            {
                var atkDir = Vector3D.Normalize(aimPoint);

                // Check range
                if (aimPoint.Length() > range) return false;

                // Calculate azimuth angle
                var aziVector = atkDir - Lib.Projection(atkDir, Azimuth.WorldMatrix.Up);
                var aziAngle = Lib.AngleBetween(aziVector, Azimuth.WorldMatrix.Backward) * Math.Sign(aziVector.Dot(Azimuth.WorldMatrix.Left));


                // Check if azimuth is OK
                if (aziAngle > aziMax || aziAngle < aziMin) return false;

                // Calculate elevation angle
                var elAngle = Lib.AngleBetween(aziVector, atkDir) * Math.Sign(atkDir.Dot(Azimuth.WorldMatrix.Up));
                if (elAngle > elMax || elAngle < elMin) return false;

                // Found best target, set target az, elv, and return
                aziTgt = aziAngle;
                elTgt = elAngle;
                return true;
            }

            private void SetAzimuth(float lower, float upper, int sign)
            {
                Azimuth.LowerLimitRad = lower;
                Azimuth.UpperLimitRad = upper;
                Azimuth.TargetVelocityRPM = aziRPM * sign;
            }
            public bool AtAimPoint()
            {
                if (aziTgt == aziRest && elTgt == elRest)
                    return false;
                if (!hasTarget)
                    return false;
                double
                    aziDif = Math.Abs(Azimuth.Angle - aziTgt),
                    elDif = Math.Abs(Elevation.Angle - elTgt);
                if (aziDif > aimTolerance && elDif > aimTolerance)
                    return false;
                return true;
            }
            public virtual void AimAndTrigger()
            {
                if (ActiveCTC || Elevation == null || Azimuth == null)
                    return;

                var aziTrue = Azimuth.Angle;
                if (aziTrue > Math.PI + 0.01) aziTrue -= 2 * (float)Math.PI;
                if (aziTrue < -Math.PI - 0.01) aziTrue += 2 * (float)Math.PI;

                var aziDiff = aziTgt - aziTrue;
                var elevationDiff = elTgt - ((Elevation.Angle + Math.PI) % (2 * Math.PI) - Math.PI);

                if (Math.Abs(aziDiff) < 0.00002)
                {
                    Azimuth.TargetVelocityRPM = 0;
                }
                else
                {
                    if (aziWrap && aziDiff > Math.PI)
                    {
                        SetAzimuth((float)(aziTgt - 2 * Math.PI), float.MaxValue, -1);
                    }
                    else if (aziWrap && aziDiff < -Math.PI)
                    {
                        SetAzimuth(float.MinValue, (float)(aziTgt + 2 * Math.PI), 1);
                    }
                    else
                    {
                        SetAzimuth(aziDiff > 0 ? (float)aziTgt : aziTrue + 0.5f * 3.1415f / 180, aziDiff < 0 ? (float)aziTgt : aziTrue - 0.5f * 3.1415f / 180, Math.Sign(aziDiff));
                    }
                }
                if (Math.Abs(elevationDiff) < 0.00002)
                {
                    Elevation.TargetVelocityRPM = 0;
                }
                else
                {
                    Elevation.UpperLimitRad = elevationDiff > 0 ? (float)elTgt : Elevation.Angle + 0.5f * 3.1415f / 180;
                    Elevation.LowerLimitRad = elevationDiff < 0 ? (float)elTgt : Elevation.Angle - 0.5f * 3.1415f / 180;
                    Elevation.TargetVelocityRPM = elRPM * Math.Sign(elevationDiff);
                }
            }
        }

        public class GunTurret : TurretDriver
        {
            public long tEID;
            double scat;
            bool spray = false, selOffset = true;
            readonly string wpns = "Weapons";

        Weapons turGuns;
            Vector3D randOffset = new Vector3D();
            public GunTurret(IMyMotorStator azi, TurretComp c)
                : base(azi, c) { }
            public override iniWrap Setup(ref CombatManager m)
            {
                var p = base.Setup(ref m);
                scat = p.Double(Lib.hdr, "Spray");
                //   aimOffset = p.Double(hdr, "aOD", 0);
                range = p.Int(Lib.hdr, "Range");
                projSpd = p.Int(Lib.hdr, "Speed");
                var sticks = p.Int(Lib.hdr, "Salvo", 0);
                spray = scat != 0;
                aziWrap = aziMax >= Math.PI && aziMin <= -Math.PI;
                p.Dispose();
                var list = new List<IMyFunctionalBlock>();
                m.Terminal.GetBlockGroupWithName(Name + " " + wpns).GetBlocksOfType(list); // e.g. PDL-LT Guns
                turGuns = new Weapons(sticks, list);
                if (Elevation != null)
                {
                    var l2 = new List<IMyCameraBlock>();
                    m.Terminal.GetBlocksOfType(l2, (b) => b.CubeGrid.EntityId == Elevation.TopGrid.EntityId && b.CustomName.Contains(Lib.array));
                    m.RegisterLidar(new LidarArray(l2, Name));
                }
                return null;
            }

            public override void SelectTarget(ref Dictionary<long, Target> targets)
            {
                // Auto reset
                aziTgt = aziRest;
                elTgt = elRest;
                if (Elevation == null || Azimuth == null) return;

                if (spray && selOffset)
                {
                    randOffset = new Vector3D(((Host.Manager.Random.NextDouble() * 2) - 1), ((Host.Manager.Random.NextDouble() * 2) - 1), ((Host.Manager.Random.NextDouble() * 2) - 1)) * scat;
                    selOffset = false;
                }

                foreach (var target in targets.Values)
                {
                    var myVel = Host.Reference.GetShipVelocities().LinearVelocity;
                    var tgtV = target.Velocity;
                    var tgtP = target.PositionUpdate(Host.Manager.Runtime);
                    tgtP += target.Radius * randOffset;
                    // Get attack position
                    var aimPoint = Lib.GetAttackPoint(tgtV - myVel, tgtP - turGuns.AimReference, projSpd);
                    if (GetTurretAngles(aimPoint))
                    {
                        hasTarget = true;
                        tEID = target.EID;
                        return;
                    }
                    hasTarget = false;
                }
            }

            public override void AimAndTrigger()
            {
                base.AimAndTrigger();
                // todo: add occlusions
                if (AtAimPoint())
                {
                    turGuns.OpenFire();
                    if (turGuns.fireTimer >= 30)
                    {
                        turGuns.fireTimer = 0;
                        selOffset = true;
                    }
                }
                else
                {
                    turGuns.HoldFire();
                    turGuns.fireTimer = 0;
                }
            }
            public void Update() => turGuns.Update();
            class Weapons
            {
                private List<IMyFunctionalBlock> Guns = new List<IMyFunctionalBlock>();
                public int
                salvoTicks = 0, // 0 or lower means no salvoing
                salvoTickCounter = 0,
                fireTicks = 0,
                fireTimer = 0;
                private int ptr = -1;
                public Vector3D AimReference
                {
                    get
                    {
                        Vector3D r = Vector3D.Zero;
                        if (Guns.Count == 0) return r;
                        foreach (var g in Guns)
                            r += g.WorldMatrix.Translation;
                        r /= Guns.Count;
                        return r;
                    }
                }

                public Weapons(int s, List<IMyFunctionalBlock> g)
                {
                    salvoTicks = s;
                    Guns = g;
                }

                public WCAPI gunsAPI = null;

                public void OpenFire()
                {
                    fireTicks = 20;
                    ++fireTimer;
                }
                public void HoldFire() => fireTicks = -1;

                public bool Active
                {
                    get
                    {
                        if (Guns.Count == 0) return false;
                        var anyWeaponOn = false;

                        for (int i = 0; i < Guns.Count; i++)
                        {
                            if (Guns[i].Enabled)
                            {
                                anyWeaponOn = true;
                                break;
                            }
                        }

                        if (!anyWeaponOn) return false;

                        return true;
                    }
                }
                public void Update(int ticks = 1)
                {
                    salvoTickCounter -= ticks;
                    fireTicks -= ticks;

                    if (Guns.Count == 0) return;
                    while (Guns[0].Closed)
                    {
                        Guns.RemoveAtFast(0);
                        if (Guns.Count == 0) return;
                    }

                    if (fireTicks > 0)
                    {
                        if (salvoTicks <= 0)
                        {
                            for (int i = 0; i < Guns.Count; i++)
                            {
                                //if (gunsAPI != null)
                                //    Guns.ForEach(gun => { gunsAPI.toggleFire(gun, true, true); });
                                //else
                                Guns.ForEach(gun => { Lib.SetValue(gun, "Shoot", true); });
                            }
                        }
                        else
                        {
                            if (salvoTickCounter < 0)
                            {
                                //if (gunsAPI != null)
                                //{
                                //    var gun = Guns[Lib.Next(ref ptr, Guns.Count)];
                                //    gunsAPI.toggleFire(gun, true, true);
                                //}
                                //else
                                //{
                                var gun = Guns[Lib.Next(ref ptr, Guns.Count)];
                                Lib.SetValue(gun, "Shoot", true);
                                //}

                                salvoTickCounter = salvoTicks;
                            }
                        }
                    }
                    else
                    {
                        for (int i = 0; i < Guns.Count; i++)
                        {
                            //if (gunsAPI != null)
                            //    Guns.ForEach(gun => { gunsAPI.toggleFire(gun, false, true); });
                            //else
                            Guns.ForEach(gun => { Lib.SetValue(gun, "Shoot", false); });
                        }
                    }
                }
            }
        }
    }
