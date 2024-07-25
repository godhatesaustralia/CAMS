using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage;
using VRageMath;

namespace IngameScript
{
    public interface IMissileRack
    {
        int Count { get; }
        bool NeedsReload { get; }
        string Launch();
        void Reload();
    }
    public static class Datalink
    {
        public static IMyIntergridCommunicationSystem IGC;
        static IMyRadioAntenna[] _broadcasters;
        public static string
            IgcParams = "IGC_MSL_PAR_MSG",
            IgcHoming = "IGC_MSL_HOM_MSG",
            IgcIff = "IGC_IFF_PKT",
            IgcFire = "IGC_MSL_FIRE_MSG",
            IgcSplash = "IGC_MSL_SPLASH_MSG",
            IgcStatus = "IGC_MSL_STAT_MSG";
        public static void Setup(Program p) 
        {
            IGC = p.IGC;
            var ant = new List<IMyRadioAntenna>();
            using (var q = new iniWrap())
                if (q.CustomData(p.Me))
                {
                    var grp = p.Terminal.GetBlockGroupWithName(q.String(Lib.HDR, "antennaGrp"));
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

    public class EKVLauncher : IMissileRack
    {
        Program _m;
        IMyMotorStator _arm;
        IMyShipWelder[] _welders;
        IMyProjector _proj;

        float
            _launchAngle,
            _RPM;
        bool _loaded = false, _gtsSearchFlag = true;
        long _reloadWaitTicks, _lastReload;
        int _reloadPtr = -1;
        public int Count => Computers.Count;
        public bool NeedsReload => !_loaded;
        public List<IMyProgrammableBlock> Computers = new List<IMyProgrammableBlock>();
        string[] Missiles;
        float[] _reload;
        Dictionary<string, IMyShipMergeBlock> _mergeDict = new Dictionary<string, IMyShipMergeBlock>();

        public EKVLauncher(IMyMotorStator a, Program p)
        {
            _arm = a;
            _m = p;
            using (var q = new iniWrap())
                if (q.CustomData(_arm))
                {
                    var h = Lib.HDR;
                    var t = q.String(h, "tags", "");
                    var rad = (float)(Lib.Pi / 180);
                    if (t != "")
                    {
                        Missiles = t.Split('\n');
                        _reload = new float[Missiles.Length];
                        if (Missiles != null)
                            for (int i = 0; i < Missiles.Length; i++)
                            {
                                Missiles[i].Trim('|');
                                var angle = q.Float(h, "weldAngle" + Missiles[i], float.MinValue);
                                if (angle != float.MinValue)
                                    _reload[i] = angle * rad;
                                var merge = (IMyShipMergeBlock)p.GridTerminalSystem.GetBlockWithName(q.String(h, "merge" + Missiles[i]));
                                if (merge != null)
                                    _mergeDict[Missiles[i]] = merge;

                            }
                    }

                    var w = q.String(h, "welders");
                    var weldnames = w.Contains(',') ? w.Split(',') : new string[] { w };
                    if (weldnames != null)
                    {
                        _welders = new IMyShipWelder[weldnames.Length];
                        for (int i = 0; i < _welders.Length; i++) {
                            _welders[i] = (IMyShipWelder)p.Terminal.GetBlockWithName(weldnames[i]);
                            if (_welders[i] != null) _welders[i].Enabled = false;
                                }
                    }
                    _launchAngle = q.Float(h, "fireAngle", 60) * rad;
                    _RPM = q.Float(h, "rpm", 5);
                    _reloadWaitTicks = q.Int(h, "reloadTicks", 210);
                    _lastReload = -_reloadWaitTicks; // makes sure reload sequence starts no matter when triggered
                }
            p.Terminal.GetBlocksOfType(Computers, b => b.CubeGrid == _arm.TopGrid);
            p.Terminal.GetBlocksOfType<IMyProjector>(null, b =>
            {
                if (b.CubeGrid == _arm.TopGrid)
                {
                    b.Enabled = false;
                    _proj = b;
                }
                return false;
            });


            _loaded = Computers.Count == Missiles.Length;
        }

        public void Reload()
        {
            if (Computers.Count == Missiles.Length) return;

            if (_gtsSearchFlag && _reloadPtr >= 0)
            {
                _m.Terminal.GetBlocksOfType<IMyProgrammableBlock>(null, b =>
                {
                    if (b.CubeGrid == _arm?.TopGrid && b.CustomName.Contains(Missiles[_reloadPtr]))
                    {
                        Computers.Add(b);
                        _gtsSearchFlag = !_gtsSearchFlag;
                    }
                    return false;
                });
            }
            if (_m.F - _lastReload >= _reloadWaitTicks)
            {
                _loaded = Computers.Count == Missiles.Length;
                if (_loaded)
                {
                    _lastReload = -_reloadWaitTicks;
                    _arm.UpperLimitRad = _launchAngle;
                    _proj.Enabled = false;
                    foreach (var w in _welders)
                        w.Enabled = false;
                    _reloadPtr = -1;
                }

                if (_reloadPtr == -1)
                {
                    foreach (var m in _mergeDict.Values) m.Enabled = false;
                    _proj.Enabled = true;
                    foreach (var w in _welders)
                        w.Enabled = true;
                    _reloadPtr++;

                }
                else if (Computers.Count != 0 && _reloadPtr < _reload.Length - 1)
                    _reloadPtr++;
                var tgt = _reload[_reloadPtr];
                _arm.LowerLimitRad = tgt;
                _arm.TargetVelocityRPM = Math.Sign(tgt - _arm.Angle) * _RPM;
                _lastReload = _m.F;
                _gtsSearchFlag = !_gtsSearchFlag;
                _mergeDict[Missiles[_reloadPtr]].Enabled = true;
            }
        }

        public string Launch()
        {
            var id = Computers[0].EntityId;
            Datalink.FireMissile(Computers[0].EntityId);
            Computers.Remove(Computers[0]);
            return id.ToString("X");
        }
    }
}