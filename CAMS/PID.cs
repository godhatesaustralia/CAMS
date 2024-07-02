using System;
using VRageMath;

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
            _lower,
            _upper,
            _timestep = 0,
            _invTS = 0,
            _errorSum = 0,
            _lastError = 0;
        bool
            _first = true,
            _decay = false;

        public double Value { get; private set; }

        // turret
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

        // aimbot
        public PID(double kP, double kI, double kD, double lBnd, double uBnd, double decay, double ts)
        {
            _kP = kP;
            _kI = kI;
            _kD = kD;
            _lower = lBnd;
            _upper = uBnd;
            _timestep = ts;
            _invTS = 1 / _timestep;
            _intDecayRatio = decay;
            _decay = true;
        }

        public double Control(double error)
        {
            if (double.IsNaN(error)) return 0;

            //Compute dI term
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

    // smac ignore this
    public class PDController
    {
        public double 
            gain_p,
            gain_d;

        double 
            second,
            lastInput;

        public PDController(double pGain, double dGain, float hz = 60f)
        {
            gain_p = pGain;
            gain_d = dGain;
            second = hz;
        }

        public double Filter(double input, int r) // r => # of digits to round
        {
            double 
                rInput = Math.Round(input, r),
                dI = (rInput - lastInput) * second; // derivative
            lastInput = rInput;

            return (gain_p * input) + (gain_d * dI);
        }

        public void Reset()
        {
            lastInput = 0;
        }
    }

    // alysius controller
    public delegate void AdjustFunc(ref double val);
    public class PCtrl
    {
        const int MIN_REFRESH = 3; //Hard Limit To Avoid Spasm
        double 
            g_out, // gain output
            g_pre, // gain predicted
            lim_out, // output limit
            
            tgtDelta,
           // lastOut,
            lastTgt;
        long lastF;
        AdjustFunc _fixAngle;
        // double oGain = 60, double pGain = 1, double oLim = 30
        public PCtrl(AdjustFunc fix, double oGain, double pGain, double oLim )
        {
            _fixAngle = fix;
            g_out = oGain;
            g_pre = pGain;
            lim_out = oLim;
        }

        // cur, exp, frame
        public float Filter(double cur, double exp, long f)
        {
            var stepDelta = Math.Max(f - lastF, 1); // limits step change to one for obv reason

            double curDelta = exp - lastTgt;

            if (stepDelta < MIN_REFRESH) // if dT is under spasm limit, clamp it to that
            {
                curDelta *= (double)MIN_REFRESH / stepDelta;
                stepDelta = MIN_REFRESH;
            }
            _fixAngle(ref curDelta);

            if (tgtDelta * curDelta < 0)  //Sign Reversal
            {
                tgtDelta = g_pre * curDelta;
            }
            else
            {
                // (1 - 1) * lastTgtDelta + (1 * curDelta) = curDelta
                tgtDelta = ((1 - g_pre) * tgtDelta) + (g_pre * curDelta);
            }

            double delta = exp - cur + tgtDelta; // delta = difference between expected and current value plus target
           _fixAngle(ref delta);

            lastTgt = exp;
            lastF = Math.Max(f, lastF);
            return (float)MathHelper.Clamp(delta * g_out / stepDelta, -lim_out, lim_out);
        }

        public void Reset()
        {
            lastF = 0;
            tgtDelta = lastTgt = 0;
        }
    }
}
