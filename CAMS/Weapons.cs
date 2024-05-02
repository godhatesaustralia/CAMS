using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    // vanilla, who cares
    public class TurretWeapons
    {
        List<IMyUserControllableGun> _fireGroup = new List<IMyUserControllableGun>();
        public int
               salvoTicks = 0, // 0 or lower means no salvoing
               salvoTickCounter = 0,
               offsetTicks = 0,
               offsetTimer = 0;
        int ptr = -1;
        public Vector3D AimReference
        {
            get
            {
                Vector3D r = Vector3D.Zero;
                if (_fireGroup.Count == 0) return r;
                foreach (var g in _fireGroup)
                    r += g.WorldMatrix.Translation;
                r /= _fireGroup.Count;
                return r;
            }
        }

        public TurretWeapons(int s, List<IMyUserControllableGun> g)
        {
            salvoTicks = s;
            _fireGroup = g;
        }

        public void OpenFire()
        {
            offsetTicks = 20;
            ++offsetTimer;
        }
        public void HoldFire() => offsetTicks = -1;

        public bool Active
        {
            get
            {
                if (_fireGroup.Count == 0) return false;
                var anyWeaponOn = false;

                for (int i = 0; i < _fireGroup.Count; i++)
                {
                    if (_fireGroup[i].Enabled)
                    {
                        anyWeaponOn = true;
                        break;
                    }
                }

                if (!anyWeaponOn) return false;

                return true;
            }
        }
        public void Update(int ticks = 1)
        {
            salvoTickCounter -= ticks;
            offsetTicks -= ticks;

            if (_fireGroup.Count == 0) return;
            while (_fireGroup[0].Closed)
            {
                _fireGroup.RemoveAtFast(0);
                if (_fireGroup.Count == 0) return;
            }

            //if (offsetTicks > 0)
            //{
            //    if (salvoTicks <= 0)
            //    {
            //        for (int i = 0; i < _fireGroup.Count; i++)
            //            _fireGroup[i]
            //    }
            //    else
            //    {
            //        if (salvoTickCounter < 0)
            //        {
            //            var gun = _fireGroup[Lib.Next(ref ptr, _fireGroup.Count)];
            //            Lib.SetValue(gun, "Shoot", true);
            //            salvoTickCounter = salvoTicks;
            //        }
            //    }
            //}
            //else
            //{
            //    for (int i = 0; i < _fireGroup.Count; i++)
            //        _fireGroup.ForEach(gun => { Lib.SetValue(gun, "Shoot", false); });
            //    }
            //}
        }
    }
}