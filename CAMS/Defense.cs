using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{

    public class Defense : CompBase // turrets and interceptors
    {
        Dictionary<string, RotorTurret> Turrets = new Dictionary<string, RotorTurret>();
        Dictionary<string, EKVLauncher> Launchers = new Dictionary<string, EKVLauncher>();
        Dictionary<string, long> _mslToTargetID = new Dictionary<string, long>();
        RoundRobin<string, RotorTurret>
            AssignRR, UpdateRR;
        RoundRobin<string, EKVLauncher> LauncherRR;
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
        IMyBroadcastListener _mslSplash;
        #endregion

        public Defense(string n) : base(n, Lib.u1 | Lib.u10 | Lib.u100)
        {
            _mslSplash = Datalink.IGC.RegisterBroadcastListener(Datalink.IgcSplash);
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
                        mng = q.String(h, "main" + grp, "Main" + def),
                        icptg = q.String(h, "ekv" + grp, "EKV" + def);
                    m.Terminal.GetBlockGroupWithName(pdg).GetBlocks(null, b =>
                    {
                        var a = b as IMyMotorStator;
                        if (a != null)
                            r.Add(a);
                        return false;
                    });
                    var pdn = new List<string>();
                    int mx = q.Int(h, "maxScansPDLR", 3);
                    foreach (var a in r)
                    {
                        var pd = new PDT(a, m, this, mx);
                        if (pd != null)
                        {
                            Turrets[pd.Name] = pd;
                            pdn.Add(pd.Name);
                        }
                    }
                    r.Clear();
                    m.Terminal.GetBlockGroupWithName(mng).GetBlocks(null, b =>
                    {
                        var a = b as IMyMotorStator;
                        if (a != null)
                            r.Add(a);
                        return false;
                    });
                    foreach (var a in r)
                    {
                        var tr = new RotorTurret(a, m);
                        if (tr != null)
                            Turrets[tr.Name] = tr;
                    }
                    r.Clear() ;
                    m.Terminal.GetBlockGroupWithName(icptg).GetBlocks(null, b =>
                    {
                        var a = b as IMyMotorStator;
                        if (a != null)
                            r.Add(a);
                        return false;
                    });
                    foreach (var a in r)
                    {
                        var l = new EKVLauncher(a, m);
                        if (l != null)
                            Launchers[a.CustomName] = l;
                    }
                    TurretNames = Turrets.Keys.ToArray();
                    PDTs = pdn.ToArray();

                    AssignRR = new RoundRobin<string, RotorTurret>(TurretNames);
                    UpdateRR = new RoundRobin<string, RotorTurret>(TurretNames);
                    LauncherRR = new RoundRobin<string, EKVLauncher>(ref Launchers);

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
            while (_mslSplash.HasPendingMessage)
            {
                var name = (string)_mslSplash.AcceptMessage().Data;
                _mslToTargetID.Remove(name);
            }

            AssignTargets();
            for (int i = 0; i < maxTurUpdates; i++)
            {
                var tur = UpdateRR.Next(ref Turrets);
                tur.UpdateTurret();
            }

            var l = LauncherRR.Next(ref Launchers);
            if (l.NeedsReload)
                l.Reload();

            if (_mslToTargetID.Count > 0)
            {

            }    

                    
        }

        #endregion

        void AssignTargets()
        {
            if (Targets.Count > 0)
            {
                if (priUpdateSwitch && F >= nextPriorityCheck)
                {
                    _tEIDsByPriority.Clear();
                    maxPriTgt = -1;
                    foreach (var t in Targets.AllTargets())
                    {
                        _tEIDsByPriority[t.Priority] = t.EID;
                        maxPriTgt = t.Priority > maxPriTgt ? t.Priority : maxPriTgt;
                    }
                    nextPriorityCheck = F + priCheckTicks;
                    // TODO: better
                    //if ((int)Targets.Get(_tEIDsByPriority[maxPriTgt]).Type == 2)
                    //    foreach (var l in Launchers.Values)
                    //        if (l.Count > 0 && !l.NeedsReload)
                    //            l.Launch();
                    priUpdateSwitch = false;
                }
                else if (!priUpdateSwitch)
                {
                    RotorTurret tur;
                    priUpdateSwitch = AssignRR.Next(ref Turrets, out tur);
                    if (tur.CanTarget(tur.tEID))
                        return;

                    int p = -1;
                    long
                        tempEID = p, // selEID
                        bestEID = p; // bestEID 
                    foreach (var kvp in _tEIDsByPriority)
                    {
                        int type = (int)Targets.Get(kvp.Value)?.Type;
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
                    if (tempEID != -1)
                    {
                        tur.tEID = tempEID;
                        _tEIDsByPriority.Remove(p);
                        _tEIDsByPriority.Add(p + 1000, tempEID);
                    }
                    return;

                }
            }

        }
    }
}
