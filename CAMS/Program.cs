using Sandbox.ModAPI.Ingame;
using System;
using VRage.Game.GUI.TextPanel;
using System.Collections.Generic;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        public Program()
        {
            Runtime.UpdateFrequency |= UpdateFrequency.Update1 | UpdateFrequency.Update10 | UpdateFrequency.Update100;
            ID = Me.CubeGrid.EntityId;

            _surf = Me.GetSurface(0);
            _surf.ContentType = ContentType.SCRIPT;
            
            Debug = new DebugAPI(this, true);

            Targets = new TargetProvider(this);
            Targets.Update(Lib.u1, -1);

            ParseComputerSettings();

            SystemCommands();

            AddSystemScreens();

            CacheMainSystems();

            UpdateRotorTurrets();

            UpdateLaunchers();

            UpdateMissileGuidance();

            _frame = 0;  
        }

        public void Save()
        {
        }

        public void Main(string argument, UpdateType updateSource)
        {
            #region core-clock
            _frame++;
            _totalRT += Runtime.TimeSinceLastRun.TotalMilliseconds;
            _lastRT = Runtime.LastRunTimeMs;

            if (_worstRT < _lastRT)
            {
                _worstRT = _lastRT;
                _worstF = _frame;
            }

            if (_runtimes.Count == 10)
                _runtimes.Dequeue();
            _runtimes.Enqueue(_lastRT);

            if ((updateSource & Lib.u10) != 0)
            {
                _avgRT = 0;
                foreach (var qr in _runtimes)
                    _avgRT += qr;
                _avgRT /= 10;

                #region pb-display
                var f = _surf.DrawFrame();
                sprites[4].Data = $"RUNTIME {_avgRT:0.000} MS";

                if (F % 200 == 0)
                    f.Add(X);
                foreach (var s in sprites)
                    f.Add(s);

                f.Dispose();
                #endregion

                Gravity = Controller.GetNaturalGravity();
            }

            if (F % 100 == 0)
                Debug.RemoveDraw();
            #endregion

            #region argument-parsing
            if (argument != "")
            {
                _cmd.Clear();
                if (_cmd.TryParse(argument))
                {
                    if (Commands.ContainsKey(_cmd.Argument(0)))
                        Commands[_cmd.Argument(0)].Invoke(_cmd);
                    else if (Displays.ContainsKey(_cmd.Argument(0)) && _cmd.ArgumentCount == 2)
                    {
                        // allegedly this is some kind of table or something so
                        switch (_cmd.Argument(1))
                        {
                            case "up":
                            {
                                Displays[_cmd.Argument(0)].Up();
                                break;
                            }
                            case "down":
                            {
                                Displays[_cmd.Argument(0)].Down();
                                break;
                            }
                            case "select":
                            {
                                Displays[_cmd.Argument(0)].Select();
                                break;
                            }
                            case "back":
                            default:
                            {
                                Displays[_cmd.Argument(0)].Back();
                                break;
                            }
                        }
                    }
                }
            }
            #endregion

            #region inline scan checks
            if (F % MastCheckTicks == 0) // guar
                foreach (var m in Masts.Values)
                    m.Update();

            int i = AllTurrets.Count - 1; 
            for (; i > 0; i--)
            {
                var at = AllTurrets[i];
                if (at.Closed || !at.IsFunctional)
                    AllTurrets.RemoveAtFast(i);
                else GetTurretTgt(at);
            }
            
            for (i = Math.Max(0, _turCheckPtr - MaxAutoTgtChecks); _turCheckPtr > i; _turCheckPtr--)
            {
                var at = Artillery[_turCheckPtr];
                if (at.Closed || !at.IsFunctional)
                    AllTurrets.RemoveAtFast(_turCheckPtr);
                else GetTurretTgt(at, true);
            }

            if (_turCheckPtr <= 0)
                _turCheckPtr = Artillery.Count - 1;
            #endregion

            #region main-sys-update
            Targets.Update(updateSource, F);

            UpdateRotorTurrets();

            UpdateLaunchers();

            UpdateMissileGuidance();

            DisplayRR.Next(ref Displays).Update();
            #endregion

            string r = "====<CAMS>====\n\n=<PERF>=\n";
            r += $"RUNS - {_frame}\nRUNTIME - {_lastRT} ms\nAVG - {_avgRT:0.####} ms\nWORST - {_worstRT} ms, F{_worstF}\n\n=<TGTS>=\n";
            r += Targets.Log;
            Echo(r);
        }
    }
}
