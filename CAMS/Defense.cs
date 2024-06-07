using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;

namespace IngameScript
{
    public class Defense : CompBase // turrets and interceptors
    {
        Dictionary<string, PDC> pdcTurrets = new Dictionary<string, PDC>();

        public Defense(string n) : base(n, Lib.u1 | Lib.u10 | Lib.u100)
        {
            // create turrets
        }

        #region implementations

        public override void Setup(Program prog)
        {
           
        }

        public override void Update(UpdateFrequency u)
        {
            
        }

        #endregion

    }
}