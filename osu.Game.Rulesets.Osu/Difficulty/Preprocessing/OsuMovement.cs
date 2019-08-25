﻿using System;
using System.Collections.Generic;
using System.Text;

using MathNet.Numerics;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Interpolation;

using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Difficulty.MathUtil;


namespace osu.Game.Rulesets.Osu.Difficulty.Preprocessing
{
    public class OsuMovement
    {
        private static readonly CubicSpline correction0MovingSpline = CubicSpline.InterpolateHermiteSorted(
                                                                            new[] { -1, -0.6, 0.3, 0.5, 1 },
                                                                            new[] { 0.6, 1, 1, 0.6, 0 },
                                                                            new[] { 0.8, 0.8, -0.8, -2, -0.8 });
        // number of coefficients in the formula
        private const int numCoeffs = 4;


        private static readonly double[] ds0f = { 0, 0.5, 1, 1.5, 2, 2.5 };
        private static readonly double[] ks0f = { -5.5, -5.5, -7.5, -7, -4.4, -4.4 };
        private static readonly double[,,] coeffs0f = new double[,,]  {{{-0.5 , -0.5 , -1   , -1.5 , -2   , -2   },
                                                                        { 0   ,  0   ,  0   ,  0   ,  0   ,  0   },
                                                                        { 1   ,  1   ,  1   ,  1   ,  1   ,  1   },
                                                                        { 5   ,  5   ,  4   ,  1.5 ,  1   ,  1   }},
                                                                       {{-0.35, -0.35, -0.7 , -0.75, -1   , -1   },
                                                                        { 0.35,  0.35,  0.7 ,  1.5 ,  2   ,  2   },
                                                                        { 1   ,  1   ,  1   ,  1   ,  1   ,  1   },
                                                                        { 0   ,  0   ,  1   ,  1   ,  1   ,  1   }},
                                                                       {{-0.35, -0.35, -0.7 , -0.75, -1   , -1   },
                                                                        {-0.35, -0.35, -0.7 , -1.5 , -2   , -2   },
                                                                        { 1   ,  1   ,  1   ,  1   ,  1   ,  1   },
                                                                        { 0   ,  0   ,  1   ,  1   ,  1   ,  1   }}};


        private static readonly double[] ds0s = { 1, 1.5, 2.5, 4, 6, 8 };
        private static readonly double[] ks0s = { -1, -1, -5.9, -5, -3.7, -3.7 };
        private static readonly double[,,] coeffs0s = new double[,,]  {{{ 2   , 2   ,  3   ,  4   ,  6   ,  6   },
                                                                        { 0   , 0   ,  0   ,  0   ,  0   ,  0   },
                                                                        { 1   , 1   ,  1   ,  0   ,  0   ,  0   },
                                                                        { 1   , 1   ,  1   ,  0.6 ,  0.4 ,  0.4 }},
                                                                       {{ 1.6 , 1.6 ,  1.8 ,  2   ,  3   ,  3   },
                                                                        { 2   , 2   ,  2.4 ,  4   ,  6   ,  6   },
                                                                        { 1   , 1   ,  1   ,  1   ,  1   ,  1   },
                                                                        { 0   , 0   ,  0.3 ,  0.24,  0.16,  0.16}},
                                                                       {{ 1.6 , 1.6 ,  1.8 ,  2   ,  3   ,  3   },
                                                                        { 2   , 2   , -2.4 , -4   , -6   , -6   },
                                                                        { 1   , 1   ,  1   ,  1   ,  1   ,  1   },
                                                                        { 0   , 0   ,  0.3 ,  0.24,  0.16,  0.16}},
                                                                       {{ 0   , 0   ,  0   , -1   , -1.5 , -1.5 },
                                                                        { 0   , 0   ,  0   ,  0   ,  0   ,  0   },
                                                                        { 1   , 1   ,  1   ,  1   ,  1   ,  1   },
                                                                        { 0   , 0   , -0.3 , -0.24, -0.16, -0.16}}};

        private static readonly double[] ds3f = { 0, 1, 2, 3, 4 };
        private static readonly double[] ks3f = { -4, -4, -4.5, -2.5, -2.5 };
        private static readonly double[,,] coeffs3f = new double[,,]  {{{0  , 1  , 2  , 4  , 4  },
                                                                        {0  , 0  , 0  , 0  , 0  },
                                                                        {0  , 0  , 0  , 0  , 0  },
                                                                        {1.5, 1.5, 1  , 0  , 0  }},
                                                                       {{0  , 0  , 0  , 0  , 0  },
                                                                        {0  , 0  , 0  , 0  , 0  },
                                                                        {0  , 0  , 0  , 0  , 0  },
                                                                        {2  , 2  , 2.5, 3.5, 3.5}}};

