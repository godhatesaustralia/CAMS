using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    public class Defense : CompBase // turrets and interceptors
    {
        Dictionary<string, PDC> pdcTurrets = new Dictionary<string, PDC>();
        Dictionary<string, Turret> mainTurrets = new Dictionary<string, Turret>();
        Dictionary<string, TurretBase> allTurrets = new Dictionary<string, TurretBase>();
        string[] pdcNames, turNames;

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
                    string
                        grp = "RotorGroup",
                        def = " CAMS Azimuths",
                        pdg = q.String(Lib.HDR, "pd" + grp, "PD" + def),
                        mng = q.String(Lib.HDR, "main" + grp, "Main" + def);
                    m.Terminal.GetBlockGroupWithName(pdg).GetBlocks(null, b =>
                    {
                        var a = b as IMyMotorStator;
                        if (a != null)
                            r.Add(a);
                        return true;
                    });
                    foreach (var a in r)
                    {
                        var pd = new PDC(a, m);
                        if (pd != null)
                            allTurrets.Add(pd.Name, pd);
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
                        var tr = new Turret(a, m);
                        if (tr != null)
                            allTurrets.Add(tr.Name, tr);
                    }
                    //pdcNames = pdcTurrets.Keys.ToArray();
                    var l = new List<string>();
                    foreach (var s in allTurrets.Keys)
                        l.Add(s);
                    turNames = l.ToArray();
                    MySprite[] spr = {
                        new MySprite(Lib.TXT, "", new Vector2(20, 112), null, Lib.GRN, Lib.VB, 0, 0.925f),// 1. TUR NAME
                        new MySprite(Lib.TXT, "AZ\nEL", new Vector2(20, 160), null, Lib.GRN, Lib.VB, 0, 1.825f),// 2. ANGLE HDR
                        new MySprite(Lib.TXT, "", new Vector2(132, 164), null, Lib.GRN, Lib.VB, 0, 0.9125f),// 3. ANGLE DATA
                        new MySprite(Lib.TXT, "", new Vector2(20, 348), null, Lib.GRN, Lib.VB, 0, 0.925f)// 5. STATE
                            };
                    m.CtrlScreens.Add(Lib.TR, new Screen(() => allTurrets.Count, spr, (p, s) =>
                     {
                         var turret = allTurrets[turNames[p]];
                         string n = turret.Name, st = turret.aimState.ToString().ToUpper();
                         int ct = p >= 9 ? 12 : 13;
                         ct -= turret.Name.Length;
                         for (; ct-- > 0;)
                             n += " ";
                         s.SetData(n + $"{p + 1}/{allTurrets.Count}", 0);
                         s.SetData($"RPM {turret.aRPM:000}\nCUR {turret.aCur:000}°\nRPM {turret.eRPM:0000}\nCUR {turret.eCur:000}°", 2);
                         s.SetData(st, 3);
                     })
                );
                }

        }



        public override void Update(UpdateFrequency u)
        {
            // if (F % 2 != 0) return;
            if (Targets.Count != 0)
            {
                var tgts = Targets.AllTargets();
                foreach (var tur in allTurrets.Values)
                    tur.Update(ref tgts);
            }
            else
            {
                foreach (var tur in allTurrets.Values)
                    tur.Reset();
            }
        }

        #endregion

    }
}