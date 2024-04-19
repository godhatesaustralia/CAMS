using Sandbox.Game.AI.Navigation;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;
using System.Security.Cryptography.X509Certificates;

namespace IngameScript
{
    public class TurretComp : CompBase
    {
        public List<RotorTurret> 
            Turrets = new List<RotorTurret>(),
            TurretsRest = new List<RotorTurret>();
        public IMyGridTerminalSystem GTS => Manager.Terminal;
        public TurretComp(string n) : base(n, UpdateFrequency.Update1)
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
                    var t = new RotorTurret(b, this);
                    t.Setup(ref m);
                    if (t.NoCAMS)
                        TurretsRest.Add(t);
                    if (t.Elevation != null)
                        Turrets.Add(t);
                }
                return true;
            });
            Manager.Screens.Add(Name, new Screen(() => Turrets.Count, new MySprite[]
            { new MySprite(SpriteType.TEXT, "", new Vector2(20, 112), null, Lib.Green, Lib.vb, 0, 0.925f),// 1. TUR NAME
              new MySprite(SpriteType.TEXT, "AZ\nEL", new Vector2(20, 160), null, Lib.Green, Lib.vb, 0, 1.825f),// 2. ANGLE HDR
              new MySprite(SpriteType.TEXT, "", new Vector2(132, 164), null, Lib.Green, Lib.vb, 0, 0.9125f),// 3. ANGLE DATA
              new MySprite(SpriteType.TEXT, "", new Vector2(488, 160), null, Lib.Green, Lib.vb, (TextAlignment)1, 1.825f),// 4. RPM
              new MySprite(SpriteType.TEXT, "", new Vector2(20, 348), null, Lib.Green, Lib.vb, 0, 0.925f)// 5. WPNS
            }, (s) =>
            {
                var turret = Turrets[s.ptr];
                string n = turret.Name;
                var ct = 14 - turret.Name.Length;
                for (; ct-- > 0;)
                    n += " ";
                s.SetData(n + $"{s.ptr + 1}/{Turrets.Count}", 0);
                s.SetData($"TGT {MathHelper.ToDegrees(turret.aziTgt).ToString("##0.#")}°\nCUR {turret.aziDeg().ToString("##0.#")}°\nTGT {MathHelper.ToDegrees(turret.elTgt).ToString("##0.#")}°\nCUR {turret.elDeg().ToString("##0.#")}°", 2);
                s.SetData($"{turret.Azimuth.TargetVelocityRPM}\n{turret.Elevation.TargetVelocityRPM}", 3);
                string cnd = "";
                foreach (var cond in turret.Conditions)
                    cnd += $"{cond.Key} " + (cond.Value ? "T/" : "F/");
                s.SetData(cnd, 4);
                //s.SetData("WEAPONS- " + (turret.isShoot ? " ENABLED" : "INACTIVE"), 4);
            }));
        }
        public override void Update(UpdateFrequency u)
        {
            if (Manager.Targets.Count != 0)
            {
                foreach (var tur in Turrets)
                {
                    if (tur.ActiveCTC) continue;
                    tur.SelectTarget(ref Manager.Targets, ref Manager.Gravity);
                    tur.AimAndTrigger();
                    tur.Update();
                }
            }
            else
            {
                foreach (var tr in TurretsRest)
                    if (!tr.ActiveCTC)
                        tr.Rest();

                foreach (var t in Turrets)
                    t.Rest();
            }
            foreach (var ti in Turrets)
                Debug += $"\n{ti.Name}";
        }
    }

    public class TurretBase
    {
        public string Name;
        public double
            aziTgt = 0,
            elTgt = 0;
        protected double
            range,
            aziRest = 0,
            elRest = 0,
            aziMin,
            aziMax,
            elMin,
            elMax;
        protected bool aziWrap = false, hasTarget = false;
        protected CompBase Host;
        protected int
            projSpd,
            aziRPM,
            elRPM;
        protected const float aimTolerance = 0.05f, radf = (float)rad;
        public const double rad = Math.PI / 180, deg = 1 / rad;
        protected readonly string elv = "Elevation", el = "EL";
        public IMyMotorStator Azimuth;
        public IMyMotorStator Elevation;
        protected IMyTurretControlBlock CTC;
        public bool ActiveCTC
        {
            get
            {
                if (CTC == null) return false;
                return (CTC.AIEnabled && CTC.HasTarget) || CTC.IsUnderControl;
            }
        }

        protected TurretBase(IMyMotorStator az, CompBase c)
        {
            Azimuth = az;
            Host = c;
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
            if (Elevation != null && Elevation.TopGrid != null)
            {
                var n = Elevation.TopGrid.CustomName;
                id = Elevation.TopGrid.EntityId;
                Elevation.TopGrid.CustomName = n.Contains("Grid") ? Name + " Arm" : n;
            }
            m.Terminal.GetBlocksOfType<IMyTurretControlBlock>(null, (b) =>
            {
                if (b.CustomName.Contains(Name) || b.CubeGrid.EntityId == Elevation.CubeGrid.EntityId )
                    CTC = b;
                return true;
            });
            return p;
        }

    }

    public class RotorTurret : TurretBase
    {
        public long tEID;
        double scat, tgtdist;
        bool spray = false, selOffset = true;
        readonly string wpns = "Weapons";
        public bool isShoot, NoCAMS;
        // temporary/debug
        public Dictionary<string, bool> Conditions = new Dictionary<string, bool>();
        public double aziDeg() {
            return Azimuth.Angle * deg;
                }
        public double elDeg() => Elevation.Angle * deg;

        Weapons Guns;
        Vector3D randOffset = new Vector3D();
        public RotorTurret(IMyMotorStator azi, TurretComp c)
            : base(azi, c) {}
        public virtual iniWrap Setup(ref CombatManager m)
        {
            var p = GetParts(ref m);
            aziMax = rad * p.Double(Lib.hdr, "aMax");
            aziMin = rad * p.Double(Lib.hdr, "aMin", -aziMax);
            elMax = rad * p.Double(Lib.hdr, "eMax", 90);
            elMin = rad * p.Double(Lib.hdr, "eMin", -elMax);
            aziRPM = p.Int(Lib.hdr, "aRPM", 30);
            elRPM = p.Int(Lib.hdr, "eRPM", 30);
            if (Elevation != null && Elevation.TopGrid != null)
            {
                // here is where i would put occlusion parse if 
            }
            NoCAMS = !p.Bool(Lib.hdr, "CAMS", true);
            scat = p.Double(Lib.hdr, "Spray");
            range = p.Double(Lib.hdr, "Range", 800);
            projSpd = p.Int(Lib.hdr, "Speed");
            var sticks = p.Int(Lib.hdr, "Salvo", 0);
            spray = scat != 0;
            aziWrap = aziMax > Math.PI && aziMin < -Math.PI;
            p.Dispose();
            var list = new List<IMyFunctionalBlock>();
            m.Terminal.GetBlockGroupWithName(Name + " " + wpns)?.GetBlocksOfType(list); // e.g. PDL-LT Guns
            Guns = new Weapons(sticks, list);
            if (Elevation != null && Elevation.TopGrid != null)
            {
                var l2 = new List<IMyCameraBlock>();
                m.Terminal.GetBlocksOfType(l2, (b) => b.CubeGrid.EntityId == Elevation.TopGrid.EntityId && b.CustomName.Contains(Lib.array));
                ((ScanComp)m.Components[Lib.sn]).Lidars.Add(new LidarArray(l2, Name));
            }
            return null;
        }

        public virtual void SelectTarget(ref Dictionary<long, Target> targets, ref Vector3D g)
        {
            // Auto reset
            aziTgt = aziRest;
            elTgt = elRest;
            tEID = -1;

            if (Elevation == null || Azimuth == null || targets.Count == 0) return;

            if (spray && selOffset)
            {
                randOffset = Lib.RandomOffset(ref Host.Manager.Random, scat);
                selOffset = false;
            }

            hasTarget = false;
            foreach (var target in targets.Values)
            {
                var myVel = Host.Reference.GetShipVelocities().LinearVelocity;
                var tgtV = target.Velocity;
                var tgtP = target.PositionUpdate(Host.Manager.Runtime);
                tgtP += target.Radius * randOffset;
                if (g != Vector3D.Zero)
                    tgtP -= g;
                // Get attack position
                var aimPoint = Lib.GetAttackPoint(tgtV - myVel, tgtP - Guns.AimReference, projSpd);
                if (GetTurretAngles(ref aimPoint))
                {
                    hasTarget = true;
                    tEID = target.EID;
                    Host.Manager.Debug.DrawLine(Guns.AimReference, Guns.AimReference + aimPoint, Lib.Green);
                    return;
                }
            }
        }

        public void Rest()
        {
            // Auto reset
            aziTgt = aziRest;
            elTgt = elRest;
            AimAndTrigger();
        }

        protected bool GetTurretAngles(ref Vector3D aimPoint)
        {
            var atkDir = Vector3D.Normalize(aimPoint);

            // Check range
            if (aimPoint.Length() > range) return false;

            // Calculate azimuth angle
            var aziVector = atkDir - Lib.Projection(atkDir, Azimuth.WorldMatrix.Up);
            var aziDot = aziVector.Dot(Azimuth.WorldMatrix.Left);
            if (double.IsNaN(aziDot)) return false;
            var aziAngle = Lib.AngleBetween(aziVector, Azimuth.WorldMatrix.Backward) * Math.Sign(aziDot);

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

        public virtual void AimAndTrigger()
        {
            if (ActiveCTC || Elevation == null || Azimuth == null)
                return;

            var aziTrue = Azimuth.Angle;
            if (aziTrue > Math.PI + 0.01) aziTrue -= 2 * (float)Math.PI;
            if (aziTrue < -Math.PI - 0.01) aziTrue += 2 * (float)Math.PI;

            var aziDiff = aziTgt - aziTrue;
            var elDiff = elTgt - ((Elevation.Angle + Math.PI) % (2 * Math.PI) - Math.PI);
            if (double.IsNaN(aziDiff)) return;
            if (Math.Abs(aziDiff) < 0.00002)
            {
                Azimuth.TargetVelocityRPM = 0;
            }
            else
            {
                if (aziWrap && aziDiff > Math.PI)
                {
                    Azimuth.LowerLimitRad = (float)(aziTgt - 2 * Math.PI);
                    Azimuth.UpperLimitRad = float.MaxValue;
                    Azimuth.TargetVelocityRPM = aziRPM * -1;
                }
                else if (aziWrap && aziDiff < -Math.PI)
                {
                    Azimuth.LowerLimitRad = float.MinValue;
                    Azimuth.UpperLimitRad = (float)(aziTgt + 2 * Math.PI);
                    Azimuth.TargetVelocityRPM = aziRPM;
                }
                else
                {
                    Azimuth.UpperLimitRad = aziDiff > 0 ? (float)aziTgt : aziTrue + 0.001f * radf;
                    Azimuth.LowerLimitRad = aziDiff < 0 ? (float)aziTgt : aziTrue - 0.001f * radf;
                    Azimuth.TargetVelocityRPM = aziRPM * Math.Sign(aziDiff);
                }
            }
            if (Math.Abs(elDiff) < 0.00002)
            {
                Elevation.TargetVelocityRPM = 0;
            }
            else
            {
                Elevation.UpperLimitRad = elDiff > 0 ? (float)elTgt : Elevation.Angle + 0.5f * radf;
                Elevation.LowerLimitRad = elDiff < 0 ? (float)elTgt : Elevation.Angle - 0.5f * radf;
                Elevation.TargetVelocityRPM = elRPM * Math.Sign(elDiff);
            }
            if ((aziTgt == aziRest && elTgt == elRest) || tEID == -1 || NoCAMS) // first quick check
                return;

            if (AtAimPoint(aziDiff, elDiff))
            {
                isShoot = true;
                Guns.OpenFire();
                Conditions["FR"] = true;
                if (Guns.fireTimer >= 30)
                {
                    Guns.fireTimer = 0;
                    selOffset = true;
                }
            }
            else
            {
                isShoot = false;
                Guns.HoldFire();
                Guns.fireTimer = 0;
            }
        }
        public bool AtAimPoint(double aD, double eD)
        {
            Conditions["AZ"] = Math.Abs(aD) < aimTolerance;
            Conditions["EL"] = Math.Abs(eD) < aimTolerance;
            if (Math.Abs(aD) > aimTolerance || Math.Abs(eD) > aimTolerance)
                return false;
            // todo: add occlusions check
            return true;
        }

        public void Update() => Guns.Update(1);
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
                    Vector3D r = new Vector3D();
                    if (Guns.Count == 0) return Vector3D.Zero;
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
                            Lib.SetValue(Guns[i], "Shoot", true);
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
    public class MapTurret : RotorTurret
    {
        List<IMyCameraBlock> Cameras = new List<IMyCameraBlock>();
        int oclTest, oclLast, elRange, oclTotal;
        public MapTurret(IMyMotorStator azi, TurretComp c)
        : base(azi, c) { }

        public override iniWrap Setup(ref CombatManager m)
        {
            base.Setup(ref m);
            m.Terminal.GetBlocksOfType(Cameras, (c) =>
            {
                bool b = c.CubeGrid.EntityId == Elevation.TopGrid.EntityId && !c.CustomName.Contains(Lib.array);
                c.EnableRaycast = b;
                return b;
            });
            Cameras[0].ShowOnHUD = true;
            oclTotal = Convert.ToInt32((aziMax - aziMin) * (elMax - elMin) * deg);
            return null;
        }
    }
}