        private static readonly double[] ds3s = { 1, 1.5, 2.5, 4, 6, 8 };
        private static readonly double[] ks3s = { -1.8, -1.8, -3, -5.4, -4.9 ,-4.9 };
        private static readonly double[,,] coeffs3s = new double[,,]  {{{-2  , -2  , -3  , -4  , -6  , -6  },
                                                                        { 0  ,  0  ,  0  ,  0  ,  0  ,  0  },
                                                                        { 1  ,  1  ,  1  ,  0  ,  0  ,  0  },
                                                                        { 0.4,  0.4,  0.2,  0.4,  0.3,  0.3}},
                                                                       {{-1  , -1  , -1.5, -2  , -3  , -3  },
                                                                        { 1.4,  1.4,  2.1,  2  ,  3  ,  3  },
                                                                        { 1  ,  1  ,  1  ,  1  ,  1  ,  1  },
                                                                        { 0  ,  0  ,  0.2,  0.4,  0.2,  0.2}},
                                                                       {{-1  , -1  , -1.5, -2  , -3  , -3  },
                                                                        {-1.4, -1.4, -2.1, -2  , -3  , -3  },
                                                                        { 1  ,  1  ,  1  ,  1  ,  1  ,  1  },
                                                                        { 0  ,  0  ,  0.2,  0.4,  0.2,  0.2}},
                                                                       {{ 0  ,  0  ,  0  ,  0  ,  0  ,  0  },
                                                                        { 0  ,  0  ,  0  ,  0  ,  0  ,  0  },
                                                                        { 0  ,  0  ,  0  ,  0  ,  0  ,  0  },
                                                                        { 2  ,  2  ,  1  ,  0.6,  0.6,  0.6}},
                                                                       {{ 1  ,  1  ,  1.5,  2  ,  3  ,  3  },
                                                                        { 0  ,  0  ,  0  ,  0  ,  0  ,  0  },
                                                                        { 1  ,  1  ,  1  ,  1  ,  1  ,  1  },
                                                                        {-1  , -1  , -0.6, -0.4, -0.3, -0.3}}};

        private static LinearSpline k0fInterp;
        private static LinearSpline[,] coeffs0fInterps;
        private static LinearSpline k0sInterp;
        private static LinearSpline[,] coeffs0sInterps;
        private static LinearSpline k3fInterp;
        private static LinearSpline[,] coeffs3fInterps;
        private static LinearSpline k3sInterp;
        private static LinearSpline[,] coeffs3sInterps;

        private const double tRatioThreshold = 1.4;
        private const double correction0Still = 0.2;

        public double RawMT { get; private set; }
        public double D { get; private set; }
        public double MT { get; private set; }


