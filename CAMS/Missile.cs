//using System.Collections.Generic;
//using System;
//using VRage.Game.ModAPI.Ingame;
//using VRageMath;
//using Sandbox.ModAPI.Ingame;
//using SpaceEngineers.Game.ModAPI.Ingame;

// TODO: EVERYTHING

//namespace IngameScript
//{
//    public class Launcher : CompBase
//    {
//        public Dictionary<MyItemType, int> TorpedoParts = new Dictionary<MyItemType, int>();

//        public int
//            PlungeDist = 1000,
//            ReloadCooldownMS = 5000,
//            AutoFireRange = 15000,
//            AutoFireTubeMS = 500,
//            AutoFireTargetMS = 2000,
//            AutoFireRadius = 30,
//            AutoFireSizeMask = 1,
//            EvasionAdjTimeMin = 500,
//            EvasionAdjTimeMax = 1000;

//        public List<IMyInteriorLight> AutofireIndicator = new List<IMyInteriorLight>();

//        public float
//            GuidanceStartSeconds = 2,
//            TrickshotDistance = 1200,
//            TrickshotTerminalDistanceSq = 1000,
//            EvasionDistSqStart = 2000 * 2000,
//            EvasionDistSqEnd = 800 * 800;

//        public bool
//            AutoFire = false,
//            Trickshot = false,
//            Evasion = false;


//        public double
//            HitOffset = 0,
//        CruiseDistSqMin = 10000,
//        EvasionOffsetMagnitude = 2; // 2x radius

//        public List<long> engagedEIDs = new List<long>();
//        public long
//            LastLaunch,
//            LoadingMask,
//            SuppressMask;


//        public Missile Fire(long f, Target target = null, bool trickshot = true, long offset = 0)
//        {
//            foreach (ITorpedoControllable tube in Children)
//            {
//                if (tube.IsOK) return tube.Fire(f, offset, target, trickshot);
//            }
//            return null;
//        }
//    }

//    public class Missile
//    {
//        public enum AltModeStage
//        {
//            Off,
//            Setup,
//            Active,
//            Terminal,
//        }

//        public List<IMyGyro> Gyros = new List<IMyGyro>();
//        public HashSet<IMyWarhead> Warheads = new HashSet<IMyWarhead>();
//        public HashSet<IMyThrust> Thrusters = new HashSet<IMyThrust>();
//        public HashSet<IMyBatteryBlock> Batteries = new HashSet<IMyBatteryBlock>();
//        public HashSet<IMyGasTank> Tanks = new HashSet<IMyGasTank>();
//        public List<IMyCameraBlock> Cameras = new List<IMyCameraBlock>();
//        public List<float> CameraExtends = new List<float>();
//        public IMySensorBlock Sensor;
//        public IMyShipController Controller;
//        public HashSet<IMyShipMergeBlock> Splitters = new HashSet<IMyShipMergeBlock>();

//        public IMyTerminalBlock Fuse;

//        GyroControl gyroControl;

//        PDController yawCtrl = new PDController(DEF_PD_P_GAIN, DEF_PD_D_GAIN, 10);
//        PDController pitchCtrl = new PDController(DEF_PD_P_GAIN, DEF_PD_D_GAIN, 10);

//        long launchF = -1, lastAdjustF;

//        public bool 
//            canInitialize = true,
//            Reserve = false,
//            Disabled = false;
//        public Target Target = null;

//        double lastSpeed;


//        public Vector3D AccelerationVector;

//        bool 
//            initialized = false,
//            plunging = true,
//            cruising = false,
//            canCruise = false;
//        int runs = 0;

//        Vector3D 
//            lastTargetVelocity,
//            RandomHitboxOffset,
//            TrickshotOffset = Vector3D.Zero,
//            EvasionOffset = Vector3D.Zero;

//        public AltModeStage TrickshotMode = AltModeStage.Off

//        AltModeStage EvasionMode = AltModeStage.Off;
//        TimeSpan LastCourseAdjustTime;

//        public bool proxArmed = false;
//        Launcher _host;

