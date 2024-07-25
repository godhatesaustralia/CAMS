using Sandbox.Game.Weapons;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http.Headers;
using System.Runtime.Remoting.Messaging;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    public class ArmLauncherWHAM
    {
        enum LauncherState
        {
            Default = 0,
            Boot = 1,
            Reload = 2,
            Ready = 3,
            Empty = 4
        }
        IMyMotorStator _arm;
        IMyShipWelder[] _welders;
        IMyProjector _proj;
        Dictionary<string, EKV> eKVDict = new Dictionary<string, EKV>();
        SortedSet<EKV> eKVsByAngle = new SortedSet<EKV>();
        float _FireAngle, _RPM;
        bool _startup = true;
        int Count = 0;
        IMyGridTerminalSystem _gts;


        public ArmLauncherWHAM(IMyMotorStator a, Program p)
        {
            _arm = a;
            _gts = p.GridTerminalSystem;
        }

        public bool Init()
        {
            if (_arm == null) return false;
            using (var q = new iniWrap())
                if (!q.CustomData(_arm)) return false;
                else
                {
                    var h = Lib.HDR;
                    var t = q.String(h, "tags", "");
                    var rad = (float)(Lib.Pi / 180);
                    if (t != "")
                    {
                        var tags = t.Split('\n');
                        if (tags != null)
                            for (int i = 0; i < tags.Length; i++)
                            {
                                tags[i].Trim('|');
                                var angle = q.Float(h, "weldAngle" + tags[i], float.MinValue);
                                if (angle != float.MinValue)
                                    angle *= rad;
                                var merge = (IMyShipMergeBlock)_gts.GetBlockWithName(q.String(h, "merge" + tags[i]));
                                if (merge != null && angle != float.MinValue)
                                {
                                    var n = q.String(h, "computer" + tags[i], tags[i] + " Computer WHAM");
                                    var cptr = (IMyProgrammableBlock)_gts.GetBlockWithName(n);
                                    
                                    eKVDict.Add(tags[i], new EKV(tags[i], n, cptr, merge, angle));
                                    if (cptr != null) Count++;
                                }
                            }
                    }

                    var w = q.String(h, "welders");
                    var weldnames = w.Contains(',') ? w.Split(',') : new string[] { w };
                    if (weldnames != null)
                    {
                        _welders = new IMyShipWelder[weldnames.Length];
                        for (int i = 0; i < _welders.Length; i++)
                        {
                            _welders[i] = (IMyShipWelder)_gts.GetBlockWithName(weldnames[i]);
                            if (_welders[i] != null) _welders[i].Enabled = false;
                        }
                    }
                    _FireAngle = q.Float(h, "fireAngle", 60) * rad;
                    _RPM = q.Float(h, "rpm", 5);
                    foreach (var val in eKVDict.Values)
                        eKVsByAngle.Add(val);
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
            return _welders != null && eKVDict.Count > 0;
        }

        public bool Boot()
        {
            if (Count == eKVDict.Count)
            {
                var m = eKVsByAngle.Min;
                if (!m.Computer?.Enabled ?? false && !m.Computer.IsRunning)
                {
                    m.Computer.Enabled = true;
                    m.Computer.TryRun("setup");
                }
                else m.Computer = (IMyProgrammableBlock)_gts.GetBlockWithName(m.ComputerName);
            }
            return _startup;
        }

        public bool ValidHandshake(string n)
        {
            var e = eKVsByAngle.Min;
            if (e.Name == n)
                eKVsByAngle.Remove(e);
            return e.Name == n;
        }
        class EKV : IComparable<EKV>
        {
            public bool Active = false, Ready = false;
            public string Name, ComputerName;
            public IMyShipMergeBlock Hardpoint;
            public IMyProgrammableBlock Computer;
            public float Reload;

            public EKV(string n, string cn, IMyProgrammableBlock c, IMyShipMergeBlock m, float r)
            {
                Name = n;
                ComputerName = cn;
                Computer = c;
                Hardpoint = m;
                Reload = r;
            }

            public int CompareTo(EKV o)
            {
                if (Reload == o.Reload)
                    return Computer == null ? -1 : Name.CompareTo(o.Name);
                else return Reload < o.Reload ? -1 : 1;
            }
        }
    }
    public class Defense : CompBase // turrets and interceptors
    {
        Dictionary<string, RotorTurret> Turrets = new Dictionary<string, RotorTurret>();
        RoundRobin<string, RotorTurret>
            AssignRR, UpdateRR;
        SortedDictionary<int, long> _tEIDsByPriority = new SortedDictionary<int, long>();
        string[] TurretNames, PDTs;
        int Count => TurretNames.Length;
        int maxPriTgt;
        #region clock
        bool priUpdateSwitch = true;
        int
            maxTurUpdates,
            priCheckTicks = 2;
        long nextPriorityCheck = 0;
        #endregion

        public Defense(string n) : base(n, Lib.u1 | Lib.u10 | Lib.u100)
        {
        }

        #region implementations

        public override void Setup(Program m)
        {
            var r = new List<IMyMotorStator>();
            Main = m;
            using (var q = new iniWrap())
                if (q.CustomData(m.Me))
                {
                    maxTurUpdates = q.Int(Lib.HDR, "maxTurUpdates", 2);
                    string
                        h = Lib.HDR,
                        grp = "RotorGroup",
                        def = " CAMS Azimuths",
                        pdg = q.String(h, "pd" + grp, "PD" + def),
                        mng = q.String(h, "main" + grp, "Main" + def);
                    m.Terminal.GetBlockGroupWithName(pdg).GetBlocks(null, b =>
                    {
                        var a = b as IMyMotorStator;
                        if (a != null)
                            r.Add(a);
                        return true;
                    });
                    var pdn = new List<string>();
                    int mx = q.Int(h, "maxScansPDLR", 3);
                    foreach (var a in r)
                    {
                        var pd = new PDT(a, m, mx);
                        if (pd != null)
                        {
                            Turrets.Add(pd.Name, pd);
                            pdn.Add(pd.Name);
                        }
                    }
                    r.Clear();
                    m.Terminal.GetBlockGroupWithName(mng).GetBlocks(null, b =>
                    {
                        var a = b as IMyMotorStator;
                        if (a != null)
                            r.Add(a);
                        return true;
                    });
                    foreach (var a in r)
                    {
                        var tr = new RotorTurret(a, m);
                        if (tr != null)
                            Turrets.Add(tr.Name, tr);
                    }

                    TurretNames = Turrets.Keys.ToArray();
                    PDTs = pdn.ToArray();

                    AssignRR = new RoundRobin<string, RotorTurret>(TurretNames);
                    UpdateRR = new RoundRobin<string, RotorTurret>(TurretNames);

                    #region list-screen
                    MySprite[] spr = {
                        new MySprite(Lib.TXT, "", new Vector2(20, 112), null, Lib.GRN, Lib.VB, 0, 0.925f),// 1. TUR NAME
                        new MySprite(Lib.TXT, "AZ\nEL", new Vector2(20, 160), null, Lib.GRN, Lib.VB, 0, 1.825f),// 2. ANGLE HDR
                        new MySprite(Lib.TXT, "", new Vector2(132, 164), null, Lib.GRN, Lib.VB, 0, 0.9125f),// 3. ANGLE DATA
                        new MySprite(Lib.TXT, "", new Vector2(20, 348), null, Lib.GRN, Lib.VB, 0, 0.925f)// 5. STATE
                            };
                    m.CtrlScreens.Add(Lib.TR, new Screen(() => TurretNames.Length, spr, (p, s) =>
                    {
                        var turret = Turrets[TurretNames[p]];
                        string n = turret.Name, st = turret.Status.ToString().ToUpper();
                        int ct = p >= 9 ? 12 : 13;
                        ct -= turret.Name.Length;
                        for (; ct-- > 0;)
                            n += " ";
                        s.SetData(n + $"{p + 1}/{Count}", 0);
                        s.SetData($"RPM {turret.aRPM:00.0}\nCUR {turret.aCur:000}°\nRPM {turret.eRPM:00.0}\nCUR {turret.eCur:000}°", 2);
                        s.SetData(st, 3);
                    }));
                    #endregion

                }

        }

        public override void Update(UpdateFrequency u)
        {
            AssignTargets();
            for (int i = 0; i < maxTurUpdates; i++)
            {
                var tur = UpdateRR.Next(ref Turrets);
                tur.UpdateTurret();
            }
        }

        #endregion

        void AssignTargets()
        {
            if (Main.Targets.Count > 0)
            {
                if (priUpdateSwitch && Main.F >= nextPriorityCheck)
                {
                    _tEIDsByPriority.Clear();
                    maxPriTgt = -1;
                    foreach (var t in Main.Targets.AllTargets())
                    {
                        _tEIDsByPriority[t.Priority] = t.EID;
                        maxPriTgt = t.Priority > maxPriTgt ? t.Priority : maxPriTgt;
                    }
                    nextPriorityCheck = Main.F + priCheckTicks;
                    priUpdateSwitch = false;
                }
                else if (!priUpdateSwitch)
                {
                    RotorTurret tur;
                    priUpdateSwitch = AssignRR.Next(ref Turrets, out tur);
                    if (tur.tEID != -1 && tur.CanTarget(tur.tEID))
                        return;

                    int p = -1;
                    long
                        tempEID = p, // selEID
                        bestEID = p; // bestEID 
                    foreach (var kvp in _tEIDsByPriority)
                    {
                        int type = (int)Main.Targets.Get(kvp.Value)?.Type;
                        if (tur.CanTarget(kvp.Value))
                        {
                            tempEID = kvp.Value;
                            p = kvp.Key;
                            if (tur.IsPDT && type == 2)
                                bestEID = kvp.Value;
                            else if (!tur.IsPDT && type == 3)
                                bestEID = kvp.Value;
                            if (bestEID != -1)
                                break;
                        }
                    }
                    tempEID = bestEID == -1 ? tempEID : bestEID;
                    if (tempEID != -1 && _tEIDsByPriority.Remove(p))
                    {
                        tur.tEID = tempEID;
                        _tEIDsByPriority.Add(p + 1000, tempEID);
                    }
                    return;

                }
            }

        }
    }
}