        public OsuMovement(OsuHitObject obj0, OsuHitObject obj1, OsuHitObject obj2, OsuHitObject obj3,
                           Vector<double> tapStrain, double clockRate)
        {
            var pos1 = Vector<double>.Build.Dense(new[] {(double)obj1.Position.X, (double)obj1.Position.Y});
            var pos2 = Vector<double>.Build.Dense(new[] {(double)obj2.Position.X, (double)obj2.Position.Y});
            var s12 = (pos2 - pos1) / (2 * obj2.Radius);
            double d12 = s12.L2Norm();
            double t12 = (obj2.StartTime - obj1.StartTime) / clockRate / 1000.0;
            double ip12 = FittsLaw.CalculateIP(d12, t12);

            RawMT = t12;

            var s01 = Vector<double>.Build.Dense(2);
            var s23 = Vector<double>.Build.Dense(2);
            double d01 = 0;
            double d23 = 0;
            double t01 = 0;
            double t23 = 0;
            bool obj1InTheMiddle = false;
            bool obj2InTheMiddle = false;


            // Correction #1 - The Previous Object
            // Estimate how obj0 affects the difficulty of hitting obj2
            double correction0 = 0;
            if (obj0 != null)
            {
                var pos0 = Vector<double>.Build.Dense(new[] {(double)obj0.Position.X, (double)obj0.Position.Y});
                s01 = (pos1 - pos0) / (2 * obj2.Radius);
                d01 = s01.L2Norm();
                t01 = (obj1.StartTime - obj0.StartTime) / clockRate / 1000.0;

                if (d12 != 0)
                {
                    double tRatio0 = t12 / t01;

                    if (tRatio0 > tRatioThreshold)
                    {
                        if (d01 == 0)
                        {
                            correction0 = correction0Still;
                        }
                        else
                        {
                            double cos012 = Math.Min(Math.Max(-s01.DotProduct(s12) / d01 / d12, -1), 1);
                            double correction0_moving = correction0MovingSpline.Interpolate(cos012);

                            double movingness = SpecialFunctions.Logistic(d01 * 2) * 2 - 1;
                            correction0 = (movingness * correction0_moving + (1 - movingness) * correction0Still) * 0.8;
                        }
                    }
                    else if (tRatio0 < 1 / tRatioThreshold)
                    {
                        if (d01 == 0)
                        {
                            correction0 = 0;
                        }
                        else
                        {
                            double cos012 = Math.Min(Math.Max(-s01.DotProduct(s12) / d01 / d12, -1), 1);
                            correction0 = (1 - cos012) * SpecialFunctions.Logistic((d01 * tRatio0 - 1.5) * 4) * 0.3;
                        }
                    }
                    else
                    {
                        obj1InTheMiddle = true;

                        var normalized_pos0 = -s01 / t01 * t12;
                        double x0 = normalized_pos0.DotProduct(s12) / d12;
                        double y0 = (normalized_pos0 - x0 * s12 / d12).L2Norm();

                        double correction0Flow = calcCorrection0Or3(d12, x0, y0, k0fInterp, coeffs0fInterps);
                        double correction0Snap = calcCorrection0Or3(d12, x0, y0, k0sInterp, coeffs0sInterps);

                        correction0 = Mean.PowerMean(correction0Flow, correction0Snap, -10);
                    }
                }
            }

            // Correction #2 - The Next Object
            // Estimate how obj3 affects the difficulty of hitting obj2
            double correction3 = 0;

            if (obj3 != null)
            {
                var pos3 = Vector<double>.Build.Dense(new[] { (double)obj3.Position.X, (double)obj3.Position.Y });
                s23 = (pos3 - pos2) / (2 * obj2.Radius);
                d23 = s23.L2Norm();
                t23 = (obj3.StartTime - obj2.StartTime) / clockRate / 1000.0;

                if (d12 != 0)
                {
                    double tRatio3 = t12 / t23;

                    if (tRatio3 > tRatioThreshold)
                    {
                        if (d23 == 0)
                        {
                            correction3 = 0;
                        }
                        else
                        {
                            double cos123 = Math.Min(Math.Max(-s12.DotProduct(s23) / d12 / d23, -1), 1);
                            double correction3_moving = correction0MovingSpline.Interpolate(cos123);

                            double movingness = SpecialFunctions.Logistic(d23 * 6 - 5) - SpecialFunctions.Logistic(-5);
                            correction3 = (movingness * correction3_moving) * 0.5;

                        }
                    }
                    else if (tRatio3 < 1 / tRatioThreshold)
                    {
                        if (d23 == 0)
                        {
                            correction3 = 0;
                        }
                        else
                        {
                            double cos123 = Math.Min(Math.Max(-s12.DotProduct(s23) / d12 / d23, -1), 1);
                            correction3 = (1 - cos123) * SpecialFunctions.Logistic((d23 * tRatio3 - 1.5) * 4) * 0.15;
                        }
                    }
                    else
                    {
                        obj2InTheMiddle = true;

                        var normalizedPos3 = s23 / t23 * t12;
                        double x3 = normalizedPos3.DotProduct(s12) / d12;
                        double y3 = (normalizedPos3 - x3 * s12 / d12).L2Norm();

                        double correction3Flow = calcCorrection0Or3(d12, x3, y3, k3fInterp, coeffs3fInterps);
                        double correction3Snap = calcCorrection0Or3(d12, x3, y3, k3sInterp, coeffs3sInterps);

                        correction3 = Math.Max(Mean.PowerMean(correction3Flow, correction3Snap, -10) - 0.1, 0) * 0.5;

                    }
                }
            }

            // Correction #3 - 4-object pattern
            // Estimate how the whole pattern consisting of obj0 to obj3 affects 
            // the difficulty of hitting obj2. This only takes effect when the pattern
            // is not so spaced (i.e. does not contain jumps)
            double patternCorrection = 0;

            if (obj1InTheMiddle && obj2InTheMiddle)
            {
                double gap = (s12 - s23 / 2 - s01 / 2).L2Norm();
                double spacing = Mean.PowerMean(d01, 1, 10) *
                                 Mean.PowerMean(d12, 1, 10) *
                                 Mean.PowerMean(d23, 1, 10);
                patternCorrection = (SpecialFunctions.Logistic((gap - 0.75) * 8) - SpecialFunctions.Logistic(-6)) *
                                     (1 - SpecialFunctions.Logistic((spacing - 3) * 4)) * 0.6;
            }

            // Correction #4 - Tap Strain
            // Estimate how tap strain affects difficulty
            double tapCorrection = 0;

            if (d12 > 0 && tapStrain != null)
            {
                tapCorrection = SpecialFunctions.Logistic((tapStrain.Sum() / tapStrain.Count / ip12 - 1) * 15) * 0.2;
            }

            // Correction #5 - Cheesing
            // The player might make the movement of obj1 -> obj2 easier by 
            // hitting obj1 early and obj2 late. Here we estimate the amount of 
            // cheesing and update MT accordingly.
            double timeEarly = 0;
            double timeLate = 0;

            if (d12 > 0)
            {
                double t01Reciprocal;
                double ip01;
                if (obj0 != null)
                {
                    t01Reciprocal = 1 / t01;
                    ip01 = FittsLaw.CalculateIP(d01, t01);
                }
                else
                {
                    t01Reciprocal = 0;
                    ip01 = 0;
                }
                timeEarly = SpecialFunctions.Logistic((ip01 / ip12 - 0.6) * (-15)) *
                            (1 / (1 / (t12 + 0.07) + t01Reciprocal)) * 0.15;

                double t23Reciprocal;
                double ip23;
                if (obj3 != null)
                {
                    t23Reciprocal = 1 / t23;
                    ip23 = FittsLaw.CalculateIP(d23, t23);
                }
                else
                {
                    t23Reciprocal = 0;
                    ip23 = 0;
                }
                timeLate = SpecialFunctions.Logistic((ip23 / ip12 - 0.6) * (-15)) *
                            (1 / (1 / (t12 + 0.07) + t23Reciprocal)) * 0.15;
            }

            // Correction #6 - Small circle bonus
            //double smallCircleBonus = 0;
            double smallCircleBonus = SpecialFunctions.Logistic((55 - 2 * obj2.Radius) / 2.0) * 0.1;

            // Apply the corrections
            double d12WithCorrection = d12 * (1 + smallCircleBonus) * (1 + correction0 + correction3 + patternCorrection) *
                                       (1 + tapCorrection);
            double t12WithCorrection = t12 + timeEarly + timeLate;

            this.D = d12WithCorrection;
            this.MT = t12WithCorrection;
        }