//        public bool AddPart(IMyTerminalBlock b)
//        {
//            bool part = false;
//            if (b.CustomName.Contains("[F]")) { Fuse = b; part = true; }
//            if (b is IMyShipController) { Controller = (IMyShipController)b; part = true; }
//            if (b is IMyGyro) { Gyros.Add((IMyGyro)b); part = true; }
//            if (b is IMyCameraBlock)
//            {
//                var camera = (IMyCameraBlock)b;
//                Cameras.Add(camera);
//                camera.EnableRaycast = true;
//                float extents;
//                float.TryParse(camera.CustomData, out extents);
//                CameraExtends.Add(extents);
//                part = true;
//            }
//            if (b is IMySensorBlock) { Sensor = (IMySensorBlock)b; part = true; }
//            if (b is IMyThrust) { Thrusters.Add((IMyThrust)b); ((IMyThrust)b).Enabled = false; part = true; }
//            if (b is IMyWarhead) { Warheads.Add((IMyWarhead)b); part = true; }
//            if (b is IMyShipMergeBlock) { Splitters.Add((IMyShipMergeBlock)b); part = true; }
//            if (b is IMyBatteryBlock) { Batteries.Add((IMyBatteryBlock)b); ((IMyBatteryBlock)b).Enabled = false; part = true; }
//            if (b is IMyGasTank) { Tanks.Add((IMyGasTank)b); ((IMyGasTank)b).Enabled = true; part = true; }
//            return part;
//        }

//        public void Init(long fstart)
//        {
//            initialized = true;
//            EvasionMode = group.Evasion ? AltModeStage.Setup : AltModeStage.Off;
//            gyroControl = new GyroControl(Gyros);
//            var refWorldMatrix = Controller.WorldMatrix;
//            gyroControl.Init(ref refWorldMatrix);
//            foreach (var tank in Tanks)
//                tank.Stockpile = false;

//            foreach (var Gyro in Gyros)
//            {
//                Gyro.GyroOverride = true;
//                Gyro.Enabled = true;
//            }
//            launchF = fstart;
//            var rand = _host.Main.RNG;
//            RandomHitboxOffset = new Vector3D(rand.NextDouble() - 0.5, rand.NextDouble() - 0.5, rand.NextDouble() - 0.5);
//        }

//        void Split() 
//        {
//            foreach (var merge in Splitters)
//                merge.Enabled = false;
//        }

//        public void CanEngageTarget(Target tgt, long f)
//        {
//            if (!initialized) return;
//            if (!IsOK())
//            {
//                foreach (var Gyro in Gyros)
//                {
//                    Gyro.Enabled = false;
//                }
//                Arm();
//                Disabled = true;
//            }
//            if (Disabled) return;
//            if (CanonicalTime - launchTime < TimeSpan.FromSeconds(Group.GuidanceStartSeconds)) return;
//            if (tgt == null) return;

//            if (CanonicalTime - launchTime > TimeSpan.FromSeconds(Group.GuidanceStartSeconds + 1) && SubTorpedos.Count > 0) Split();

//            Target = tgt;

//            Vector3D normalAccelerationVector = RefreshNavigation(f);

//            canCruise = canCruise && normalAccelerationVector.Dot(Vector3D.Normalize(Controller.GetShipVelocities().LinearVelocity)) > .98;
//            if (cruising != canCruise)
//            {
//                cruising = canCruise;
//                foreach (var thruster in Thrusters) thruster.ThrustOverridePercentage = canCruise ? .25f : 1f;
//            }

//            AimAtTarget(normalAccelerationVector);
//        }

//        public void FastUpdate()
//        {
//            if (initialized)
//            {
//                runs++;
//                if (runs == 2)
//                {
//                    //                    HostSubsystem.Context.Log.Debug("Fast Tube #" + TubeIndex);
//                    foreach (var thruster in Thrusters)
//                    {
//                        thruster.Enabled = true;
//                        thruster.ThrustOverridePercentage = 1;
//                    }
//                    foreach (var battery in Batteries)
//                    {
//                        battery.Enabled = true;
//                    }
//                }
//            }
//        }

//        public bool IsOK()
//        {
//            return Gyros.Count > 0 && Controller != null && Controller.IsFunctional && Thrusters.Count > 0;
//        }

