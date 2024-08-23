using Sandbox.ModAPI.Ingame;
using System;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        public Program()
        {
            Runtime.UpdateFrequency |= UpdateFrequency.Update1 | UpdateFrequency.Update10 | UpdateFrequency.Update100;
            ID = Me.CubeGrid.EntityId;

            Datalink.Setup(this);
            
            Debug = new DebugAPI(this, true);

            Targets = new TargetProvider(this);

            ParseComputerSettings();

            AddSystemCommands();

            AddSystemScreens();

            CacheMainSystems();
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

            int n = Math.Min(AllTurrets.Count, _turCheckPtr + MaxAutoTgtChecks);
            for (; _turCheckPtr < n; _turCheckPtr++)
                GetTurretTgt(AllTurrets[_turCheckPtr]);
                
            if (n == AllTurrets.Count)
                _turCheckPtr = 0;

            for (int i = 0; i < Artillery.Count; i++)
                GetTurretTgt(Artillery[i], true);

            #endregion

            #region main-sys-update
            Targets.Update(updateSource, F);

            UpdateRotorTurrets();

            UpdateAMS();

            DisplayRR.Next(ref Displays).Update();
            #endregion

            string r = "====<CAMS>====\n\n";
            r += $"RUNS - {_frame}\nRUNTIME - {_lastRT} ms\nAVG - {_avgRT:0.####} ms\nWORST - {_worstRT} ms, F{_worstF}\n";
            Echo(r);
        }
    }
}