        public static void Initialize()
        {
            prepareInterp(ds0f, ks0f, coeffs0f, ref k0fInterp, ref coeffs0fInterps);
            prepareInterp(ds0s, ks0s, coeffs0s, ref k0sInterp, ref coeffs0sInterps);
            prepareInterp(ds3f, ks3f, coeffs3f, ref k3fInterp, ref coeffs3fInterps);
            prepareInterp(ds3s, ks3s, coeffs3s, ref k3sInterp, ref coeffs3sInterps);
        }


        private static void prepareInterp(double[] ds, double[] ks, double[,,] coeffs,
                                           ref LinearSpline kInterp, ref LinearSpline[,] coeffsInterps)
        {
            kInterp = LinearSpline.InterpolateSorted(ds, ks);

            coeffsInterps = new LinearSpline[coeffs.GetLength(0), numCoeffs];
            for (int i = 0; i < coeffs.GetLength(0); i++)
            {
                for (int j = 0; j < numCoeffs; j++)
                {
                    double[] coeff_ij = new double[coeffs.GetLength(2)];
                    for (int k = 0; k < coeffs.GetLength(2); k++)
                    {
                        coeff_ij[k] = coeffs[i, j, k];
                    }
                    coeffsInterps[i, j] = LinearSpline.InterpolateSorted(ds, coeff_ij);
                }
            }
        }

        private static double calcCorrection0Or3(double d, double x, double y,
                                                     LinearSpline kInterp, LinearSpline[,] coeffsInterps)
        {
            double correction_raw = kInterp.Interpolate(d);
            for (int i = 0; i < coeffsInterps.GetLength(0); i++)
            {
                double[] cs = new double[numCoeffs];
                for (int j = 0; j < numCoeffs; j++)
                {
                    cs[j] = coeffsInterps[i, j].Interpolate(d);
                }
                correction_raw += cs[3] * Math.Sqrt(Math.Pow((x - cs[0]), 2) +
                                                    Math.Pow((y - cs[1]), 2) +
                                                    cs[2]);
            }
            return SpecialFunctions.Logistic(correction_raw);
        }

    }
}