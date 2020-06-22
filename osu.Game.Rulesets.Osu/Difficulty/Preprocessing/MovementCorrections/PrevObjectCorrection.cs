// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using MathNet.Numerics;
using MathNet.Numerics.Interpolation;
using MathNet.Numerics.LinearAlgebra;
using osu.Game.Rulesets.Osu.Difficulty.MathUtil;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Preprocessing.MovementCorrections
{
    public class PrevObjectCorrection : ObjectCorrection
    {
        // flow
        private static readonly double[] ds_flow = { 0, 1, 1.35, 1.7, 2.3, 3 };
        private static readonly double[] ks_flow = { -11.5, -5.9, -5.4, -5.6, -2, -2 };
        private static readonly double[] scales_flow = { 1, 1, 1, 1, 1, 1 };

        private static readonly double[,,] coeffs_flow =
        {
            {
                { 0, -0.5, -1.15, -1.8, -2, -2 },
                { 0, 0, 0, 0, 0, 0 },
                { 1, 1, 1, 1, 1, 1 },
                { 6, 1, 1, 1, 1, 1 }
            },
            {
                { 0, -0.8, -0.9, -1, -1, -1 },
                { 0, 0.5, 0.75, 1, 2, 2 },
                { 1, 0.5, 0.4, 0.3, 0, 0 },
                { 3, 0.7, 0.7, 0.7, 1, 1 }
            },
            {
                { 0, -0.8, -0.9, -1, -1, -1 },
                { 0, -0.5, -0.75, -1, -2, -2 },
                { 1, 0.5, 0.4, 0.3, 0, 0 },
                { 3, 0.7, 0.7, 0.7, 1, 1 }
            },
            {
                { 0, 0, 0, 0, 0, 0 },
                { 0, 0.95, 0.975, 1, 0, 0 },
                { 0, 0, 0, 0, 0, 0 },
                { 0, 0.7, 0.55, 0.4, 0, 0 }
            },
            {
                { 0, 0, 0, 0, 0, 0 },
                { 0, -0.95, -0.975, -1, 0, 0 },
                { 0, 0, 0, 0, 0, 0 },
                { 0, 0.7, 0.55, 0.4, 0, 0 }
            }
        };

        // snap
        private static readonly double[] ds_snap = { 0, 1.5, 2.5, 4, 6, 8 };
        private static readonly double[] ks_snap = { -1, -5, -6.7, -6.5, -4.3, -4.3 };
        private static readonly double[] scales_snap = { 1, 0.85, 0.6, 0.8, 1, 1 };

        private static readonly double[,,] coeffs_snap =
        {
            {
                { 0.5, 2, 2.8, 5, 5, 5 },
                { 0, 0, 0, 0, 0, 0 },
                { 1, 1, 1, 0, 0, 0 },
                { 0.6, 1, 0.8, 0.6, 0.2, 0.2 }
            },
            {
                { 0.25, 1, 0.7, 2, 2, 2 },
                { 0.5, 2, 2.8, 4, 6, 6 },
                { 1, 1, 1, 1, 1, 1 },
                { 0.6, 1, 0.8, 0.3, 0.2, 0.2 }
            },
            {
                { 0.25, 1, 0.7, 2, 2, 2 },
                { -0.5, -2, -2.8, -4, -6, -6 },
                { 1, 1, 1, 1, 1, 1 },
                { 0.6, 1, 0.8, 0.3, 0.2, 0.2 }
            },
            {
                { 0, 0, -0.5, -2, -3, -3 },
                { 0, 0, 0, 0, 0, 0 },
                { 1, 1, 1, 1, 1, 1 },
                { -0.7, -1, -0.9, -0.1, -0.1, -0.1 }
            }
        };

        private static LinearSpline kFlowInterp;
        private static LinearSpline scaleFlowInterp;
        private static LinearSpline[,] coeffsFlowInterps;
        private static LinearSpline kSnapInterp;
        private static LinearSpline scaleSnapInterp;
        private static LinearSpline[,] coeffsSnapInterps;

        /// <summary>
        /// Gets the interpolations ready for use.
        /// </summary>
        public static void Initialize()
        {
            PrepareInterp(ds_flow, ks_flow, scales_flow, coeffs_flow, out kFlowInterp, out scaleFlowInterp, out coeffsFlowInterps);
            PrepareInterp(ds_snap, ks_snap, scales_snap, coeffs_snap, out kSnapInterp, out scaleSnapInterp, out coeffsSnapInterps);
        }

        /// <summary>
        /// Estimate how object 0 affects the difficulty of hitting current object
        /// </summary>
        /// <param name="obj0">Object 0</param>
        /// <param name="t01">Time between object 0 and object 1</param>
        /// <param name="t12">Time between object 1 and current object</param>
        /// <param name="d01">Distance between object 0 and object 1</param>
        /// <param name="d12">Distance between object 1 and current object</param>
        /// <param name="s01">Normalized distance between object 0 and object 1</param>
        /// <param name="s12">Normalized distance between object 1 and current object</param>
        /// <param name="flowiness012"></param>
        /// <param name="obj1InTheMiddle"></param>
        /// <returns>Correction value</returns>
        public static double Calculate(OsuHitObject obj0, double t01, double t12, double d01, double d12, Vector<double> s01, Vector<double> s12, out double flowiness012, out bool obj1InTheMiddle)
        {
            double correction = 0;
            flowiness012 = 0;
            obj1InTheMiddle = false;

            if (obj0 != null && d12 != 0)
            {
                double tRatio = t12 / t01;

                if (tRatio > T_RATIO_THRESHOLD)
                {
                    if (d01 == 0)
                    {
                        correction = CORRECTION_STILL;
                    }
                    else
                    {
                        double cos012 = Math.Min(Math.Max(-s01.DotProduct(s12) / d01 / d12, -1), 1);
                        double correctionMoving = CORRECTION_MOVING_SPLINE.Interpolate(cos012);

                        double movingness = SpecialFunctions.Logistic(d01 * 6 - 5) - SpecialFunctions.Logistic(-5);
                        correction = (movingness * correctionMoving + (1 - movingness) * CORRECTION_STILL) * 1.5;
                    }
                }
                else if (tRatio < 1 / T_RATIO_THRESHOLD)
                {
                    if (d01 == 0)
                    {
                        correction = 0;
                    }
                    else
                    {
                        double cos012 = Math.Min(Math.Max(-s01.DotProduct(s12) / d01 / d12, -1), 1);
                        correction = (1 - cos012) * SpecialFunctions.Logistic((d01 * tRatio - 1.5) * 4) * 0.3;
                    }
                }
                else
                {
                    obj1InTheMiddle = true;

                    var normalizedPos = -s01 / t01 * t12;
                    double x = normalizedPos.DotProduct(s12) / d12;
                    double y = (normalizedPos - x * s12 / d12).L2Norm();

                    double correctionFlow = CalcCorrection(d12, x, y, kFlowInterp, scaleFlowInterp, coeffsFlowInterps);
                    double correctionSnap = CalcCorrection(d12, x, y, kSnapInterp, scaleSnapInterp, coeffsSnapInterps);
                    double correctionStop = calcCorrectionStop(x, y);

                    flowiness012 = SpecialFunctions.Logistic((correctionSnap - correctionFlow - 0.05) * 20);

                    correction = Mean.PowerMean(new[] { correctionFlow, correctionSnap, correctionStop }, -10) * 1.3;
                }
            }
            return correction;
        }

        private static double calcCorrectionStop(double x, double y)
        {
            return SpecialFunctions.Logistic(10 * Math.Sqrt(x * x + y * y + 1) - 12);
        }
    }
}
