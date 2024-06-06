using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        public Program()
        {
            Targets = new TargetProvider(this);
            Debug = new DebugAPI(this, true);
            Components.Add(Lib.SN, new Scanner(Lib.SN));
            Runtime.UpdateFrequency |= UpdateFrequency.Update1 | UpdateFrequency.Update10 | UpdateFrequency.Update100;
            Start();
        }

        public void Save()
        {
            // Called when the program needs to save its state. Use
            // this method to save your state to the Storage field
            // or some other means. 
            // 
            // This method is optional and can be removed if not
            // needed.
        }

        public void Main(string argument, UpdateType updateSource)
        {
            _frame++;
            _totalRT += Runtime.TimeSinceLastRun.TotalMilliseconds;
                
            _cmd.Clear();
            if (argument != "" && _cmd.TryParse(argument))
            {
                if (Components.ContainsKey(_cmd.Argument(0)) && Components[_cmd.Argument(0)].Commands.ContainsKey(_cmd.Argument(1)))
                    Components[_cmd.Argument(0)].Commands[_cmd.Argument(1)].Invoke(_cmd);
                else if (_cmd.Argument(0) == "switch" && _cmd.ArgumentCount == 3 && Displays.ContainsKey(_cmd.Argument(1)))
                    Displays[_cmd.Argument(1)].SetActive(_cmd.Argument(2));
                else
                {
                    if (Displays.ContainsKey(_cmd.Argument(0)))
                    {
                        if (_cmd.Argument(1) == "up")
                            Displays[_cmd.Argument(0)].Up();
                        else if (_cmd.Argument(1) == "down")
                            Displays[_cmd.Argument(0)].Down();
                        //else if (_cmd.Argument(0) == "select")
                        //    Displays[_cmd.Argument(0)].Select.Invoke(s);
                        //else if (_cmd.Argument(0) == "back")
                        //    Displays[_cmd.Argument(0)].Back.Invoke(s);
                    }
                }
            }
            var rt = Runtime.LastRunTimeMs;
            if (_worstRT < rt)
            {
                _worstRT = rt;
                _worstF = _frame;
            }
            var u = Lib.UpdateConverter(updateSource);
            if (_runtimes.Count == _rtMax)
                _runtimes.Dequeue();
            _runtimes.Enqueue(rt);
            if ((u & UpdateFrequency.Update10) != 0)
            {
                _avgRT = 0;
                foreach (var qr in _runtimes)
                    _avgRT += qr;
                _avgRT /= _rtMax;
                Gravity = Controller.GetNaturalGravity();
            }
            if ((u & UpdateFrequency.Update100) != 0)
                Debug.RemoveDraw();
            UpdateFrequency tgtFreq = UpdateFrequency.Update1;

            foreach (var comp in Components.Values)
            {
                if ((comp.Frequency & u) != 0)
                    comp.Update(tgtFreq);
                tgtFreq |= comp.Frequency;
            }
            Targets.Update(u);
            _totalRT += rt;
            Runtime.UpdateFrequency |= u;
            foreach (var s in Displays.Values)
                s.Update(u);

            Runtime.UpdateFrequency = tgtFreq;
            string r = "[[COMBAT MANAGER]]\n\n";
            foreach (var tgt in Targets.AllTargets())
                r += $"{tgt.eIDString}\nDIST {tgt.Distance:0000}, ELAPSED {tgt.Elapsed(F)}\n";
            //foreach (var c in Components.Values)
            //    Debug.PrintHUD(c.Debug);
            //Debug.PrintHUD(Components[Lib.TR].Debug);
            r += $"RUNS - {_frame}\nRUNTIME - {rt} ms\nAVG - {_avgRT:0.####} ms\nWORST - {_worstRT} ms, F{_worstF}\n";
            r += Components[Lib.SN].Debug;
            Echo(r);

        }
    }
}
