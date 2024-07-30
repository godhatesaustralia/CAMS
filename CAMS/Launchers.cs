using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage;
using VRageMath;

namespace IngameScript
{
    public static class Datalink
    {
        public static long ID;
        public static IMyIntergridCommunicationSystem IGC;
        public static IMyBroadcastListener
            MissileReady,
            MissileSplash;
        static IMyRadioAntenna[] _broadcasters;
        public static string
            IgcParams = "IGC_MSL_PAR_MSG",
            IgcHoming = "IGC_MSL_HOM_MSG",
            IgcInit = "IGC_MSL_RDY_MSG",
            IgcFire = "IGC_MSL_FIRE_MSG",
            IgcSplash = "IGC_MSL_SPLASH_MSG",
            IgcStatus = "IGC_MSL_STAT_MSG";

        public static void Setup(Program p)
        {
            ID = p.Me.EntityId;
            IGC = p.IGC;
            MissileReady = p.IGC.RegisterBroadcastListener(IgcInit);
            MissileSplash = p.IGC.RegisterBroadcastListener(IgcSplash);
            var ant = new List<IMyRadioAntenna>();
            using (var q = new iniWrap())
                if (q.CustomData(p.Me))
                {
                    var grp = p.Terminal.GetBlockGroupWithName(q.String(Lib.H, "antennaGrp"));
                    if (grp != null)
                        grp.GetBlocksOfType(ant);
                    else p.Terminal.GetBlocksOfType(ant);
                }
                else p.Terminal.GetBlocksOfType(ant);
            _broadcasters = ant.ToArray();
            //if (_broadcasters != null && _broadcasters.Length > 0)
            //    _broadcasters[0].Enabled = _broadcasters[0].EnableBroadcasting = true;
        }

        public static void FireMissile(long id) => IGC.SendUnicastMessage(id, IgcFire, "");

        public static void SendWhamTarget(ref Vector3D hitPos, ref Vector3D tPos, ref Vector3D tVel, ref Vector3D preciseOffset, ref Vector3D myPos, double elapsed, long tEID)
        {
            var mat1 = new Matrix3x3();
            FillMatrix(ref mat1, ref hitPos, ref tPos, ref tVel);

            var mat2 = new Matrix3x3();
            FillMatrix(ref mat2, ref preciseOffset, ref myPos, ref Vector3D.Zero);

            var msg = new MyTuple<Matrix3x3, Matrix3x3, float, long, long>
            {
                Item1 = mat1,
                Item2 = mat2,
                Item3 = (float)elapsed,
                Item4 = tEID,
                Item5 = _broadcasters[0].EntityId,
            };
            IGC.SendBroadcastMessage(IgcHoming, msg);
        }

        public static void SendParams(bool kill, bool stealth, bool spiral, bool topdown, bool precise, bool retask)
        {
            byte packed = 0;
            packed |= BoolToByte(kill);
            packed |= (byte)(BoolToByte(stealth) << 1);
            packed |= (byte)(BoolToByte(spiral) << 2);
            packed |= (byte)(BoolToByte(topdown) << 3);
            packed |= (byte)(BoolToByte(precise) << 4);
            packed |= (byte)(BoolToByte(retask) << 5);

            var msg = new MyTuple<byte, long>
            {
                Item1 = packed,
                Item2 = _broadcasters[0].EntityId
            };

            IGC.SendBroadcastMessage(IgcParams, msg);
        }

        static byte BoolToByte(bool value) => value ? (byte)1 : (byte)0;
        static void FillMatrix(ref Matrix3x3 mat, ref Vector3D col0, ref Vector3D col1, ref Vector3D col2)
        {
            mat.M11 = (float)col0.X;
            mat.M21 = (float)col0.Y;
            mat.M31 = (float)col0.Z;

            mat.M12 = (float)col1.X;
            mat.M22 = (float)col1.Y;
            mat.M32 = (float)col1.Z;

            mat.M13 = (float)col2.X;
            mat.M23 = (float)col2.Y;
            mat.M33 = (float)col2.Z;
        }

    }

    /// <summary>
    /// Launcher representation of a general anti-ship missile running WHAM-C.
    /// </summary>
    public class MSL
    {
        public string Name, ComputerName;
        public IMyShipMergeBlock Hardpoint;
        public IMyProgrammableBlock Computer;
        public MSL(string n, string cn, IMyProgrammableBlock c, IMyShipMergeBlock m)
        {
            Name = n;
            ComputerName = cn;
            Computer = c;
            Hardpoint = m;
        }
    }

