﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using MathNet.Numerics;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Interpolation;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Difficulty.MathUtil;

namespace osu.Game.Rulesets.Osu.Difficulty.Preprocessing
{
    public class OsuMovement
    {
        private static readonly LinearSpline correction_neg2_moving_spline = LinearSpline.InterpolateSorted(
            new[] { -1.0, 1.0 },
            new[] { 1.1, 0 });

        private const double t_ratio_threshold = 1.4;
        private const double correction_neg2_still = 0;

        /// <summary>
        /// Uncorrected movement time
        /// </summary>
        public double RawMovementTime { get; private set; }

        /// <summary>
        /// Corrected distance between objects
        /// </summary>
        public double Distance { get; private set; }

        /// <summary>
        /// Corrected movement time
        /// </summary>
        public double MovementTime { get; private set; }

        /// <summary>
        /// Movement index of performance
        /// </summary>
        public double IndexOfPerformance { get; private set; }

        /// <summary>
        /// Cheesablility of the movement
        /// </summary>
        public double Cheesablility { get; private set; }

        /// <summary>
        /// Cheesable time ratio
        /// </summary>
        public double CheesableRatio { get; private set; }

        /// <summary>
        /// Object start time
        /// </summary>
        public double Time { get; private set; }

        /// <summary>
        /// Movement ends on a slider
        /// </summary>
        public bool EndsOnSlider { get; private set; }

        /// <summary>
        /// Extracts movement (only for the first object in a beatmap).
        /// </summary>
        public static List<OsuMovement> ExtractMovement(OsuHitObject obj)
        {
            var movement = GetEmptyMovement(obj.StartTime / 1000.0);

            var movementWithNested = new List<OsuMovement> { movement };
            // add zero difficulty movements corresponding to slider ticks/slider ends so combo is reflected properly
            int extraNestedCount = obj.NestedHitObjects.Count - 1;

            for (int i = 0; i < extraNestedCount; i++)
            {
                movementWithNested.Add(GetEmptyMovement(movement.Time));
            }

            return movementWithNested;
        }

