using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    public interface IWeapons
    {
        bool SwitchOffset { get; }
        Vector3D AimPos { get; }
        Vector3D AimDir { get; }
        void Fire(long f);
        void Hold();
    }
    // vanilla, who cares
    public class Weapons : IWeapons
    {
        List<IMyUserControllableGun> _guns = new List<IMyUserControllableGun>();
        readonly int salvoTicks = 0, offsetTicks; // 0 or lower means no salvoing
        int ptr = -1;
        public long salvoCounter = 0, offsetCounter = 0;

        public bool SwitchOffset => offsetCounter <= 0;
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

        public Vector3D AimDir
        {
            get
            {
                var r = Vector3D.Zero;
                if (_guns.Count == 0) return r;
                foreach (var g in _guns)
                    r += g.WorldMatrix.Forward;
                r /= _guns.Count;
                return r;
            }
        }

        public Weapons(List<IMyUserControllableGun> g, int s = 0, int o = -1)
        {
            _guns = g;
            salvoTicks = s;
            offsetTicks = o;
            foreach (var w in _guns)
                w.Shoot = false;
        }

        public void Fire(long f)
        {
            if (!Shooting)
            {
                Shooting = true;
                offsetCounter = offsetTicks;
            }

            salvoCounter--;
            offsetCounter--;

            if (_guns.Count == 0) return;
            while (_guns[0].Closed)
            {
                _guns.RemoveAtFast(0);
                if (_guns.Count == 0) return;
            }

            if (salvoTicks <= 0)
            {
                for (int i = 0; i < _guns.Count; i++)
                    _guns[i].Shoot = true;
            }
            else if (salvoCounter <= 0)
            {
                _guns[Lib.Next(ref ptr, _guns.Count)].Shoot = true;
                salvoCounter = salvoTicks;
            }
            if (offsetCounter < 0)
                offsetCounter = offsetTicks;
        }

        public void Hold()
        {
            if (!Shooting)
                return;
            for (int i = 0; i < _guns.Count; i++)
                _guns[i].Shoot = false;
            offsetCounter = salvoCounter = 0;
            Shooting = false;
        }
    }

    public class CoreWeapons : IWeapons
    {
        WCAPI _wapi;
        List<IMyTerminalBlock> _guns = new List<IMyTerminalBlock>();
        readonly int salvoTicks, offsetTicks; // 0 or lower means no salvoing
        int ptr = -1;
        long lastF = 0, salvoCounter = 0, offsetCounter = 0;
        public bool SwitchOffset => offsetCounter <= 0;
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

        public Vector3D AimDir
        {
            get
            {
                var r = Vector3D.Zero;
                if (_guns.Count == 0) return r;
                foreach (var g in _guns)
                    r += g.WorldMatrix.Forward;
                r /= _guns.Count;
                return r;
            }
        }

        public CoreWeapons(List<IMyTerminalBlock> g, WCAPI w, int s = 0, int o = 0)
        {
            _guns = g;
            _wapi = w;
            salvoTicks = s;
            offsetTicks = o;
        }

        public void Fire(long f)
        {
            if (!Shooting)
            {
                Shooting = true;
                offsetCounter = offsetTicks;
                lastF = f - 1;
            }

            salvoCounter -= f - lastF;
            offsetCounter -= f - lastF;

            if (_guns.Count == 0) return;
            while (_guns[0].Closed)
            {
                _guns.RemoveAtFast(0);
                if (_guns.Count == 0) return;
            }

            if (salvoTicks <= 0)
            {
                for (int i = 0; i < _guns.Count; i++)
                    _wapi.Shoot(_guns[i], true, false);
            }
            else if (salvoCounter <= 0)
            {
                _wapi.Shoot(_guns[Lib.Next(ref ptr, _guns.Count)], true, false);
                salvoCounter = salvoTicks;
            }
            if (offsetCounter < 0)
                offsetCounter = offsetTicks;

            lastF = f;
        }

        public void Hold()
        {
            if (!Shooting)
                return;
            for (int i = 0; i < _guns.Count; i++)
                _wapi.Shoot(_guns[i], false, false);
            salvoCounter = offsetCounter = 0;
            Shooting = false;
        }
    }
}