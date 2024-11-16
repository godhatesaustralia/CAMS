using Microsoft.Build.Framework;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage;
using VRageMath;

namespace IngameScript
{
    public enum RackState
    {
        /// <summary>
        /// Launcher has lost full functionality
        /// </summary>
        Inoperable,
        /// <summary>
        /// All missiles depleted
        /// </summary>
        Empty,
        /// <summary>
        /// Launcher in process of rebuilding a missile
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

    public class Launcher
    {
        public readonly string Name;
        public readonly string[] Report = new string[] { "", "" };
        public bool Auto;
        protected int _msgPtr = 0, _loadPtr = 0, _firePtr;
        public int Total = 0;
        public long NextUpdateF = 0;
        public RackState Status = 0;

        protected const int
            ACTIVE_T = 3,
            READY_T = 23,
            RELOAD_T = 7;

        protected IMyShipWelder _weld;
        protected IMyProjector _proj;
        protected Hardpoint[] _bases;
        protected Missile[] _msls;
        protected Program _p;

        public Launcher(string n, Program p)
        {
            Name = n;
            _p = p;
        }

        protected bool Init(ref iniWrap q, out string[] tags)
        {
            var t = q.String(Name, "tags", "");

            if (t != "")
            {
                tags = t.Split('\n');
                _bases = new Hardpoint[tags.Length];
                _msls = new Missile[tags.Length];

                Auto = q.Bool(Name, "defense");

                _weld = _p.Terminal.GetBlockWithName(q.String(Name, "welder")) as IMyShipWelder;
                _proj = _p.Terminal.GetBlockWithName(q.String(Name, "projector")) as IMyProjector;

                if (_proj == null || _weld == null) return false;
                _proj.Enabled = _weld.Enabled = false;
                return true;
            }

            tags = null;
            return false;
        }

        public virtual bool Setup(ref iniWrap q)
        {
            int c = 0;
            bool ok = true;
            string[] tags;

            if (Init(ref q, out tags))
                for (; Total < tags.Length && ok;)
                {
                    tags[Total].Trim('|');

                    var merge = (IMyShipMergeBlock)_p.Terminal.GetBlockWithName(q.String(Name, "merge" + tags[Total]));
                    if (merge != null)
                    {
                        var hpt = new Hardpoint(tags[Total], 0);
                        ok &= hpt.Init(merge, ref _msls[Total]);
                        if (ok)
                        {
                            _bases[Total] = hpt;
                            if (merge.IsConnected) c++;
                            Total++;
                        }
                    }
                    else ok = false;
                }
            else ok = false;
            //rdy = Total == c;

            AddReport("#RCK INIT");

            // while (rdy && _loadPtr < _bases.Length)
            // {
            //     var b = _bases[_loadPtr];
            //     rdy &= b.CollectMissileBlocks() && b.IsMissileReady(ref _msls[_loadPtr]);
            //     if (rdy) _loadPtr++;
            // }
            Status = c == 0 ? RackState.Empty : RackState.Reload;

            return ok;
        }

        public virtual int Update()
        {
            if (Status == RackState.Ready)
                return READY_T;

            var m = _bases[_loadPtr];
            if (Status == RackState.Reload)
            {
                _proj.Enabled = true;
                if (m.CollectMissileBlocks() && m.IsMissileReady(ref _msls[_loadPtr]))
                {
                    _loadPtr++;

                    // if reload set is now empty, go to firing position
                    // otherwise go to next reload position

                    AddReport($"READY {_loadPtr}/{Total}");
                    if (_loadPtr < _bases.Length)
                    {
                        _bases[_loadPtr].Base.Enabled = _weld.Enabled = _proj.Enabled = true;
                        AddReport("RELOADING");
                        Status = RackState.Reload;
                    }
                    else
                    {
                        _loadPtr = 0;
                        _firePtr = _msls.Length - 1;
                        _proj.Enabled = _weld.Enabled = false;
                        AddReport("ALL READY");
                        Status = RackState.Ready;
                    }
                    return READY_T;
                }
                else if (m.Base.Closed || _weld.Closed || _proj.Closed)
                    Status = RackState.Inoperable;
                else return RELOAD_T;
            }
            else if (Status == RackState.Empty)
            {
                foreach (var h in _bases)
                    h.Base.Enabled = false;

                m.Base.Enabled = _weld.Enabled = _proj.Enabled = true;
                Status = RackState.Reload;
                AddReport("ALL EMPTY");
                return ACTIVE_T;
            }
            return 0;
        }

        /// <summary>
        /// Attempts to fire a missile. If none remain after firing, the launcher enters its reload sequence.
        /// </summary>
        /// <param name="teid">Entity id of target</param>
        /// <param name="dict"=>Reference to system missiles database</param>
        /// <param name="force"=>Whether to force a launch during sequenced reload (dangerous!)</param>
        /// <returns>Whether a missile was fired successfully.</returns>
        public bool Fire(long teid, ref Dictionary<long, Missile> dict, bool force = false)
        {
            if (Status == RackState.Ready || (Status == RackState.Reload && force))
            {
                Missile m = null;
                if (force)
                {
                    _firePtr = 0;
                    while (m.Controller == null && _firePtr < _msls.Length)
                    {
                        m = _msls[_firePtr];
                        _firePtr++;
                        if (_firePtr >= _msls.Length)
                            return false;
                    }

                    _proj.Enabled = _weld.Enabled = false;
                }
                else m = _msls[_firePtr];

                if (dict.Count < _p.HardpointsCount * 2 && m.Controller != null)
                {
                    dict.Add(m.MEID, m);
                    dict[m.MEID].Launch(teid, _p);
                    AddReport($"FIRE {m.IDTG}");

                    //_msls[_firePtr].Clear();
                    _firePtr--;

                    if (_firePtr < 0)
                        Status = RackState.Empty;
                    return true;
                }
            }
            return false;
        }