        /// <summary>
        /// Calculates the movement time, effective distance and other details for the movement from objPrev to objCurr.
        /// </summary>
        /// <param name="objNeg4">Object that that was three objects before current</param>
        /// <param name="objNeg2">Prevprev object</param>
        /// <param name="objPrev">Previous object</param>
        /// <param name="objCurr">Current object</param>
        /// <param name="objNext">Next object</param>
        /// <param name="tapStrain">Current object tap strain</param>
        /// <param name="noteDensity">Current object visual note density</param>
        /// <param name="clockRate">Clock rate</param>
        /// <param name="hidden">Are we calculating hidden mod?</param>
        /// <returns>List of movements related to current object</returns>
        public static List<OsuMovement> ExtractMovement(OsuHitObject objNeg2, OsuHitObject objPrev, OsuHitObject objCurr, OsuHitObject objNext,
                                                        Vector<double> tapStrain, double clockRate,
                                                        bool hidden = false, double noteDensity = 0, OsuHitObject objNeg4 = null)
        {
            var movement = new OsuMovement();

            double tPrevCurr = (objCurr.StartTime - objPrev.StartTime) / clockRate / 1000.0;
            movement.RawMovementTime = tPrevCurr;
            movement.Time = objCurr.StartTime / 1000.0;

            if (objCurr is Spinner || objPrev is Spinner)
            {
                movement.IndexOfPerformance = 0;
                movement.Distance = 0;
                movement.MovementTime = 1;
                movement.Cheesablility = 0;
                movement.CheesableRatio = 0;
                return new List<OsuMovement> { movement };
            }

            if (objNeg2 is Spinner)
                objNeg2 = null;

            if (objNext is Spinner)
                objNext = null;

            if (objCurr is Slider)
                movement.EndsOnSlider = true;

            // calculate basic info (position, displacement, distance...)
            // explanation of abbreviations:
            // posx: position of obj x
            // sxy : displacement (normalized) from obj x to obj y
            // txy : time difference of obj x and obj y
            // dxy : distance (normalized) from obj x to obj y
            // ipxy: index of performance of the movement from obj x to obj y
            var posPrev = Vector<double>.Build.Dense(new[] { objPrev.StackedPosition.X, (double)objPrev.StackedPosition.Y });
            var posCurr = Vector<double>.Build.Dense(new[] { objCurr.StackedPosition.X, (double)objCurr.StackedPosition.Y });
            var sPrevCurr = (posCurr - posPrev) / (2 * objCurr.Radius);
            double dPrevCurr = sPrevCurr.L2Norm();
            double ipPrevCurr = FittsLaw.CalculateIp(dPrevCurr, tPrevCurr);

            movement.IndexOfPerformance = ipPrevCurr;

            var posNeg2 = Vector<double>.Build.Dense(2);
            var posNext = Vector<double>.Build.Dense(2);
            var sNeg2Prev = Vector<double>.Build.Dense(2);
            var sCurrNext = Vector<double>.Build.Dense(2);
            double dNeg2Prev = 0;
            double dNeg2Curr = 0;
            double dCurrNext = 0;
            double tNeg2Prev = 0;
            double tCurrNext = 0;

            double flowinessNeg2PrevCurr = 0;
            double flowinessPrevCurrNext = 0;
            bool objPrevTemporallyInTheMiddle = false;
            bool objCurrTemporallyInTheMiddle = false;

            double dNeg4Curr = 0;

            if (objNeg4 != null)
            {
                var posNeg4 = Vector<double>.Build.Dense(new[] { objNeg4.StackedPosition.X, (double)objNeg4.StackedPosition.Y });
                dNeg4Curr = ((posCurr - posNeg4) / (2 * objCurr.Radius)).L2Norm();
            }

            if (objNeg2 != null)
            {
                posNeg2 = Vector<double>.Build.Dense(new[] { objNeg2.StackedPosition.X, (double)objNeg2.StackedPosition.Y });
                sNeg2Prev = (posPrev - posNeg2) / (2 * objCurr.Radius);
                dNeg2Prev = sNeg2Prev.L2Norm();
                tNeg2Prev = (objPrev.StartTime - objNeg2.StartTime) / clockRate / 1000.0;
                dNeg2Curr = ((posCurr - posNeg2) / (2 * objCurr.Radius)).L2Norm();
            }

            if (objNext != null)
            {
                posNext = Vector<double>.Build.Dense(new[] { objNext.StackedPosition.X, (double)objNext.StackedPosition.Y });
                sCurrNext = (posNext - posCurr) / (2 * objCurr.Radius);
                dCurrNext = sCurrNext.L2Norm();
                tCurrNext = (objNext.StartTime - objCurr.StartTime) / clockRate / 1000.0;
            }

            // Correction #1 - The Previous Object
            // Estimate how objNeg2 affects the difficulty of hitting objCurr
            double correctionNeg2 = 0;

            if (objNeg2 != null && dPrevCurr != 0)
            {
                double tRatioNeg2 = tPrevCurr / tNeg2Prev;
                double cosNeg2PrevCurr = Math.Min(Math.Max(-sNeg2Prev.DotProduct(sPrevCurr) / dNeg2Prev / dPrevCurr, -1), 1);

                if (tRatioNeg2 > t_ratio_threshold)
                {
                    if (dNeg2Prev == 0)
                    {
                        correctionNeg2 = correction_neg2_still;
                    }
                    else
                    {
                        double correctionNeg2Moving = correction_neg2_moving_spline.Interpolate(cosNeg2PrevCurr);

                        double movingness = SpecialFunctions.Logistic(dNeg2Prev * 6 - 5) - SpecialFunctions.Logistic(-5);
                        correctionNeg2 = (movingness * correctionNeg2Moving + (1 - movingness) * correction_neg2_still) * 1.5;
                    }
                }
                else if (tRatioNeg2 < 1 / t_ratio_threshold)
                {
                    if (dNeg2Prev == 0)
                    {
                        correctionNeg2 = 0;
                    }
                    else
                    {
                        correctionNeg2 = (1 - cosNeg2PrevCurr) * SpecialFunctions.Logistic((dNeg2Prev * tRatioNeg2 - 1.5) * 4) * 0.3;
                    }
                }
                else
                {
                    objPrevTemporallyInTheMiddle = true;

                    var normalizedPosNeg2 = -sNeg2Prev / tNeg2Prev * tPrevCurr;
                    double xNeg2 = normalizedPosNeg2.DotProduct(sPrevCurr) / dPrevCurr;
                    double yNeg2 = (normalizedPosNeg2 - xNeg2 * sPrevCurr / dPrevCurr).L2Norm();

                    double correctionNeg2Flow = AngleCorrection.FLOW_NEG2.Evaluate(dPrevCurr, xNeg2, yNeg2);
                    double correctionNeg2Snap = AngleCorrection.SNAP_NEG2.Evaluate(dPrevCurr, xNeg2, yNeg2);
                    double correctionNeg2Stop = calcCorrection0Stop(dPrevCurr, xNeg2, yNeg2);

                    flowinessNeg2PrevCurr = SpecialFunctions.Logistic((correctionNeg2Snap - correctionNeg2Flow - 0.05) * 20);

                    correctionNeg2 = Mean.PowerMean(new[] { correctionNeg2Flow, correctionNeg2Snap, correctionNeg2Stop }, -10) * 1.3;
                }
            }

            // Correction #2 - The Next Object
            // Estimate how objNext affects the difficulty of hitting objCurr
            double correctionNext = 0;

            if (objNext != null && dPrevCurr != 0)
            {
                double tRatioNext = tPrevCurr / tCurrNext;
                double cosPrevCurrNext = Math.Min(Math.Max(-sPrevCurr.DotProduct(sCurrNext) / dPrevCurr / dCurrNext, -1), 1);

                if (tRatioNext > t_ratio_threshold)
                {
                    if (dCurrNext == 0)
                    {
                        correctionNext = 0;
                    }
                    else
                    {
                        double correctionNextMoving = correction_neg2_moving_spline.Interpolate(cosPrevCurrNext);

                        double movingness = SpecialFunctions.Logistic(dCurrNext * 6 - 5) - SpecialFunctions.Logistic(-5);
                        correctionNext = movingness * correctionNextMoving * 0.5;
                    }
                }
                else if (tRatioNext < 1 / t_ratio_threshold)
                {
                    if (dCurrNext == 0)
                    {
                        correctionNext = 0;
                    }
                    else
                    {
                        correctionNext = (1 - cosPrevCurrNext) * SpecialFunctions.Logistic((dCurrNext * tRatioNext - 1.5) * 4) * 0.15;
                    }
                }
                else
                {
                    objCurrTemporallyInTheMiddle = true;

                    var normalizedPosNext = sCurrNext / tCurrNext * tPrevCurr;
                    double xNext = normalizedPosNext.DotProduct(sPrevCurr) / dPrevCurr;
                    double yNext = (normalizedPosNext - xNext * sPrevCurr / dPrevCurr).L2Norm();

                    double correctionNextFlow = AngleCorrection.FLOW_NEXT.Evaluate(dPrevCurr, xNext, yNext);
                    double correctionNextSnap = AngleCorrection.SNAP_NEXT.Evaluate(dPrevCurr, xNext, yNext);

                    flowinessPrevCurrNext = SpecialFunctions.Logistic((correctionNextSnap - correctionNextFlow - 0.05) * 20);

                    correctionNext = Math.Max(Mean.PowerMean(correctionNextFlow, correctionNextSnap, -10) - 0.1, 0) * 0.5;
                }
            }

            // Correction #3 - 4-object pattern
            // Estimate how the whole pattern consisting of objNeg2 to objNext affects
            // the difficulty of hitting objCurr. This only takes effect when the pattern
            // is not so spaced (i.e. does not contain jumps)
            double patternCorrection = 0;

            if (objPrevTemporallyInTheMiddle && objCurrTemporallyInTheMiddle)
            {
                double gap = (sPrevCurr - sCurrNext / 2 - sNeg2Prev / 2).L2Norm() / (dPrevCurr + 0.1);
                patternCorrection = (SpecialFunctions.Logistic((gap - 1) * 8) - SpecialFunctions.Logistic(-6)) *
                                    SpecialFunctions.Logistic((dNeg2Prev - 0.7) * 10) * SpecialFunctions.Logistic((dCurrNext - 0.7) * 10) *
                                    Mean.PowerMean(flowinessNeg2PrevCurr, flowinessPrevCurrNext, 2) * 0.6;
            }

            // Correction #4 - Tap Strain
            // Estimate how tap strain affects difficulty
            double tapCorrection = 0;

            if (dPrevCurr > 0 && tapStrain != null)
            {
                tapCorrection = SpecialFunctions.Logistic((Mean.PowerMean(tapStrain, 2) / ipPrevCurr - 1.34) / 0.1) * 0.15;
            }

            // Correction #5 - Cheesing
            // The player might make the movement of objPrev -> objCurr easier by
            // hitting objPrev early and objCurr late. Here we estimate the amount of
            // cheesing and update MT accordingly.
            double timeEarly = 0;
            double timeLate = 0;
            double cheesabilityEarly = 0;
            double cheesabilityLate = 0;

            if (dPrevCurr > 0)
            {
                double tNeg2PrevReciprocal;
                double ipNeg2Prev;

                if (objNeg2 != null)
                {
                    tNeg2PrevReciprocal = 1 / (tNeg2Prev + 1e-10);
                    ipNeg2Prev = FittsLaw.CalculateIp(dNeg2Prev, tNeg2Prev);
                }
                else
                {
                    tNeg2PrevReciprocal = 0;
                    ipNeg2Prev = 0;
                }

                cheesabilityEarly = SpecialFunctions.Logistic((ipNeg2Prev / ipPrevCurr - 0.6) * (-15)) * 0.5;
                timeEarly = cheesabilityEarly * (1 / (1 / (tPrevCurr + 0.07) + tNeg2PrevReciprocal));

                double tCurrNextReciprocal;
                double ipCurrNext;

                if (objNext != null)
                {
                    tCurrNextReciprocal = 1 / (tCurrNext + 1e-10);
                    ipCurrNext = FittsLaw.CalculateIp(dCurrNext, tCurrNext);
                }
                else
                {
                    tCurrNextReciprocal = 0;
                    ipCurrNext = 0;
                }

                cheesabilityLate = SpecialFunctions.Logistic((ipCurrNext / ipPrevCurr - 0.6) * (-15)) * 0.5;
                timeLate = cheesabilityLate * (1 / (1 / (tPrevCurr + 0.07) + tCurrNextReciprocal));
            }

            // Correction #6 - High bpm jump buff (alt buff)
            double effectiveBpm = 30 / (tPrevCurr + 1e-10);
            double highBpmJumpBuff = SpecialFunctions.Logistic((effectiveBpm - 354) / 16) *
                                     SpecialFunctions.Logistic((dPrevCurr - 1.9) / 0.15) * 0.23;

            // Correction #7 - Small circle bonus
            double smallCircleBonus = ((SpecialFunctions.Logistic((55 - 2 * objCurr.Radius) / 3.0) * 0.3) +
                                      (Math.Pow(24.5 - Math.Min(objCurr.Radius, 24.5), 1.4) * 0.01315)) *
                                      Math.Max(SpecialFunctions.Logistic((dPrevCurr - 0.5) / 0.1), 0.25);

            // Correction #8 - Stacked notes nerf
            double dPrevCurrStackedNerf = Math.Max(0, Math.Min(dPrevCurr, Math.Min(1.2 * dPrevCurr - 0.185, 1.4 * dPrevCurr - 0.32)));

            // Correction #9 - Slow small jump nerf
            double smallJumpNerfFactor = 1 - 0.17 * Math.Exp(-Math.Pow((dPrevCurr - 2.2) / 0.7, 2)) *
                SpecialFunctions.Logistic((255 - effectiveBpm) / 10);

            // Correction #10 - Slow big jump buff
            double bigJumpBuffFactor = 1 + 0.15 * SpecialFunctions.Logistic((dPrevCurr - 6) / 0.5) *
                SpecialFunctions.Logistic((210 - effectiveBpm) / 8);

            // Correction #11 - Hidden Mod
            double correctionHidden = 0;

            if (hidden)
            {
                correctionHidden = 0.05 + 0.008 * noteDensity;
            }

            // Correction #12 - Stacked wiggle fix
            if (objNeg2 != null && objNext != null)
            {
                var dPrevNext = ((posNext - posPrev) / (2 * objCurr.Radius)).L2Norm();
                var dNeg2Next = ((posNext - posNeg2) / (2 * objCurr.Radius)).L2Norm();

                if (dNeg2Prev < 1 && dNeg2Curr < 1 && dNeg2Next < 1 && dPrevCurr < 1 && dPrevNext < 1 && dCurrNext < 1)
                {
                    correctionNeg2 = 0;
                    correctionNext = 0;
                    patternCorrection = 0;
                    tapCorrection = 0;
                }
            }

            // Correction #13 - Repetitive jump nerf
            // Nerf big jumps where objNeg2 and objCurr are close or where objNeg4 and objCurr are close
            double jumpOverlapCorrection = 1 - (Math.Max(0.15 - 0.1 * dNeg2Curr, 0) + Math.Max(0.1125 - 0.075 * dNeg4Curr, 0)) *
                SpecialFunctions.Logistic((dPrevCurr - 3.3) / 0.25);

            // Correction #14 - Sudden distance increase buff
            double distanceIncreaseBuff = 1;

            if (objNeg2 != null)
            {
                double dNeg2PrevOverlapNerf = Math.Min(1, Math.Pow(dNeg2Prev, 3));
                double timeDifferenceNerf = Math.Exp(-4 * Math.Pow(1 - Math.Max(tPrevCurr / (tNeg2Prev + 1e-10), tNeg2Prev / (tPrevCurr + 1e-10)), 2));
                double distanceRatio = dPrevCurr / Math.Max(1, dNeg2Prev);
                double bpmScaling = Math.Max(1, -16 * tPrevCurr + 3.4);
                distanceIncreaseBuff = 1 + 0.225 * bpmScaling * timeDifferenceNerf * dNeg2PrevOverlapNerf * Math.Max(0, distanceRatio - 2);
            }

            var streamJumpBuff = 0.0;
            // Correction #15 - Buff sudden single distance increase in 4-object flowy patterns (jumps in streams)
            if (obj1InTheMiddle && obj2InTheMiddle)
            {
                var streamSpacingMean = Mean.PowerMean(d01, d23, 1.5);

                if (d12 > streamSpacingMean)
                {
                    var distanceDifference = d12 - streamSpacingMean;
                    streamJumpBuff = SpecialFunctions.Logistic((distanceDifference - 0.55) / 0.08) *
                                     (1.0 - Math.Exp(-Mean.PowerMean(flowiness012, flowiness123, 8) * 3.5)) *
                                     SpecialFunctions.Logistic((streamSpacingMean - 0.5) / 0.07) * 0.42;
                }
            }

            // Apply the corrections
            double dPrevCurrWithCorrection = dPrevCurrStackedNerf * (1 + smallCircleBonus) * (1 + correctionNeg2 + correctionNext + patternCorrection) *
                                       (1 + highBpmJumpBuff) * (1 + tapCorrection) * smallJumpNerfFactor * bigJumpBuffFactor * (1 + correctionHidden) *
                                       jumpOverlapCorrection * distanceIncreaseBuff * (1 + streamJumpBuff);

            movement.Distance = dPrevCurrWithCorrection;
            movement.MovementTime = tPrevCurr;
            movement.Cheesablility = cheesabilityEarly + cheesabilityLate;
            movement.CheesableRatio = (timeEarly + timeLate) / (tPrevCurr + 1e-10);

            var movementWithNested = new List<OsuMovement> { movement };

            // add zero difficulty movements corresponding to slider ticks/slider ends so combo is reflected properly
            int extraNestedCount = objCurr.NestedHitObjects.Count - 1;

            for (int i = 0; i < extraNestedCount; i++)
            {
                movementWithNested.Add(GetEmptyMovement(movement.Time));
            }

            return movementWithNested;
        }

        public static OsuMovement GetEmptyMovement(double time)
        {
            return new OsuMovement
            {
                Distance = 0,
                MovementTime = 1,
                CheesableRatio = 0,
                Cheesablility = 0,
                RawMovementTime = 0,
                IndexOfPerformance = 0,
                Time = time
            };
        }

        private static double calcCorrection0Stop(double d, double x, double y)
        {
            return SpecialFunctions.Logistic(10 * Math.Sqrt(x * x + y * y + 1) - 12);
        }
    }
}
