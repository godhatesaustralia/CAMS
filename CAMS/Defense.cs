using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;

namespace IngameScript
{
    public class Defense : CompBase // turrets and interceptors
    {
        Dictionary<string, PDC> pdcTurrets = new Dictionary<string, PDC>();

        public Defense(string n) : base(n, Lib.u1 | Lib.u10 | Lib.u100)
        {

        }

        #region implementations

        public override void Setup(Program p)
        {
            var r = new List<IMyMotorStator>();
            Main = p;
            using (var q = new iniWrap())
                if (q.CustomData(p.Me))
                {
                    string
                        s = "RotorGroup",
                        def = " CAMS Azimuths",
                        pdg = q.String(Lib.HDR, "pd" + s, "PD" + def),
                        mng = q.String(Lib.HDR, "main" + s, "Main" + def);
                    p.Terminal.GetBlockGroupWithName(pdg).GetBlocks(null, b =>
                    {
                        var a = b as IMyMotorStator;
                        if (a != null)
                            r.Add(a);
                        return true;
                    });
                    foreach (var a in r)
                    {
                        var pd = new PDC(a, p);
                        if (pd != null)
                            pdcTurrets.Add(pd.Name, pd);
                    }
                    r.Clear();
                    p.Terminal.GetBlockGroupWithName(mng).GetBlocks(null, b =>
                    {
                        var a = b as IMyMotorStator;
                        if (a != null)
                            r.Add(a);
                        return true;
                    });
                    //foreach (var a in r)
                    //{
                    //    var pd = new PDC(a, p);
                    //    if (pd != null)
                    //        pdcTurrets.Add(pd.Name, pd);
                    //}
                }
        }



        public override void Update(UpdateFrequency u)
        {

        }

        #endregion

    }
}