using System;
using MathNet.Numerics;
using MathNet.Numerics.Interpolation;

namespace osu.Game.Rulesets.Osu.Difficulty.Preprocessing.MovementCorrections
{
    public abstract class ObjectCorrection
    {
        // number of coefficients in the formula for correction0/3
        protected const int NUM_COEFFS = 4;
        protected const double T_RATIO_THRESHOLD = 1.4;
        protected const double CORRECTION_STILL = 0;

        protected static readonly LinearSpline CORRECTION_MOVING_SPLINE = LinearSpline.InterpolateSorted(
            new[] { -1.0, 1.0 },
            new[] { 1.1, 0 });

        protected static void PrepareInterp(double[] ds, double[] ks, double[] scales, double[,,] coeffs,
                                            out LinearSpline kInterp, out LinearSpline scaleInterp, out LinearSpline[,] coeffsInterps)
        {
            kInterp = LinearSpline.InterpolateSorted(ds, ks);
            scaleInterp = LinearSpline.InterpolateSorted(ds, scales);

            coeffsInterps = new LinearSpline[coeffs.GetLength(0), NUM_COEFFS];

            for (int i = 0; i < coeffs.GetLength(0); i++)
            {
                for (int j = 0; j < NUM_COEFFS; j++)
                {
                    double[] coeffij = new double[coeffs.GetLength(2)];

                    for (int k = 0; k < coeffs.GetLength(2); k++)
                    {
                        coeffij[k] = coeffs[i, j, k];
                    }

                    coeffsInterps[i, j] = LinearSpline.InterpolateSorted(ds, coeffij);
                }
            }
        }

        protected static double CalcCorrection(double d, double x, double y,
                                                  LinearSpline kInterp, LinearSpline scaleInterp, LinearSpline[,] coeffsInterps)
        {
            double correctionRaw = kInterp.Interpolate(d);

            for (int i = 0; i < coeffsInterps.GetLength(0); i++)
            {
                double[] cs = new double[NUM_COEFFS];

                for (int j = 0; j < NUM_COEFFS; j++)
                {
                    cs[j] = coeffsInterps[i, j].Interpolate(d);
                }

                correctionRaw += cs[3] * Math.Sqrt(Math.Pow((x - cs[0]), 2) +
                                                   Math.Pow((y - cs[1]), 2) +
                                                   cs[2]);
            }

            return SpecialFunctions.Logistic(correctionRaw) * scaleInterp.Interpolate(d);
        }
    }
}
