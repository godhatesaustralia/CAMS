using EmptyKeys.UserInterface.Generated.PlayerTradeView_Bindings;
using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    public class Screen
    {
        public bool Active = false;
        const int _dSz = 64;
        MySprite[] sprites;
        public int ptr { get; protected set; }
        public Func<int> pMax = null;
        public Action<Screen> GetData = null, Select = null, Back = null;
        protected readonly float graphLength;
        UpdateFrequency update;
        public Screen(Func<int> m, MySprite[] s, Action<Screen> a = null, float g = 0, UpdateFrequency u = UpdateFrequency.Update10)
        {
            ptr = 0;
            sprites = s;
            pMax = m;
            GetData = a;
            graphLength = g;
            update = u;
        }

        public static MySprite Sprite(SpriteType t, string d, float x, float y, Color c, int align = 2, float? rs = null, string fnt = null, float ? sx = null, float? sy = null)
        {
            var p = new Vector2(x, y);
            var a = (TextAlignment)align;
            return (t == Lib.TXT 
                ? new MySprite(t, d, p, null, c, fnt, a, rs.HasValue ? rs.Value : 1) 
                : new MySprite(t, d, p, sx.HasValue && sy.HasValue ? new Vector2(sx.Value, sy.Value) : new Vector2(sx ?? _dSz, sy ?? _dSz), c, null, a, rs ?? 0));
        }

        public void SetData(string d, int i) => sprites[i].Data = d;

        public void SetColor(Color c, int i) => sprites[i].Color = c;

        //public void SetLength(float f, int i)
        //{
        //    var s = sprites[i];
        //    if (s.Type != Lib.SHP) return;
        //    var a = sprites[i].Size = new Vector2(graphLength * f, s.Size.Value.Y);
        //}
        public virtual void Up()
        {
            if (ptr == 0)
                return;
            else ptr--;
        }

        public virtual void Down()
        {
            if (ptr == pMax.Invoke() - 1)
                return;
            else ptr++;
        }

        public void Draw(IMyTextSurface s, UpdateFrequency u)
        {
            if ((u & update) == 0) return;
            s.ScriptBackgroundColor = Lib.BG;
            GetData(this);
            var f = s?.DrawFrame();
            if (!f.HasValue) return;
            f.Value.Add(new MySprite(data: Lib.SQH, position: new Vector2(256, 256), size: new Vector2(520, 308), color: Lib.GRN));
            for (int i = 0; i < sprites.Length; i++)
                f.Value.Add(sprites[i]);
            f.Value.Dispose();
        }

    }

    public class ListScreen : Screen
    {
        public int pgs, cur, cnt; // pages #, current page, allowed items count (def 4)

        public ListScreen(Func<int> m, int mx = 4, MySprite[] spr = null, Action<Screen> a = null, float g = 0, UpdateFrequency u = UpdateFrequency.Update10) : base(m, spr, a, g, u)
        {
            cur = 1;
            cnt = mx - 1;
        }

        public override void Up()
        {
            if (pMax.Invoke() == 0 || (ptr == 0 && cur == 1)) return;
            else
            {
                if (ptr == 0 && cur > 1)
                {
                    ptr = cnt;
                    cur--;
                    //sprites["tst"].Reset(256, 293);
                }
                else if (ptr <= cnt)
                {
                    ptr--;
                    //sprites["tst"].Move(0, -tH);
                }
            }

        }
        public override void Down()
        {
            var ct = pMax.Invoke();
            if (ct == 0 || ((ptr == 9 || ptr == ct % 10 - 1) && cur == pgs)) return;
            else
            {
                if (ptr == 9 && cur < pgs)
                {
                    ptr = 0;
                    cur++;
                    //sprites["tst"].Reset(256, 108);
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