//        void AimAtTarget(Vector3D TargetVector)
//        {
//            //TargetVector.Normalize();
//            //TargetVector += Controller.WorldMatrix.Up * 0.1;

//            //---------- Activate Gyroscopes To Turn Towards tgt ----------

//            double 
//                absX = Math.Abs(TargetVector.X),
//                absY = Math.Abs(TargetVector.Y),
//                absZ = Math.Abs(TargetVector.Z),

//                yawInput, pitchInput;

//            if (absZ < 0.00001)
//            {
//                yawInput = pitchInput = MathHelperD.PiOver2;
//            }
//            else
//            {
//                bool 
//                    flipYaw = absX > absZ,
//                    flipPitch = absY > absZ;

//                yawInput = FastAT(Math.Max(flipYaw ? (absZ / absX) : (absX / absZ), 0.00001));
//                pitchInput = FastAT(Math.Max(flipPitch ? (absZ / absY) : (absY / absZ), 0.00001));

//                if (flipYaw) yawInput = MathHelperD.PiOver2 - yawInput;
//                if (flipPitch) pitchInput = MathHelperD.PiOver2 - pitchInput;

//                if (TargetVector.Z > 0)
//                {
//                    yawInput = (Math.PI - yawInput);
//                    pitchInput = (Math.PI - pitchInput);
//                }
//            }

//            //---------- PID Controller Adjustment ----------

//            if (double.IsNaN(yawInput)) yawInput = 0;
//            if (double.IsNaN(pitchInput)) pitchInput = 0;

//            yawInput *= GetSign(TargetVector.X);
//            pitchInput *= GetSign(TargetVector.Y);

//            yawInput = yawCtrl.Filter(yawInput, 2);
//            pitchInput = pitchCtrl.Filter(pitchInput, 2);

//            if (Math.Abs(yawInput) + Math.Abs(pitchInput) > DEF_PD_AIM_LIMIT)
//            {
//                double adjust = DEF_PD_AIM_LIMIT / (Math.Abs(yawInput) + Math.Abs(pitchInput));
//                yawInput *= adjust;
//                pitchInput *= adjust;
//            }

//            //---------- Set Gyroscope Parameters ----------

//            gyroControl.SetGyroYaw((float)yawInput);
//            gyroControl.SetGyroPitch((float)pitchInput);
//            gyroControl.SetGyroRoll(ROLL_THETA);
//        }

//        const double 
//            DEF_PD_P_GAIN = 10,
//            DEF_PD_D_GAIN = 5,
//            DEF_PD_AIM_LIMIT = 6.3;

//        float ROLL_THETA = 0;

//        Vector3D RefreshNavigation(long f)
//        {
//            var targetPosition = Target.AdjustedPosition(f);
//            targetPosition += (RandomHitboxOffset * Target.Radius * _host.HitOffset);
//            var rangeVector = targetPosition - Controller.WorldMatrix.Translation;
//            var waypointVector = rangeVector;
//            var distTargetSq = rangeVector.LengthSquared();

//            var rand = _host.Main.RNG;
//            proxArmed = proxArmed ? proxArmed : distTargetSq < 120 * 120;

//            var grav = Controller.GetNaturalGravity();
//            bool inGrav = grav != Vector3D.Zero;
//            // plunging makes no sense in space;
//            plunging = inGrav && plunging;
//            // Trickshot a bad idea in gravity
//            TrickshotMode = inGrav ? AltModeStage.Off : TrickshotMode;
//            // Can't cruise in gravity or too close to target
//            canCruise = !inGrav && (distTargetSq > _host.CruiseDistSqMin);

//            // TRICKSHOT - SETUP
//            if (TrickshotMode == AltModeStage.Setup)
//            {
//                TrickshotOffset = TrigHelpers.GetRandomPerpendicularNormalToDirection(rand, rangeVector);
//                TrickshotOffset *= Group.TrickshotDistance;

//                TrickshotMode = AltModeStage.Active;
//            }

//            // EVASION - SETUP
//            if (EvasionMode == AltModeStage.Setup)
//            {
//                EvasionMode = AltModeStage.Active;
//            }

