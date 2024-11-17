using System;
using VRageMath;

namespace IngameScript
{   
    public class PDCtrl
    {
         double 
            gain_p,
            gain_d,
            second,
            lastInput;

        public double Filter(double input, int r) // r => # of digits to round
        {
            double 
                rInput = Math.Round(input, r),
                dI = (rInput - lastInput) * second; // derivative
            lastInput = rInput;

            return (gain_p * input) + (gain_d * dI);
        }

        public void Reset(double pg, double dg, float hz = 60f)
        {
            gain_p = pg;
            gain_d = dg;
            second = hz;
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