    public enum LauncherState
    {
        /// <summary>
        /// All missiles depleted
        /// </summary>
        Empty,
        /// <summary>
        /// Used on script startup when all missiles are already present
        /// </summary>
        Boot,
        /// <summary>
        /// Searching grid terminal system for next missile computer
        /// </summary>
        ReloadSearch,
        /// <summary>
        /// Waiting for handshake IGC signal
        /// </summary>
        ReloadWait,
        /// <summary>
        /// Launcher arm rotating
        /// </summary>
        Moving,
        /// <summary>
        /// Launcher able to fire
        /// </summary>
        Ready
    }

    public class ArmLauncherWHAM
    {
        public int Total = 0;
        public long NextUpdateF = 0;
        public LauncherState Status = 0;
        /// <summary>
        /// Launcher hinge
        /// </summary>
        IMyMotorStator _arm;
        IMyShipWelder _welder;
        IMyProjector _proj;
        SortedSet<EKV>
            eKVsReload = new SortedSet<EKV>(),
            eKVsLaunch = new SortedSet<EKV>();
        float _fireAngle, _tgtAngle, _RPM;
        const int
            ACTIVE_T = 5,
            WAIT_T = 20,
            SEARCH_T = 40;
        IMyGridTerminalSystem _gts;

        /// <summary>
        /// Launcher representation of an EKV (Explosive Kill Vehicle) interceptor running WHAM-C.
        /// </summary>
        private class EKV : MSL, IComparable<EKV>
        {
            /// <summary>
            /// Reloading angle of the missile in radians
            /// </summary>
            public float Reload;

            public EKV(string n, string cn, IMyProgrammableBlock c, IMyShipMergeBlock m, float r) : base(n, cn, c ,m)
            {
                Reload = r;
            }

            public int CompareTo(EKV o)
            {
                if (Reload == o.Reload)
                    return Computer == null ? -1 : Name.CompareTo(o.Name);
                else return Reload < o.Reload ? -1 : 1;
            }
        }
        public ArmLauncherWHAM(IMyMotorStator a, Program p)
        {
            _arm = a;
            _gts = p.GridTerminalSystem;
        }

        /// <summary>
        /// Parses custom data settings and creates EKVs to be handled by the launcher.
        /// </summary>
        /// <returns>Whether launcher initialization was a success or failure.</returns>
        public bool Init()
        {
            if (_arm == null) return false;
            int c = 0;
            using (var q = new iniWrap())
                if (!q.CustomData(_arm)) return false;
                else
                {
                    var h = Lib.H;
                    var t = q.String(h, "tags", "");
                    var rad = (float)(Lib.PI / 180);
                    if (t != "")
                    {
                        var tags = t.Split('\n');
                        if (tags != null)
                            for (; Total < tags.Length; Total++)
                            {
                                tags[Total].Trim('|');
                                var angle = q.Float(h, "weldAngle" + tags[Total], float.MinValue);
                                if (angle != float.MinValue)
                                    angle *= rad;
                                var merge = (IMyShipMergeBlock)_gts.GetBlockWithName(q.String(h, "merge" + tags[Total]));
                                if (merge != null && angle != float.MinValue)
                                {
                                    var n = q.String(h, "computer" + tags[Total], tags[Total] + " Computer WHAM");
                                    var cptr = (IMyProgrammableBlock)_gts.GetBlockWithName(n);
                                    eKVsReload.Add(new EKV(tags[Total], n, cptr, merge, angle));
                                    if (cptr != null) c++;
                                }
                            }
                        Total++;
                    }

                    var w = q.String(h, "welder", "");
                    _welder = _gts.GetBlockWithName(w) as IMyShipWelder;
                    if (_welder == null)
                        return false;
                    _welder.Enabled = false;
                    _fireAngle = _tgtAngle = q.Float(h, "fireAngle", 60) * rad;
                    _RPM = q.Float(h, "rpm", 5);

                    _gts.GetBlocksOfType<IMyProjector>(null, b =>
                    {
                        if (b.CubeGrid == _arm.TopGrid)
                        {
                            b.Enabled = false;
                            _proj = b;
                        }
                        return false;
                    });
                }
            if (Total == c)
                Status = LauncherState.Boot;
            return _welder != null && _proj != null && eKVsReload.Count > 0;
        }