//            // EVASION - ACTIVE
//            if (EvasionMode == AltModeStage.Active)
//            {
//                if (distTargetSq <= Group.EvasionDistSqStart &&
//                    distTargetSq >= Group.EvasionDistSqEnd)
//                {
//                    if (lastAdjustF - f < 0)
//                    {
//                        var invlerp = VectorHelpers.InvLerp(distTargetSq, Group.EvasionDistSqStart, Group.EvasionDistSqEnd);
//                        var nextCourseTime = VectorHelpers.Lerp(invlerp, Group.EvasionAdjTimeMax, Group.EvasionAdjTimeMin);
//                        LastCourseAdjustTime = CanonicalTime + TimeSpan.FromMilliseconds(nextCourseTime);

//                        EvasionOffset = TrigHelpers.GetRandomPerpendicularNormalToDirection(rand, rangeVector);

//                        var offsetMag = VectorHelpers.Lerp(invlerp, Group.EvasionOffsetMagnitude * Target.Radius, Target.Radius);
//                        EvasionOffset *= offsetMag;

//                        // Whip did this but less cool:
//                        // _maxRandomAccelRatio = 0.25
//                        // double angle = RNGesus.NextDouble() * Math.PI * 2.0;
//                        // _randomizedHeadingVector = Math.Sin(angle) * _missileReference.WorldMatrix.Up + Math.Cos(angle) * _missileReference.WorldMatrix.Right;
//                        // _randomizedHeadingVector *= _maxRandomAccelRatio;
//                    }
//                }
//                else
//                {
//                    EvasionOffset = Vector3D.Zero;
//                }
//            }

//            // TRICKSHOT - ACTIVE
//            if (TrickshotMode == AltModeStage.Active)
//            {
//                waypointVector += TrickshotOffset;
//                if (waypointVector.LengthSquared() < 100 * 100 ||
//                    distTargetSq < Group.TrickshotTerminalDistanceSq)
//                {
//                    TrickshotOffset = Vector3D.Zero;
//                    TrickshotMode = AltModeStage.Off;
//                }
//            }

//            // PLUNGING 
//            if (plunging)
//            {
//                var gravDir = grav;
//                gravDir.Normalize();

//                var targetHeightDiff = rangeVector.Dot(-gravDir); // Positive if target is higher than missile

//                if ((rangeVector.LengthSquared() < Group.PlungeDist * Group.PlungeDist)
//                   && targetHeightDiff > 0)
//                {
//                    plunging = false;
//                }

//                if (plunging)
//                {
//                    waypointVector -= gravDir * Group.PlungeDist;
//                    if (waypointVector.LengthSquared() < 300 * 300)
//                        plunging = false;
//                }
//            }

//            // EVASION - We apply the evasion effect last as it can
//            // interfere with plunge & trickshot detecting that they are complete.
//            waypointVector += EvasionOffset;

//            var linearVelocity = Controller.GetShipVelocities().LinearVelocity;
//            Vector3D velocityVector = Target.Velocity - linearVelocity;
//            var speed = Controller.GetShipSpeed();

//            double alignment = linearVelocity.Dot(ref waypointVector);
//            if (alignment > 0)
//            {
//                Vector3D 
//                    rangeDivSqVector = waypointVector / waypointVector.LengthSquared(),
//                    compensateVector = velocityVector - (velocityVector.Dot(ref waypointVector) * rangeDivSqVector),

//                    targetAccel = (lastTargetVelocity - Target.Velocity) * 0.16667,

//                    targetANVector = targetAccel - grav - (targetAccel.Dot(ref waypointVector) * rangeDivSqVector);

//                bool accelerating = speed > lastSpeed + 1;
//                if (accelerating)
//                {
//                    canCruise = false;
//                    AccelerationVector = linearVelocity + (3.5 * 1.5 * (compensateVector + (0.5 * targetANVector)));
//                }
//                else
//                {
//                    AccelerationVector = linearVelocity + (3.5 * (compensateVector + (0.5 * targetANVector)));
//                }
//            }
//            // going backwards or perpendicular
//            else
//            {
//                AccelerationVector = (waypointVector * 0.1) + velocityVector;
//            }

//            lastTargetVelocity = Target.Velocity;
//            lastSpeed = speed;

