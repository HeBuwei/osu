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
    public class NextObjectCorrection : ObjectCorrection
    {
        // flow
        private static readonly double[] ds_flow = { 0, 1, 2, 3, 4 };
        private static readonly double[] ks_flow = { -4, -5.3, -5.2, -2.5, -2.5 };
        private static readonly double[] scales_flow = { 1, 1, 1, 1, 1 };

        private static readonly double[,,] coeffs_flow =
        {
            {
                { 0, 1.2, 2, 2, 2 },
                { 0, 0, 0, 0, 0 },
                { 0, 0, 0, 0, 0 },
                { 1.5, 1, 0.4, 0, 0 }
            },
            {
                { 0, 0, 0, 0, 0 },
                { 0, 0, 0, 0, 0 },
                { 0, 0, 0, 0, 0 },
                { 2, 1.5, 2.5, 3.5, 3.5 }
            },
            {
                { 0, 0.3, 0.6, 0.6, 0.6 },
                { 0, 1, 2.4, 2.4, 2.4 },
                { 0, 0, 0, 0, 0 },
                { 0, 0.4, 0.4, 0, 0 }
            },
            {
                { 0, 0.3, 0.6, 0.6, 0.6 },
                { 0, -1, -2.4, -2.4, -2.4 },
                { 0, 0, 0, 0, 0 },
                { 0, 0.4, 0.4, 0, 0 }
            }
        };

        // snap
        private static readonly double[] ds_snap = { 1, 1.5, 2.5, 4, 6, 8 };
        private static readonly double[] ks_snap = { -2, -2, -3, -5.4, -4.9, -4.9 };
        private static readonly double[] scales_snap = { 1, 1, 1, 1, 1, 1 };

        private static readonly double[,,] coeffs_snap =
        {
            {
                { -2, -2, -3, -4, -6, -6 },
                { 0, 0, 0, 0, 0, 0 },
                { 1, 1, 1, 0, 0, 0 },
                { 0.4, 0.4, 0.2, 0.4, 0.3, 0.3 }
            },
            {
                { -1, -1, -1.5, -2, -3, -3 },
                { 1.4, 1.4, 2.1, 2, 3, 3 },
                { 1, 1, 1, 1, 1, 1 },
                { 0.4, 0.4, 0.2, 0.4, 0.2, 0.2 }
            },
            {
                { -1, -1, -1.5, -2, -3, -3 },
                { -1.4, -1.4, -2.1, -2, -3, -3 },
                { 1, 1, 1, 1, 1, 1 },
                { 0.4, 0.4, 0.2, 0.4, 0.2, 0.2 }
            },
            {
                { 0, 0, 0, 0, 0, 0 },
                { 0, 0, 0, 0, 0, 0 },
                { 0, 0, 0, 0, 0, 0 },
                { 0, 0, 1, 0.6, 0.6, 0.6 }
            },
            {
                { 1, 1, 1.5, 2, 3, 3 },
                { 0, 0, 0, 0, 0, 0 },
                { 1, 1, 1, 1, 1, 1 },
                { 0, 0, -0.6, -0.4, -0.3, -0.3 }
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
        /// Estimate how next object affects the difficulty of hitting current object
        /// </summary>
        /// <param name="obj3">Next object</param>
        /// <param name="t12">Time between previous and current objects</param>
        /// <param name="t23">Time between current and next objects</param>
        /// <param name="d12">Distance between previous and current objects</param>
        /// <param name="d23">Distance between current and next objects</param>
        /// <param name="s12">Displacement between previous and current objects</param>
        /// <param name="s23">Displacement between current and next objects</param>
        /// <param name="flowiness123">How "flowy" movement is</param>
        /// <param name="obj2InTheMiddle">Is current object temporally in the middle between previous and next</param>
        /// <returns>Correction value</returns>
        public static double Calculate(OsuHitObject obj3, double t12, double t23, double d12, double d23, Vector<double> s12, Vector<double> s23, out double flowiness123, out bool obj2InTheMiddle)
        {
            double correction = 0;
            flowiness123 = 0;
            obj2InTheMiddle = false;

            if (obj3 != null && d12 != 0)
            {
                double tRatio = t12 / t23;

                if (tRatio > T_RATIO_THRESHOLD)
                {
                    if (d23 == 0)
                    {
                        correction = 0;
                    }
                    else
                    {
                        double cos123 = Math.Min(Math.Max(-s12.DotProduct(s23) / d12 / d23, -1), 1);
                        double correctionMoving = CORRECTION_MOVING_SPLINE.Interpolate(cos123);

                        double movingness = SpecialFunctions.Logistic(d23 * 6 - 5) - SpecialFunctions.Logistic(-5);
                        correction = (movingness * correctionMoving) * 0.5;
                    }
                }
                else if (tRatio < 1 / T_RATIO_THRESHOLD)
                {
                    if (d23 == 0)
                    {
                        correction = 0;
                    }
                    else
                    {
                        double cos123 = Math.Min(Math.Max(-s12.DotProduct(s23) / d12 / d23, -1), 1);
                        correction = (1 - cos123) * SpecialFunctions.Logistic((d23 * tRatio - 1.5) * 4) * 0.15;
                    }
                }
                else
                {
                    obj2InTheMiddle = true;

                    var normalizedPos = s23 / t23 * t12;
                    double x = normalizedPos.DotProduct(s12) / d12;
                    double y = (normalizedPos - x * s12 / d12).L2Norm();

                    double correctionFlow = CalcCorrection(d12, x, y, kFlowInterp, scaleFlowInterp, coeffsFlowInterps);
                    double correctionSnap = CalcCorrection(d12, x, y, kSnapInterp, scaleSnapInterp, coeffsSnapInterps);

                    flowiness123 = SpecialFunctions.Logistic((correctionSnap - correctionFlow - 0.05) * 20);

                    correction = Math.Max(Mean.PowerMean(correctionFlow, correctionSnap, -10) - 0.1, 0) * 0.5;
                }
            }
            return correction;
        }
    }
}
