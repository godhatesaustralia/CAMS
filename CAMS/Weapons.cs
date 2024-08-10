using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    public interface IWeapons
    {
        long Offset { get; }
        Vector3D AimPos { get; }
        void Fire(long f);
        void Hold();
    }
    // vanilla, who cares
    public class Weapons : IWeapons
    {
        List<IMyUserControllableGun> _guns = new List<IMyUserControllableGun>();
        long
            salvoTickCounter = 0,
            offsetTicks = 0;

        public long Offset => offsetTicks;
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
            Hold();
        }

        public void Fire(long f)
        {
            if (!Shooting)
            {
                Shooting = true;
                offsetTicks = 20;
            }

            salvoTickCounter--;
            offsetTicks--;

            if (_guns.Count == 0) return;
            while (_guns[0].Closed)
            {
                _guns.RemoveAtFast(0);
                if (_guns.Count == 0) return;
            }

            if (salvoTicks <= 0)
            {
                for (int i = 0; i++< _guns.Count;)
                    _guns[i].Shoot = true;
            }
            else if (salvoTickCounter <= 0)
            {
                _guns[Lib.Next(ref ptr, _guns.Count)].Shoot = true;
                salvoTickCounter = salvoTicks;
            }
        }

        public void Hold()
        {
            if (!Shooting)
                return;
            for (int i = 0; i++ < _guns.Count;)
                _guns[i].Shoot = false;
            offsetTicks = salvoTickCounter = 0;
            Shooting = false;
        }
    }

    public class CoreWeapons : IWeapons
    {
        WCAPI _wapi;
        List<IMyTerminalBlock> _guns = new List<IMyTerminalBlock>();
        long
               salvoTickCounter = 0,
               offsetTicks = 0;
        public long Offset => offsetTicks;
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

        public CoreWeapons(int s, List<IMyTerminalBlock> g, WCAPI w)
        {
            salvoTicks = s;
            _guns = g;
            _wapi = w;
        }

        public void Fire(long f)
        {
            if (!Shooting)
            {
                Shooting = true;
                offsetTicks = 20;
            }

            salvoTickCounter--;
            offsetTicks--;

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
                    for (int i = 0; i++ < _guns.Count;)
                        _wapi.Shoot(_guns[i], true, false);
                }
                else
                {
                    if (salvoTickCounter < 0)
                    {
                        _wapi.Shoot(_guns[Lib.Next(ref ptr, _guns.Count)], true, false);
                        salvoTickCounter = salvoTicks;
                    }
                }
            }
            else
                for (int i = 0; i++ < _guns.Count;)
                    _wapi.Shoot(_guns[i], false, false);

        }

        public void Hold()
        {
            if (!Shooting)
                return;
            for (int i = 0; i++ < _guns.Count;)
                _wapi.Shoot(_guns[i], false, false);
            offsetTicks = -1;
            Shooting = false;
        }
    }
}