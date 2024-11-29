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
        public readonly bool isLarge = false;
        Dictionary<string, Screen> _screens => isLarge ? _m.LCDScreens : _m.CtrlScreens;
        public int ptr { get; private set; }
        Func<int> ptrMax = null;
        Color _bg;
        public Display(Program m, IMyTerminalBlock b, string a, bool vcr = true)
        {
            _m = m;
            _surf = b is IMyTextPanel ? b as IMyTextSurface : (b is IMyTextSurfaceProvider ? ((IMyTextSurfaceProvider)b).GetSurface(0) : null);
            using (var p = new iniWrap())
                if (p.CustomData(b))
                {
                    Name = p.String(Lib.H, "name", b.CustomName);
                    isLarge = b is IMyTextPanel;
                    _bg = p.Color(Lib.H, "colorBG", Color.Black);
                    _sprites = p.Sprites(Lib.H, vcr ? Lib.SPR : Lib.SPR + "_V");
                }
                else return;
            _nxSprRef = m.RNG.Next(REF_TKS / 10) + _nxSprRef;
            SetActive(a ?? Lib.MS);
        }

        public void SetActive(string a)
        {
            if (!_screens.ContainsKey(a)) return;

            ptr = 0;
            ptrMax = _screens[a].pMax;
            _active = a;
        }

        public void Up()
        {
            if (ptr == 0)
                return;
            else ptr--;
        }

        public void Down()
        {
            if (ptr == ptrMax.Invoke() - 1)
                return;
            else ptr++;
        }

        public void Select() => _screens[_active].Enter?.Invoke(ptr, _screens[_active]);

        public void Back() => _screens[_active].Return?.Invoke(ptr, _screens[_active]);

        public void Update() // cursed
        {
            int i = 0;
            var s = _screens[_active];


            _surf.ScriptBackgroundColor = _bg;
            s.Refresh(ptr);

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
        public MySprite[] Sprites;
        public readonly bool UseBaseSprites;
        public int ptr { get; protected set; }
        public Func<int> pMax = null;
        public Action<int, Screen> Data = null, Enter = null, Return = null; 
        public readonly int MinTicks;
        public Screen(Func<int> m, MySprite[] s, Action<int, Screen> d = null, Action<int, Screen> e = null, Action<int, Screen> r = null, bool u = true)
        {
            ptr = 0;
            Sprites = s;
            pMax = m;
            Data = d;
            Enter = e;
            Return = r;
            UseBaseSprites = u;
        }

        public void Refresh(int p) => Data(p, this);
        public void Write(string d, int i) => Sprites[i].Data = d;    
        public void Color(Color c, int i) => Sprites[i].Color = c;
    }

    public class ListScreen : Screen
    {
        public int pgs, cur, cnt; // pages #, current page, allowed items count (def 4)

        public ListScreen(Func<int> m, int mx = 4, MySprite[] spr = null, Action<int, Screen> d = null, Action<int, Screen> e = null, Action<int, Screen> r = null, bool def = false) : base(m, spr, d, e, r, def)
        {
            cur = 1;
            cnt = mx - 1;
        }

        public void Up()
        {
            if (pMax.Invoke() == 0 || (ptr == 0 && cur == 1)) return;
            else
            {
                if (ptr == 0 && cur > 1)
                {
                    ptr = cnt;
                    cur--;
                    //sprites["tst"].Clear(256, 293);
                }
                else if (ptr <= cnt)
                {
                    ptr--;
                    //sprites["tst"].Move(0, -tH);
                }
            }

        }
        public void Down()
        {
            var ct = pMax.Invoke();
            if (ct == 0 || ((ptr == 9 || ptr == ct % 10 - 1) && cur == pgs)) return;
            else
            {
                if (ptr == 9 && cur < pgs)
                {
                    ptr = 0;
                    cur++;
                    //sprites["tst"].Clear(256, 108);
                }
                else if (ptr < cnt)
                {
                    ptr++;
                    //sprites["tst"].Move(0, tH);
                }
            }
        }
    }
}