//            return Vector3D.TransformNormal(AccelerationVector, MatrixD.Transpose(Controller.WorldMatrix));
//        }

//        void Arm()
//        {
//            foreach (var warhead in Warheads) warhead.IsArmed = true;
//        }

//        public void Detonate()
//        {
//            foreach (var warhead in Warheads)
//            {
//                warhead.IsArmed = true;
//                warhead.Detonate();
//            }
//        }

//        double FastAT(double x)
//        {
//            return 0.785375 * x - x * (x - 1.0) * (0.2447 + 0.0663 * x);
//        }

//        double GetSign(double value)
//        {
//            return value < 0 ? -1 : 1;
//        }

//    }

//    public class GyroControl
//    {
//        Action<IMyGyro, float>[] profiles =
//        {
//            (g, v) => { g.Yaw = -v; },
//            (g, v) => { g.Yaw = v; },
//            (g, v) => { g.Pitch = -v; },
//            (g, v) => { g.Pitch = v; },
//            (g, v) => { g.Roll = -v; },
//            (g, v) => { g.Roll = v; }
//        };

//        List<IMyGyro> gyros;

//        byte[] 
//            gyroYaw,
//            gyroPitch,
//            gyroRoll;

//        int active = 0;

//        public GyroControl(List<IMyGyro> newGyros)
//        {
//            gyros = newGyros;
//        }

//        public void Init(ref MatrixD refWorldMatrix)
//        {
//            if (gyros == null)
//            {
//                gyros = new List<IMyGyro>();
//            }

//            gyroYaw = new byte[gyros.Count];
//            gyroPitch = new byte[gyros.Count];
//            gyroRoll = new byte[gyros.Count];

//            for (int i = 0; i < gyros.Count; i++)
//            {
//                gyroYaw[i] = SetRelativeDirection(gyros[i].WorldMatrix.GetClosestDirection(refWorldMatrix.Up));
//                gyroPitch[i] = SetRelativeDirection(gyros[i].WorldMatrix.GetClosestDirection(refWorldMatrix.Left));
//                gyroRoll[i] = SetRelativeDirection(gyros[i].WorldMatrix.GetClosestDirection(refWorldMatrix.Forward));
//            }

//            active = 0;
//        }

//        public byte SetRelativeDirection(Base6Directions.Direction dir)
//        {
//            switch (dir)
//            {
//                case Base6Directions.Direction.Up:
//                    return 1;
//                case Base6Directions.Direction.Down:
//                    return 0;
//                case Base6Directions.Direction.Left:
//                    return 2;
//                case Base6Directions.Direction.Right:
//                    return 3;
//                case Base6Directions.Direction.Forward:
//                    return 4;
//                case Base6Directions.Direction.Backward:
//                    return 5;
//            }
//            return 0;
//        }

//        public void SetGyroOverride(bool bOverride)
//        {
//            CheckGyro();

//            for (int i = 0; i < gyros.Count; i++)
//            {
//                if (i == active) gyros[i].GyroOverride = bOverride;
//                else gyros[i].GyroOverride = false;
//            }
//        }

//        public void SetGyroYaw(float yawRate)
//        {
//            CheckGyro();

//            if (active < gyros.Count)
//            {
//                profiles[gyroYaw[active]](gyros[active], yawRate);
//            }
//        }

//        public void SetGyroPitch(float pitchRate)
//        {
//            if (active < gyros.Count)
//            {
//                profiles[gyroPitch[active]](gyros[active], pitchRate);
//            }
//        }

//        public void SetGyroRoll(float rollRate)
//        {
//            if (active < gyros.Count)
//            {
//                profiles[gyroRoll[active]](gyros[active], rollRate);
//            }
//        }

//        void CheckGyro()
//        {
//            while (active < gyros.Count)
//            {
//                if (gyros[active].IsFunctional)
//                {
//                    break;
//                }
//                else
//                {
//                    IMyGyro gyro = gyros[active];

//                    gyro.Enabled = gyro.GyroOverride = false;
//                    gyro.Yaw = gyro.Pitch = gyro.Roll = 0f;

//                    active++;
//                }
//            }
//        }
//    }

//}