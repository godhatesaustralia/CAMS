using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage;
using VRageMath;

namespace IngameScript
{
    public enum LauncherState
    {
        /// <summary>
        /// All missiles depleted
        /// </summary>
        Empty,
        /// <summary>
        /// Used on script startup when all missiles are already present
        /// </summary>
        Reload,
        /// <summary>
        /// Launcher arm rotating
        /// </summary>
        Moving,
        /// <summary>
        /// Launcher able to fire
        /// </summary>
        Ready
    }

    public class ArmLauncher
    {
        public string Name;
        public readonly string[] Report = new string[] { "", "" };
        int _msgPtr = 0, _loadPtr = 0;
        public int Total = 0;
        public long NextUpdateF = 0;
        public LauncherState Status = 0;
        /// <summary>
        /// Launcher hinge
        /// </summary>
        IMyMotorStator _arm;
        IMyShipWelder _welder;
        IMyProjector _proj;
        Hardpoint[] _bases;
        List<Missile> _missiles;
        float _fireAngle, _tgtAngle, _RPM;
        const int
            ACTIVE_T = 3,
            READY_T = 30,
            RELOAD_T = 7;
        const float TOL = 0.01f;
        Program _p;
        public ArmLauncher(IMyMotorStator a, Program p)
        {
            _arm = a;
            _p = p;
        }

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
                    Name = q.String(h, "name", _arm.CustomName);
                    if (t != "")
                    {
                        var tags = t.Split('\n');
                        var temp = new SortedSet<Hardpoint>();
                        _missiles = new List<Missile>(tags.Length);
                        for (; Total < tags.Length;)
                        {
                            tags[Total].Trim('|');
                            var angle = q.Float(h, "weldAngle" + tags[Total], float.MinValue);
                            if (angle != float.MinValue)
                                angle *= rad;

                            var merge = (IMyShipMergeBlock)_p.GridTerminalSystem.GetBlockWithName(q.String(h, "merge" + tags[Total]));
                            if (merge != null && angle != float.MinValue)
                            {
                                var hpt = new Hardpoint(tags[Total], angle);
                                if (hpt.Init(merge))
                                {
                                    temp.Add(hpt);
                                    if (merge.IsConnected) c++;
                                    Total++;
                                }
                            }
                        }
                        _bases = temp.ToArray(); // sub optimal
                    }

                    var w = q.String(h, "welder", "");
                    _welder = _p.GridTerminalSystem.GetBlockWithName(w) as IMyShipWelder;
                    if (_welder == null)
                        return false;

                    _welder.Enabled = false;
                    _fireAngle = q.Float(h, "fireAngle", 60) * rad;
                    _RPM = q.Float(h, "rpm", 5);

                    _p.GridTerminalSystem.GetBlocksOfType<IMyProjector>(null, b =>
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
            {
                _tgtAngle = _fireAngle;
            }
            AddReport("RACK INIT");
            return _welder != null && _proj != null && _bases != null;
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
                var e = _missiles[0];
                if (e.Controller != null)
                {
                    _missiles.RemoveAtFast(0);
                    id = e.Controller.EntityId;
                    e.Launch();
  
                    AddReport($"FIRED MSL");
                    if (_missiles.Count == 0)
                        Status = LauncherState.Empty;
                    return true;
                }
            }
            return false;
        }


        public int Update()
        {
            if (Status == LauncherState.Ready)
                return READY_T;

            var e = _bases[_loadPtr];
            switch (Status)
            {
                case LauncherState.Reload:
                    {
                        Missile m = null;
                        if (e.CollectMissileBlocks() && e.IsMissileReady(ref m))
                        {
                            _missiles.Add(m);
                            AddReport($"LOGON {_missiles.Count}/{Total}");
                            // if reload set is now empty, go to firing position
                            // otherwise go to next reload position
                            _loadPtr++;
                            _tgtAngle = _loadPtr >= _bases.Length ? _fireAngle : _bases[_loadPtr].Reload;
                            StartRotation();
                            return ACTIVE_T;
                        }
                        else return RELOAD_T;
                    }
                case LauncherState.Moving:
                    {
                        if (Math.Abs(_arm.Angle - _tgtAngle) < TOL)
                        {
                            _arm.TargetVelocityRPM = 0;
                            if (_loadPtr < _bases.Length)
                            {
                                e.Base.Enabled = true;
                                _welder.Enabled = true;
                                AddReport("AT TARGET");
                                Status = LauncherState.Reload;
                            }
                            else if (_tgtAngle == _fireAngle)
                            {
                                _loadPtr = 0;
                                _proj.Enabled = false;
                                AddReport("ALL READY");
                                Status = LauncherState.Ready;
                            }
                            return RELOAD_T;
                        }
                        return ACTIVE_T;
                    }
                default:
                case LauncherState.Empty:
                    {
                        foreach (var h in _bases)
                            h.Base.Enabled = false;

                        e.Base.Enabled = _proj.Enabled = true;
                        _tgtAngle = e.Reload;
                        AddReport("ALL EMPTY");
                        StartRotation();
                        return ACTIVE_T;
                    }
            }
        }

        void AddReport(string s)
        {
            var now = NextUpdateF;
            Report[Lib.Next(ref _msgPtr, Report.Length)] = $">{now:X4} " + s;
        }

        void StartRotation()
        {
            var adj = _arm.Angle;

            // if (adj < -Lib.HALF_PI)
            //     adj += MathHelper.Pi;
            // else if (adj > Lib.HALF_PI)
            //     adj -= MathHelper.Pi;

            var next = adj - _tgtAngle;

            if (next > 0)
            {
                _arm.LowerLimitRad = _tgtAngle;
                _arm.TargetVelocityRPM = -_RPM;
            }
            else if (next < 0)
            {
                _arm.UpperLimitRad = _tgtAngle;
                _arm.TargetVelocityRPM = _RPM;
            }

            _welder.Enabled = false;
            Status = LauncherState.Moving;
        }
    }
}