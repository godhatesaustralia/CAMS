using System;
using System.Collections.Generic;
using System.Text;

namespace IngameScript
{
    // you know we had to
    // integral decay from framework without clamp
    // maybe from whip by way of framework who knows who cares
    public class PID
    {
        double
            _kP = 0,
            _kI = 0,
            _kD = 0,
            _intDecayRatio = 0,
            _timestep = 0,
            _invTS = 0,
            _errorSum = 0,
            _lastError = 0;

        bool
            _first = true,
            _decay = false;

        public double Value { get; private set; }

        // Used for everywhere else
        public PID(double kP, double kI, double kD, double decay, double ts)
        {
            _kP = kP;
            _kI = kI;
            _kD = kD;
            _timestep = ts;
            _invTS = 1 / _timestep;
            _intDecayRatio = decay;
            _decay = true;
        }

        public double Control(double error)
        {
            if (double.IsNaN(error)) return 0;

            //Compute derivative term
            var errorDerivative = (error - _lastError) * _invTS;

            if (_first)
            {
                errorDerivative = 0;
                _first = false;
            }

            //Compute integral term
            if (!_decay)
                _errorSum += error * _timestep;
            else
                _errorSum = _errorSum * (1.0 - _intDecayRatio) + error * _timestep;

            //Store this error as last error
            _lastError = error;

            //Construct output
            Value = _kP * error + _kI * _errorSum + _kD * errorDerivative;
            return Value;
        }

        public double Control(double error, double timeStep)
        {
            _timestep = timeStep;
            _invTS = 1 / _timestep;
            return Control(error);
        }

        public void Reset()
        {
            _errorSum = 0;
            _lastError = 0;
            _first = true;
        }
    }
}