        protected void AddReport(string s)
        {
            var now = NextUpdateF;
            Report[Lib.Next(ref _msgPtr, Report.Length)] = $">{now:X4} " + s;
        }
    }

    public class ArmLauncher : Launcher
    {
        /// <summary>
        /// Launcher rotation hinge
        /// </summary>
        IMyMotorStator _arm;
        bool _quickstart = true;
        float _fireAngle, _tgtAngle, _RPM;
        const float TOL = 0.01f;


        public ArmLauncher(string n, Program p, IMyMotorStator a) : base(n, p)
        {
            _arm = a;
        }

        public override bool Setup(ref iniWrap q)
        {
            int c = 0;
            bool ok = true;
            string[] tags;
            var rad = (float)(Lib.PI / 180);

            if (Init(ref q, out tags))
            {
                var temp = new SortedSet<Hardpoint>();
                for (; Total < tags.Length && ok;)
                {
                    tags[Total].Trim('|');
                    var angle = q.Float(Name, "weldAngle" + tags[Total], float.MinValue);
                    if (angle != float.MinValue)
                        angle *= rad;

                    var merge = (IMyShipMergeBlock)_p.GridTerminalSystem.GetBlockWithName(q.String(Name, "merge" + tags[Total]));
                    if (merge != null && angle != float.MinValue)
                    {
                        var hpt = new Hardpoint(tags[Total], angle);
                        ok &= hpt.Init(merge, ref _msls[Total]);
                        if (ok)
                        {
                            temp.Add(hpt);
                            if (merge.IsConnected) c++;
                            Total++;
                        }
                    }
                    else ok = false;
                }
                _bases = temp.ToArray(); // sub optimal
            }
            else ok = false;

            _fireAngle = q.Float(Name, "fireAngle", 60) * rad;
            _RPM = q.Float(Name, "rpm", 5);
            _quickstart = Total == c;

            AddReport("#ARM INIT");

            // while (rdy && _loadPtr < _bases.Length)
            // {
            //     var b = _bases[_loadPtr];
            //     rdy &= b.CollectMissileBlocks() && b.IsMissileReady(ref _msls[_loadPtr]);
            //     if (rdy) _loadPtr++;
            // }
            
            if (_quickstart)
            {
                _tgtAngle = _fireAngle;
                StartRotation();
            }
            
            Status = _quickstart ? RackState.Reload : RackState.Empty;
            return ok;
        }

        public override int Update()
        {
            if (Status == RackState.Ready)
                return READY_T;

            var e = _bases[_loadPtr];
            switch (Status)
            {
                case RackState.Reload:
                    {
                        if (e.CollectMissileBlocks() && e.IsMissileReady(ref _msls[_loadPtr]))
                        {
                            _loadPtr++;

                            // if reload set is now empty, go to firing position
                            // otherwise go to next reload position

                            AddReport($"READY {_loadPtr}/{Total}");

                            if (_quickstart)
                            {
                                _quickstart = _loadPtr < _bases.Length;
                                if (!_quickstart)
                                {
                                    _loadPtr = 0;
                                    _firePtr = _msls.Length - 1;
                                    _proj.Enabled = false;
                                    AddReport("ALL READY");
                                    Status = RackState.Ready;
                                }
                                return ACTIVE_T;
                            }

                            _tgtAngle = _loadPtr >= _bases.Length ? _fireAngle : _bases[_loadPtr].Reload;
                            StartRotation();
                            return ACTIVE_T;
                        }
                        else if (e.Base.Closed || _weld.Closed || _proj.Closed)
                        {
                            Status = RackState.Inoperable;
                            return 0;
                        }
                        else return RELOAD_T;
                    }
                case RackState.Moving:
                    {
                        if (Math.Abs(_arm.Angle - _tgtAngle) < TOL)
                        {
                            _arm.TargetVelocityRPM = 0;
                            if (_loadPtr < _bases.Length)
                            {
                                e.Base.Enabled = _weld.Enabled = true;
                                AddReport("AT TARGET");
                                Status = RackState.Reload;
                            }
                            else if (_tgtAngle == _fireAngle)
                            {
                                _loadPtr = 0;
                                _firePtr = _msls.Length - 1;
                                _proj.Enabled = false;
                                AddReport("ALL READY");
                                Status = RackState.Ready;
                            }
                            return RELOAD_T;
                        }
                        return ACTIVE_T;
                    }
                default:
                case RackState.Empty:
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

        void StartRotation()
        {
            var adj = _arm.Angle;
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

            _weld.Enabled = false;
            Status = RackState.Moving;
        }
    }
}