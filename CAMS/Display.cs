using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    public class Display
    {
        const int REF_TKS = 800;
        Program _m;
        IMyTextSurface _surf;
        MySprite[] _sprites = null;
        public readonly string Name = null;
        string _active;
        long _nxSprRef;
        int _ptr, _idx;
        bool _sel;
        public readonly bool isLarge = false;
        Dictionary<string, Screen> _screens => isLarge ? _m.LCDScreens : _m.CtrlScreens;

        Func<int> _pMax = null;
        Color _bg;
        public Display(Program m, IMyTerminalBlock b, string a)
        {
            _m = m;
            _surf = b is IMyTextPanel ? b as IMyTextSurface : (b is IMyTextSurfaceProvider ? ((IMyTextSurfaceProvider)b).GetSurface(0) : null);
            
            using (var p = new iniWrap())
                if (p.CustomData(b))
                {
                    Name = p.String(Lib.H, "name", b.CustomName);
                    isLarge = b is IMyTextPanel;
                    _bg = p.Color(Lib.H, "colorBG", Color.Black);
                    _sprites = p.Sprites(Lib.H, Lib.VCR ? Lib.SPR : Lib.SPR + "_V");
                }
                else return;

            _nxSprRef = m.RNG.Next(REF_TKS / 10) + _nxSprRef;
            SetActive(a ?? Lib.MS);
        }

        public void SetActive(string a)
        {
            if (!_screens.ContainsKey(a)) return;

            _sel = false;
            _ptr = _idx = 0;
            _pMax = _screens[a].Max;
            _active = a;
        }

        public void Up()
        {
            if (_ptr == 0)
                return;
            else --_ptr;
        }

        public void Down()
        {
            if (_ptr == _pMax() - 1)
                return;
            else ++_ptr;
        }

        public void Select()
        {    
            var s = _screens[_active];
            if (s.Enter == null) return;

            _idx = _ptr;

            _sel = true;
            s.Enter(_ptr, s);
            _pMax = s.Max;
            _ptr = 0;
        }

        public void Back()
        {
            var s = _screens[_active];
            if (s.Return == null) return;

            _ptr = _idx;
            _sel = false;
            s.Return(_ptr, s);
            _pMax = s.Max;
        }

        public void Update() // cursed
        {
            int i = 0;
            var s = _screens[_active];

            _surf.ScriptBackgroundColor = _bg;
            s.Data(_ptr, _idx, _sel, s);

            var f = _surf.DrawFrame();

            if (_m.F >= _nxSprRef)
            {
                f.Add(Program.X);
                _nxSprRef += REF_TKS;
            }

            for (; i < s.Sprites.Length; i++)
                f.Add(s.Sprites[i]);

            if (s.UseBaseSprites)
                for (i = 0; i < _sprites.Length; i++)
                    f.Add(_sprites[i]);

            f.Dispose();
        }
    }
    public class Screen
    {
        public int Index;
        public MySprite[] Sprites;
        public bool UseBaseSprites, Sel;
        public Func<int> Max = null;
        public Action<int, int, bool, Screen> Data = null;
        public Action<int, Screen> Enter = null, Return = null;
        public readonly int MinTicks;
        public Screen(Func<int> m, MySprite[] s, Action<int, int, bool, Screen> d = null, Action<int, Screen> e = null, Action<int, Screen> r = null, bool u = true)
        {
            Sprites = s;
            Max = m;
            Data = d;
            Enter = e;
            Return = r;
            UseBaseSprites = u;

        if (!Lib.VCR)
            for (int i = 0; i < Sprites.Length; ++i)
                if ((int)Sprites[i].Type == 2)
                    Sprites[i].RotationOrScale *= Lib.FSCL;
        }
        public void Write(string d, int i) => Sprites[i].Data = d;
        public void Color(Color c, int i) => Sprites[i].Color = c;
    }
}