        /// <summary>
        /// Attempts to fire a missile. If none remain after firing, the launcher enters its reload sequence.
        /// </summary>
        /// <param name="id">Unique ID of the fired missile.</param>
        /// <returns>Whether a missile was fired successfully.</returns>
        public bool Fire(out long id)
        {
            id = -1;
            if (Status == LauncherState.Ready)
            {
                var e = eKVsLaunch.Max;
                if (e?.Computer != null)
                {
                    eKVsLaunch.Remove(e);
                    id = e.Computer.EntityId;
                    Datalink.FireMissile(id);
                    e.Computer = null;
                    eKVsReload.Add(e);
                    if (eKVsLaunch.Count == 0)
                        Status = LauncherState.Empty;
                    return true;
                }
            }
            return false;
        }

        public int Update()
        {
            if (Status == LauncherState.Ready || Status == LauncherState.ReloadWait)
                return WAIT_T;
            var e = eKVsReload.Min;
            if (Status == LauncherState.ReloadSearch)
            {
                e.Computer = (IMyProgrammableBlock)_gts.GetBlockWithName(e.ComputerName);
                if (!e.Computer?.IsRunning ?? false && e.Computer.TryRun($"setup{Datalink.ID}"))
                    Status = LauncherState.ReloadWait;
                else
                    return SEARCH_T;
                return WAIT_T;
            }
            else if (Status == LauncherState.Moving)
            {
                if (Math.Abs(_arm.Angle - _tgtAngle) < 0.01)
                {
                    _arm.TargetVelocityRPM = 0;
                    if (eKVsReload.Count != 0)
                    {
                        e.Hardpoint.Enabled = true;
                        _welder.Enabled = true;
                        Status = LauncherState.ReloadSearch;
                    }
                    else if (_tgtAngle == _fireAngle)
                    {
                        _proj.Enabled = false;
                        Status = LauncherState.Ready;
                    }
                    return SEARCH_T;
                }
                return ACTIVE_T;
            }
            else if (Status == LauncherState.Empty)
            {
                foreach (var ekv in eKVsReload)
                    ekv.Hardpoint.Enabled = false;
                e.Hardpoint.Enabled = _proj.Enabled = true;
                _tgtAngle = e.Reload;
                StartRotation();
            }
            else if (Status == LauncherState.Boot)
            {
                e.Computer = (IMyProgrammableBlock)_gts.GetBlockWithName(e.ComputerName);
                if (_arm.Angle != _tgtAngle)
                {
                    StartRotation();
                    Status = LauncherState.Boot;
                }
                if (!e.Computer.IsRunning)
                    e.Computer?.TryRun($"setup{Datalink.ID}");
            }
            return ACTIVE_T;
        }

        void StartRotation()
        {
            var adj = _arm.Angle;

            if (adj < -MathHelper.PiOver2)
                adj += MathHelper.Pi;
            else if (adj > MathHelper.PiOver2)
                adj -= MathHelper.Pi;

            var next = adj - _tgtAngle;
            if (next < 0)
            {
                _arm.LowerLimitRad = _tgtAngle;
                _arm.TargetVelocityRPM = -_RPM;
            }
            else if (next > 0)
            {
                _arm.UpperLimitRad = _tgtAngle;
                _arm.TargetVelocityRPM = _RPM;
            }
            _welder.Enabled = false;
            Status = LauncherState.Moving;
        }

        /// <summary>
        /// Checks if an ID belongs to this launcher.
        /// If it does, advances the reload sequence to next missile or moves the launcher to reload position if none are left to reload.
        /// </summary>
        /// <param name="id">Missile computer entity ID received via IGC.</param>
        /// <returns>Whether handshake was successfully received by this launcher.</returns>
        public bool CheckHandshake(long id)
        {
            if (Status == LauncherState.Ready || eKVsReload.Count == 0)
                return false;
            var e = eKVsReload.Min;
            bool ok = e.Computer.EntityId == id;
            if (ok)
            {
                Datalink.IGC.SendUnicastMessage(id, Datalink.IgcInit, "");
                eKVsReload.Remove(e);
                eKVsLaunch.Add(e);
                // if reload set is now empty, go to firing position
                // otherwise go to next reload position
                _tgtAngle = eKVsReload.Count == 0 ? _fireAngle : eKVsReload.Min.Reload;
                StartRotation();
            }
            return ok;
        }
    }

    public class StaticLauncherWHAM
    {
        public int Total = 0;
        public long NextUpdateF = 0;
        IMyProjector _proj;
        IMyShipWelder[] _welders;
        IMyGridTerminalSystem _gts;
        public StaticLauncherWHAM(Program p)
        {
            _gts = p.GridTerminalSystem;
        }
    }
}