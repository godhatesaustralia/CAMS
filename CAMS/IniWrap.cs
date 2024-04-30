using Sandbox.ModAPI.Ingame;
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
        string tld = "~";
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

        public bool hasSection(string aSct)
        {
            return myIni.ContainsSection(aSct);
        }

        public bool hasKey(string aSct, string aKy)
        {
            aKy = keymod(aSct, aKy);
            return myIni.ContainsKey(aSct, aKy);
        }

        public float Float(string aSct, string aKy, float def = 1)
        {
            aKy = keymod(aSct, aKy);
            return myIni.Get(aSct, aKy).ToSingle(def);
        }
        public double Double(string aSct, string aKy, double def = 0)
        {
            aKy = keymod(aSct, aKy);
            return myIni.Get(aSct, aKy).ToDouble(def);
        }

        public byte Byte(string aSct, string aKy, byte def = 2)
        {
            aKy = keymod(aSct, aKy);
            return myIni.Get(aSct, aKy).ToByte(def);
        }
        //public SpriteType Type(string aSct, string aKy)
        //{
        //    aKy = keymod(aSct, aKy);
        //    var t = myIni.Get(aSct, aKy);
        //    string c = tld;
        //    if (t.ToByte(3) != 3)
        //        return (SpriteType)t.ToByte(2);
        //    else
        //        c = t.ToString(c).ToLower();
        //    switch (c) // *sigh*
        //    {
        //        case "shape":
        //        case "sprite":
        //            return SpriteType.TEXTURE;
        //        case "clip":
        //            return SpriteType.CLIP_RECT;
        //        case "text":
        //        default:
        //            return SpriteType.TEXT;
        //    }

        //}
        //public TextAlignment Alignment(string aSct, string aKy)
        //{
        //    aKy = keymod(aSct, aKy);
        //    var a = myIni.Get(aSct, aKy);
        //    string c = tld;
        //    if (a.ToByte(3) != 3)
        //        return (TextAlignment)a.ToByte(2);
        //    else
        //        c = a.ToString(c).ToLower();
        //    switch (c) // oh what the hell
        //    {
        //        case "l":
        //        case "left":
        //            return TextAlignment.LEFT;
        //        case "r":
        //        case "right":
        //            return TextAlignment.RIGHT;
        //        case "c":
        //        case "center":
        //        default:
        //            return TextAlignment.CENTER;
        //    }
        //}

        //public bool TryReadVector2(string aSct, string aKey, out float x, out float y, string n = "")
        //{
        //    x = y = 0;
        //    string s = myIni.Get(aSct, aKey).ToString();
        //    if (s == "")
        //        return false;
        //    var v = s.Split(',');
        //    //return false;
        //    try
        //    {
        //        x = float.Parse(v[0].Trim('('));
        //        y = float.Parse(v[1].Trim(')'));
        //    }
        //    catch (Exception)
        //    {
        //        throw new Exception($"\nError reading {aKey} floats for {aSct} in {n}: \n{v[0]} and {v[1]}");
        //    }
        //    return true;
        //}

        public int Int(string aSct, string aKy, int def = 0)
        {
            aKy = keymod(aSct, aKy);
            return myIni.Get(aSct, aKy).ToInt32(def);
        }
        public bool Bool(string aSct, string aKy, bool def = false)
        {
            aKy = keymod(aSct, aKy);
            return myIni.Get(aSct, aKy).ToBoolean(def);
        }
        public string String(string aSct, string aKy, string def = "")
        {
            aKy = keymod(aSct, aKy);
            return myIni.Get(aSct, aKy).ToString(def);
        }
        //public Color Color(string aSct, string aKy, string def = "")
        //{
        //    aKy = keymod(aSct, aKy);
        //    byte r, g, b, a;
        //    def = myIni.Get(aSct, aKy).ToString(def).ToLower();
        //    if (def.Length != 8)
        //        return VRageMath.Color.White; //safety
        //    r = Hex(def, 0, 2);
        //    g = Hex(def, 2, 2);
        //    b = Hex(def, 4, 2);
        //    a = Hex(def, 6, 2);
        //    return new Color(r, g, b, a);
        //}
        //byte Hex(string input, int start, int length)
        //{
        //    return Convert.ToByte(input.Substring(start, length), 16);
        //}
        string keymod(string s, string k)
        {
            k = !myIni.ContainsKey(s, k.ToLower()) ? k : k.ToLower();
            return k;
        }
        public bool StringContains(string aSct, string t)
        {
            return aSct.Contains(t);
        }

        public override string ToString() => myIni.ToString();

        public void Dispose()
        {
            myIni.Clear();
            IniCount--;
        }
    }
}
