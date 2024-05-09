using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    // vanilla, who cares
    public class TurretWeapons
    {
        List<IMyUserControllableGun> _guns = new List<IMyUserControllableGun>();
        public int
               salvoTickCounter = 0,
               offsetTicks = 0,
               offsetTimer = 0;
        readonly int salvoTicks = 0; // 0 or lower means no salvoing
        int ptr = -1;
        public Vector3D AimRef
        {
            get
            {
                Vector3D r = Vector3D.Zero;
                if (_guns.Count == 0) return r;
                foreach (var g in _guns)
                    r += g.WorldMatrix.Translation;
                r /= _guns.Count;
                return r;
            }
        }

        public TurretWeapons(int s, List<IMyUserControllableGun> g)
        {
            salvoTicks = s;
            _guns = g;
        }

        public void Fire()
        {
            offsetTicks = 20;
            ++offsetTimer;
        }
        public void Hold() => offsetTicks = -1;

        public bool Active
        {
            get
            {
                if (_guns.Count == 0) return false;
                var anyWeaponOn = false;

                for (int i = 0; i < _guns.Count; i++)
                {
                    if (_guns[i].Enabled)
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

            if (_guns.Count == 0) return;
            while (_guns[0].Closed)
            {
                _guns.RemoveAtFast(0);
                if (_guns.Count == 0) return;
            }

            if (offsetTicks > 0)
            {
                if (salvoTicks <= 0)
                {
                    for (int i = 0; i < _guns.Count; i++)
                        _guns[i].Shoot = true;
                }
                else
                {
                    if (salvoTickCounter < 0)
                    {
                        _guns[Lib.Next(ref ptr, _guns.Count)].Shoot = true;
                        salvoTickCounter = salvoTicks;
                    }
                }
            }
            else
                for (int i = 0; i < _guns.Count; i++)
                    _guns[i].Shoot = false;

        }
    }
}