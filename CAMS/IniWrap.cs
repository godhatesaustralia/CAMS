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

namespace IngameScript
{
    public class iniWrap : IDisposable
    {
        static List<MyIni> IniParsers = new List<MyIni>();
        static int IniCount = 0;
        MyIni myIni;
        string tld = "~";
        MyIniParseResult result;

        public iniWrap()
        {
            ++IniCount;
            if (IniParsers.Count < IniCount)
                IniParsers.Add(new MyIni());

            myIni = IniParsers[IniParsers.Count - 1];

            myIni.Clear();
        }

        public bool CustomData(IMyTerminalBlock block, out MyIniParseResult Result)
        {
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
        private string keymod(string s, string k)
        {
            k = !myIni.ContainsKey(s, k.ToLower()) ? k : k.ToLower();
            return k;
        }
        public bool StringContains(string aSct, string t)
        {
            return aSct.Contains(t);
        }
        public void Dispose()
        {
            IniCount--;
        }
    }
}
