using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;

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
        public Queue<string> Time = new Queue<string>(MSG_CT), Log = new Queue<string>(MSG_CT);
        public bool Auto;
        protected int _load = 0, _fire;
        public int Total = 0;
        public long NextUpdateF = 0;
        public RackState Status = 0;

        protected const int
            ACTIVE_T = 3,
            READY_T = 23,
            RELOAD_T = 7,
            MSG_CT = 4;

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

        public string GetID(int p) => _msls[p].IDTG;

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
                            else merge.Enabled = false;
                            Total++;
                        }
                    }
                    else ok = false;
                }
            else ok = false;

            AddReport("#RCK INIT");

            if (c != 0)
            {
                _proj.Enabled = _weld.Enabled = _bases[0].Base.Enabled = true;
                Status = RackState.Reload;
            }
            else Status = RackState.Empty;
            return ok;
        }

        public virtual int Update()
        {
            if (Status == RackState.Ready)
                return READY_T;

            var m = _bases[_load];
            if (Status == RackState.Reload)
            {
                //proj.Enabled = true;
                if (m.CollectMissileBlocks() && m.IsMissileReady(ref _msls[_load]))
                {
                    _load++;

                    // if reload set is now empty, go to firing position
                    // otherwise go to next reload position

                    AddReport($"READY {_load}/{Total}");
                    if (_load < Total)
                    {
                        _bases[_load].Base.Enabled = _weld.Enabled = true;
                        AddReport("RELOADING");
                        Status = RackState.Reload;
                    }
                    else
                    {
                        _load = _fire = 0;
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
        public bool Fire(long teid, ref Dictionary<long, Missile> dict)
        {
            if (Status != RackState.Ready)
                return false;

            var m = _msls[_fire];
            if (dict.Count < _p.HardpointsCount * 2 && m.Controller != null)
            {
                try{ dict.Add(m.MEID, m); }
                catch{ string s = ""; foreach (var kvp in dict) s += $"\n{kvp.Value.IDTG}"; throw new Exception(s); }
                dict[m.MEID].Launch(teid, _p);
                AddReport($"FIRE {m.IDTG}");

                _msls[_fire] = _p.RecycledMissile;
                _fire++;

                if (_fire >= Total)
                {
                    Status = RackState.Empty;
                    NextUpdateF += ACTIVE_T;
                }
                return true;
            }

            return false;
        }

        protected void AddReport(string s)
        {
            var now = NextUpdateF;
            if (Log.Count >= MSG_CT)
            {
                Time.Dequeue();
                Log.Dequeue();
            }
            Time.Enqueue($"\n>{now:X4}");
            Log.Enqueue($"\n{s}");
        }
    }

    public class ArmLauncher : Launcher
    {
        /// <summary>
        /// Launcher rotation hinge
        /// </summary>
        IMyMotorStator _arm;
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
                    var angle = q.Float(Name, "weldAngle" + tags[Total], -361);
                    angle *= rad;

                    var merge = (IMyShipMergeBlock)_p.GridTerminalSystem.GetBlockWithName(q.String(Name, "merge" + tags[Total]));
                    if (merge != null)
                    {
                        var hpt = new Hardpoint(tags[Total], angle);
                        ok &= angle != -361 * rad && hpt.Init(merge, ref _msls[Total]);
                        if (ok)
                        {
                            temp.Add(hpt);
                            if (merge.IsConnected) c++;
                            Total++;
                        }
                    }
                    else ok = false;
                }
                _bases = temp.ToArray();
            }
            else ok = false;

            _fireAngle = q.Float(Name, "fireAngle", 60) * rad;
            _RPM = q.Float(Name, "rpm", 5);
            bool rdy = Total == c, load = c != 0;
            
            while (load && _load < Total)
            {
                var b = _bases[_load];
                load &= b.CollectMissileBlocks() && b.IsMissileReady(ref _msls[_load]);
                if (load) { AddReport($"#BOOT {_load + 1}/{Total}"); _load++; }
            }

            if (load)
            {
                AddReport("#QK START");              
                _tgtAngle = _fireAngle;
                StartRotation();
            }
            else
            {
                AddReport("#ARM INIT");
                _tgtAngle = _bases[_load].Reload;
                _bases[_load].Base.Enabled = _proj.Enabled = true;
                StartRotation();
            }

            return ok;
        }

        public override int Update()
        {
            if (Status == RackState.Ready)
                return READY_T;
            
            switch (Status)
            {
                case RackState.Reload:
                    {
                        var e = _bases[_load];
                        if (e.CollectMissileBlocks() && e.IsMissileReady(ref _msls[_load]))
                        {
                            _load++;

                            // if reload set is now empty, go to firing position
                            // otherwise go to next reload position

                            AddReport($"READY {_load}/{Total}");

                            _tgtAngle = _load >= Total ? _fireAngle : _bases[_load].Reload;
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
                            if (_tgtAngle == _fireAngle)
                            {
                                _load = _fire = 0;
                                _proj.Enabled = false;
                                AddReport("ALL READY");
                                Status = RackState.Ready;
                            }
                            else if (_load < Total)
                            {
                                _bases[_load].Base.Enabled = _weld.Enabled = true;
                                AddReport("AT TARGET");
                                Status = RackState.Reload;
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

                        _bases[_load].Base.Enabled = _proj.Enabled = true;
                        _tgtAngle = _bases[_load].Reload;
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
            AddReport($"MOVE {_tgtAngle * 180 / Lib.PI:000}°");
            Status = RackState.Moving;
        }
    }
}