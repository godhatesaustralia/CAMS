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
        MySprite[] sprites;
        public int ptr { get; private set; }
        public Func<int> pMax = null;
        public Action<Screen> GetData = null, Select = null, Back = null;
        readonly float graphLength;
        UpdateFrequency update;
        public Screen(Func<int> m, MySprite[] spr = null, Action<Screen> a = null, float g = 0, UpdateFrequency u = UpdateFrequency.Update1)
        {
            ptr = 0;
            pMax = m;
            sprites = spr;
            GetData = a;
            graphLength = g;
            update = u;
        }
        public void SetData(string d, int i)
        {
            sprites[i].Data = d;
        }

        public void SetLength(float f, int i)
        {
            var s = sprites[i];
            if (s.Type != SpriteType.TEXTURE) return;
            var a = sprites[i].Size = new Vector2(graphLength * f, s.Size.Value.Y);
        }
        public void Up()
        {
            if (ptr == 0)
                return;
            else ptr--;
        }

        public void Down()
        {
            if (ptr == pMax.Invoke() - 1)
                return;
            else ptr++;
        }

        public virtual void Draw(IMyTextSurface s, UpdateFrequency u)
        {
            if ((u & update) == 0) return;
            s.ScriptBackgroundColor = Lib.bG;
            //if (!Active) return;
            GetData(this);
            var f = s?.DrawFrame();
            if (!f.HasValue) return;
            f.Value.Add(new MySprite(data: "SquareHollow", position: new Vector2(256, 256), size: new Vector2(520, 308), color: Lib.Green));
            for (int i = 0; i < sprites.Length; i++)
                f.Value.Add(sprites[i]);
            f.Value.Dispose();
        }

    }
}