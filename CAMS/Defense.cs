using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    public class Defense : CompBase // turrets and interceptors
    {
        ArmLauncherWHAM[] Launchers;
        Dictionary<long, long> ekvTrackedTgts = new Dictionary<long, long>();
        Dictionary<string, RotorTurret> Turrets = new Dictionary<string, RotorTurret>();
        RoundRobin<string, RotorTurret>
            AssignRR, UpdateRR;
        SortedSet<Target> PriorityTargets = new SortedSet<Target>();
        string[] TurretNames, PDTs;
        int Count => TurretNames.Length;
        #region settings
        bool priUpdateSwitch = true;
        int
            maxTurUpdates,
            maxKillTracks,
            priCheckTicks = 2;
        long nextPriCheck = 0;
        #endregion

        public Defense(string n) : base(n, Lib.u1 | Lib.u10 | Lib.u100)
        {
        }

        #region implementations

        public override void Setup(Program m)
        {
            var r = new List<IMyMotorStator>();
            var l = new List<ArmLauncherWHAM>();
            Main = m;
            using (var q = new iniWrap())
                if (q.CustomData(m.Me))
                {
                    maxTurUpdates = q.Int(Lib.HDR, "maxTurUpdates", 2);
                    maxKillTracks = q.Int(Lib.HDR, "maxKillTracks", 3);
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
                    foreach (var az in r)
                    {
                        var pd = new PDT(az, m, mx);
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
                        return false;
                    });
                    foreach (var az in r)
                    {
                        var tr = new RotorTurret(az, m);
                        if (tr != null)
                            Turrets.Add(tr.Name, tr);
                    }

                    r.Clear();
                    m.Terminal.GetBlockGroupWithName(icptg).GetBlocks(null, b =>
                    {
                        var a = b as IMyMotorStator;
                        if (a != null)
                            r.Add(a);
                        return false;
                    });
                    foreach (var arm in r)
                    {
                        var rk = new ArmLauncherWHAM(arm, Main);
                        if (rk.Init())
                            l.Add(rk);
                    }

                    Launchers = l.ToArray();

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
            #region (temp) launch arms update
            while (Datalink.MissileReady.HasPendingMessage)
            {
                var m = Datalink.MissileReady.AcceptMessage();
                if (m.Data is long)
                    foreach (var rk in Launchers)
                        if (rk.CheckHandshake((long)m.Data)) break;
            }

            foreach (var rk in Launchers)
                rk.Update();
            #endregion

            AssignTargets();

            Intercept();

            AssignPDTTracking();

            for (int i = 0; i < maxTurUpdates; i++)
                UpdateRR.Next(ref Turrets).UpdateTurret();
        }
        #endregion

        void Intercept()
        {
            if (Main.Targets.Count == 0)
                return;

            Target t;
            for (int i = 0; i < maxKillTracks && i < PriorityTargets.Count; i++)
            {
                long ekv;
                t = PriorityTargets.Min;
                foreach (var rk in Launchers)
                    if (t.PriorityKill && !ekvTrackedTgts.ContainsKey(t.EID))
                    {
                        if (rk.Fire(out ekv))
                            ekvTrackedTgts.Add(t.EID, ekv);
                    }
                PriorityTargets.Remove(t);
            }

            foreach (var tgt in ekvTrackedTgts.Keys)
            {
                t = Main.Targets.Get(tgt);
                if (!PriorityTargets.Contains(t))
                    PriorityTargets.Add(t);
            }
        }

        void AssignPDTTracking()
        {
            if (ekvTrackedTgts.Count == 0)
            return;

            PDT tur;
            foreach (var id in ekvTrackedTgts.Keys)
                foreach (var n in PDTs)
                {
                    tur = (PDT)Turrets[n];
                    if (tur.tEID == -1 && !tur.Inoperable)
                        tur.AssignLidarTarget(id);
                }
        }

        void AssignTargets()
        {
            if (Main.Targets.Count > 0)
            {
                if (priUpdateSwitch && Main.F >= nextPriCheck)
                {
                    PriorityTargets.Clear();
                    foreach (var t in Main.Targets.AllTargets())
                    {
                        PriorityTargets.Add(t);
                    }
                    nextPriCheck = Main.F + priCheckTicks;
                    priUpdateSwitch = false;
                }
                else if (!priUpdateSwitch)
                {
                    RotorTurret tur;
                    Target temp = null;
                    priUpdateSwitch = AssignRR.Next(ref Turrets, out tur);
                    if (tur.tEID != -1 && tur.CanTarget(tur.tEID))
                        return;
                    int p = -1;
                    foreach (var tgt in PriorityTargets)
                    {
                        int type = (int)tgt.Type;
                        if (tur.CanTarget(tgt.EID))
                        {
                            temp = tgt;
                            p = tgt.Priority;
                            if (tur.IsPDT && type == 2)
                                break;
                            else if (!tur.IsPDT && type == 3)
                                break;
                        }
                    }

                    // if (PriorityTargets.Remove(temp))
                    // {
                    //     tur.tEID = temp.EID;
                    //     temp.Priority += 1000;
                    //     PriorityTargets.Add(temp);
                    // }
                    return;

                }
            }

        }
    }
}