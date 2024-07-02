using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    // vanilla, who cares
    public class Weapons
    {
        List<IMyUserControllableGun> _guns = new List<IMyUserControllableGun>();
        public long
               salvoTickCounter = 0,
               offsetTicks = 0,
               offsetTimer = 0,
               lastTrigger = 0;
        readonly long salvoTicks = 0; // 0 or lower means no salvoing
        int ptr = -1;
        bool Shooting = false;

        public Vector3D AimPos
        {
            get
            {
                var r = Vector3D.Zero;
                if (_guns.Count == 0) return r;
                foreach (var g in _guns)
                    r += g.WorldMatrix.Translation;
                r /= _guns.Count;
                return r;
            }
        }

        public Weapons(int s, List<IMyUserControllableGun> g)
        {
            salvoTicks = s;
            _guns = g;
        }

        public void Fire(long f)
        {
            Shooting = true;
            offsetTicks = 20;
            ++offsetTimer;
            f -= lastTrigger;
            salvoTickCounter -= f;
            offsetTicks -= f;
            lastTrigger += f;

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

        public void Hold()
        {
            if (!Shooting)
                return;
            for (int i = 0; i < _guns.Count; i++)
                _guns[i].Shoot = false;
            offsetTicks = -1;
            Shooting = false;
        }

    }
}