using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    public class Display
    {
        Program _m;
        IMyTextSurface _surf;
        MySprite[] _sprites = null;
        readonly Vector2 _cnr = new Vector2(256, 256), _sz = new Vector2(512, 512);
        public readonly string Name = null;
        string _active;
        public readonly bool isLarge = false;
        Dictionary<string, Screen> _screens => isLarge ? _m.LCDScreens : _m.CtrlScreens;
        public int ptr { get; private set; }
        // just keep everything to a minimum for now
        //readonly int _offset;
        //static int _oBase = 0;
        Func<int> ptrMax = null;
        public UpdateFrequency Freq = Lib.u1;
        Color _bg, _main;
        public Display(Program m, IMyTerminalBlock b, string a, bool vcr = true)
        {
            _m = m;
            _surf = b is IMyTextPanel ? b as IMyTextSurface : (b is IMyTextSurfaceProvider ? ((IMyTextSurfaceProvider)b).GetSurface(0) : null);
            using (var p = new iniWrap())
                if (p.CustomData(b))
                {
                    Name = p.String(Lib.HDR, "name", b.CustomName);

                    p.TryReadVector2(Lib.HDR, "size", ref _sz);
                    _bg = p.Color(Lib.HDR, "colorBG", Color.Black);
                    _main = p.Color(Lib.HDR, "colorMain", Lib.GRN);
                    isLarge = b is IMyTextPanel;
                    _sprites = p.Sprites(Lib.HDR, vcr ? Lib.SPR : Lib.SPR + "_V");
                }
                else return;
            SetActive(a ?? Lib.MS);
        
        }

        public void SetActive(string a)
        {
            if (!_screens.ContainsKey(a)) return;
            ptr = 0;
            ptrMax = _screens[a].pMax;
            Freq = _screens[a].Update;
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

        public void Update(UpdateFrequency u) // cursed
        {
            int i = 0;
            if ((u & Freq) == 0) return;
            _surf.ScriptBackgroundColor = _bg;
            _screens[_active].GetData(ptr);
            var f = _surf.DrawFrame();
            for (; i < _screens[_active].sprites.Length; i++)
                f.Add(_screens[_active].sprites[i]);
            for (i = 0; i < _sprites.Length; i++)
                f.Add(_sprites[i]);
            f.Dispose();
        }
    }
    public class Screen
    {
        public MySprite[] sprites;
        public int ptr { get; protected set; }
        public Func<int> pMax = null;
        Action<int, Screen> Data = null, Select = null, Back = null;
        protected readonly float graphLength;
        public readonly UpdateFrequency Update;
        public Screen(Func<int> m, MySprite[] s, Action<int, Screen> a = null, float g = 0, UpdateFrequency u = UpdateFrequency.Update10)
        {
            ptr = 0;
            sprites = s;
            pMax = m;
            Data = a;
            graphLength = g;
            Update = u;
        }
        public void GetData(int p) => Data(p, this);
        public void SetData(string d, int i) => sprites[i].Data = d;    
        public void SetColor(Color c, int i) => sprites[i].Color = c;
    }

    public class ListScreen : Screen
    {
        public int pgs, cur, cnt; // pages #, current page, allowed items count (def 4)

        public ListScreen(Func<int> m, int mx = 4, MySprite[] spr = null, Action<int, Screen> a = null, float g = 0, UpdateFrequency u = UpdateFrequency.Update10) : base(m, spr, a, g, u)
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