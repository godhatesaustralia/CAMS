﻿using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Text;
using System.Globalization;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;
using VRage.Input;
using VRage.Game.GUI.TextPanel;
using System.Security.Policy;

namespace IngameScript
{
    public class iniWrap : IDisposable
    {
        static List<MyIni> IniParsers = new List<MyIni>();
        static int IniCount = 0;
        static public int total = 0;
        static public int Count => IniParsers.Count;
        MyIni myIni;
        MyIniParseResult result;

        public iniWrap()
        {
            ++IniCount;
            ++total;
            if (IniParsers.Count < IniCount)
                IniParsers.Add(new MyIni());
            myIni = IniParsers[IniCount - 1];
            myIni.Clear();
        }

        public bool CustomData(IMyTerminalBlock block, out MyIniParseResult Result)
        {
            Result = new MyIniParseResult();
            if (block == null)
                return false;
            var output = myIni.TryParse(block.CustomData, out result);
            Result = result;
            return output;
        }

        public bool CustomData(IMyTerminalBlock block)
        {
            var output = myIni.TryParse(block.CustomData, out result);
            return output;
        }

        public bool HasKey(string aSect, string aKey) => myIni.ContainsKey(aSect, aKey);

        public bool TryReadVector2(string aSct, string aKey, ref Vector2 def)
        {
            string s = myIni.Get(aSct, aKey).ToString();
            if (s == "")
                return false;
            var V = s.Split(',');
            //return false;
            try
            {
                def.X = float.Parse(V[0].Trim('('));
                def.Y = float.Parse(V[1].Trim(')'));
            }
            catch (Exception)
            {
                throw new Exception($"\nError reading {aKey} floats for {aSct}:\n{V[0]} and {V[1]}");
            }
            return true;
        }

        byte Hex(string input, int start = 0, int length = 2) => Convert.ToByte(input.Substring(start, length), 16);

        public int Int(string s, string k, int def = 0) => myIni.Get(s, k).ToInt32(def);

        public float Float(string aSct, string aKy, float def = 1) => myIni.Get(aSct, aKy).ToSingle(def);
        
        public double Double(string aSct, string aKy, double def = 0) => myIni.Get(aSct, aKy).ToDouble(def);

        public bool Bool(string s, string k, bool def = false) => myIni.Get(s, k).ToBoolean(def);

        public string String(string s, string k, string def = "") => myIni.Get(s, k).ToString(def);

        public Color Color(string s, string k, Color def)
        {
            byte r, g, b, a;
            var c = myIni.Get(s, k).ToString().ToLower();
            if (c.Length != 8)
                return def; //safety
            r = Hex(c);
            g = Hex(c, 2);
            b = Hex(c, 4);
            a = Hex(c, 6);
            return new Color(r, g, b, a);
        }

        public bool GyroYPR(string s, string k, out byte y, out byte p, out byte r)
        {
            y = p = r = 0;
            var g = myIni.Get(s, k).ToString().ToLower();
            if (g.Length != 6)
                return false;
            y = Hex(g);
            p = Hex(g, 2);
            r = Hex(g, 4);
            return true;
        }

        public MySprite[] Sprites(string s, string k)
        {
            string[] list = myIni.Get(s, k).ToString().Split('\n'), itm;
            var r = new MySprite[list.Length];
            for (int i = 0; i <list.Length; i++)
            {
                try
                {
                    itm = list[i].Split('$');
                    if (itm.Length != 8 && itm.Length != 9)
                        throw new Exception($"\ninvalid sprite key {itm}");
                    itm[1] = itm[1].Contains(Lib.NL) ? itm[1].Replace(Lib.NL, "\n") : itm[1];
                    var spr = new MySprite
                    {
                        Type = (SpriteType)byte.Parse(itm[0]),
                        Data = itm[1],
                        Position = Lib.V2(float.Parse(itm[2]), float.Parse(itm[3])),
                        Alignment = (TextAlignment)byte.Parse(itm[4]),
                        RotationOrScale = float.Parse(itm[5]),
                        Color = new Color(Hex(itm[6], 0), Hex(itm[6], 2), Hex(itm[6], 4), 255)
                    };
                    if (spr.Type == Program.TXT)
                        spr.FontId = itm[7];
                    else
                        spr.Size = Lib.V2(float.Parse(itm[7]), float.Parse(itm[8]));
                    r[i] = spr;
                }
                catch(Exception)
                {
                    continue;
                }
            }
            return r;
        }

        public override string ToString() => myIni.ToString();

        public void Dispose()
        {
            myIni.Clear();
            IniCount--;
        }
    }
}
