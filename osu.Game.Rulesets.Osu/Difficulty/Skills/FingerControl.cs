// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Rulesets.Osu.Objects;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MathNet.Numerics.Interpolation;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics;
using osu.Game.Rulesets.Osu.Difficulty.MathUtil;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    public class FingerControl
    {
        private const double strain_multiplier = 1.3;
        private const double repetition_weight = 0.7;
        private const double hard_strain_threshold = 1.1;

        private double identicalStrainTolerance;

        private List<double> noteHistory = new List<double>();
        private List<double> noteHistoryVirtual = new List<double>();
        private LinearSpline prevFractionSpline = LinearSpline.InterpolateSorted(
            new double[] { 1.0, 1.5 , 2.0, 3.0, 4.0 },
            new double[] { 0.5, 1.5, 0.9, 0.25, 0.0 } 
        );
        private LinearSpline nextFractionSpline = LinearSpline.InterpolateSorted(
            new double[] { 1.0 , 7.0/6.0, 1.5 , 1.75, 2.0, 3.0, 4.0 },
            new double[] { 0.05, 1.0    , 0.75, 1.0 , 0.5, 0.0, 0.0 }
        );

        private double compareStrains(double strain1, double strain2, LinearSpline fractionSpline)
        {
            if (strain1 == 0 || strain2 == 0)
                return 1;
            double fraction = Math.Max(strain1 / strain2, strain2 / strain1);
            var res = fractionSpline.Interpolate(fraction);
            if (res < 0) // spline can sometimes dip below 0 which breaks everything 
                res = 0;
            return res;
        }

        private (double, bool) checkAnomaly(List<double> refNoteHistory)
        {
            List<double> uniqueStrains = new List<double>();

            // Get all unique straintimes, ignore current object
            for (int i = 0; i < refNoteHistory.Count-1; i++)
            {
                bool exists = false;
                for (int j = 0; j < uniqueStrains.Count; j++)
                {
                    if (Math.Abs(uniqueStrains[j] - refNoteHistory[i]) < identicalStrainTolerance)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                    uniqueStrains.Add(refNoteHistory[i]);
            }

            // Check if current strain exists previously, and find the ratio closest to 1
            bool unique = true;
            double strainTime = refNoteHistory[refNoteHistory.Count - 1];
            double strainRatio = 0;
            double closestStrain = 0;
            for (int j = 0; j < uniqueStrains.Count; j++)
            {
                if (
                    Math.Abs(strainTime - uniqueStrains[j]) < identicalStrainTolerance ||
                    Math.Abs(strainTime * 2 - uniqueStrains[j]) < identicalStrainTolerance ||
                    Math.Abs(strainTime / 2 - uniqueStrains[j]) < identicalStrainTolerance
                )
                {
                    unique = false;
                    break;
                }
                
                double strainRatioTest = Math.Max(strainTime, uniqueStrains[j]) / Math.Min(strainTime, uniqueStrains[j]);
                if (strainRatioTest - 1 < strainRatio - 1)
                {
                    strainRatio = strainRatioTest;
                    closestStrain = uniqueStrains[j];
                }
            }
            
            if (!unique)
                return ((double)uniqueStrains.Count, true);

            return ((double)uniqueStrains.Count, false); 
        }

        private double calculateExpectancy(List<double> refNoteHistory)
        {
            // See how many unique strains there are, and get a nerfed version of the straintime
            (double anomalyVal, bool exists) = checkAnomaly(refNoteHistory);

            refNoteHistory.Reverse();

            // Get reference pattern
            List<double> pattern = new List<double>();
            double strainTime = refNoteHistory[0];
            for (int i = 1; i < refNoteHistory.Count; i++)
            {
                if (Math.Abs(refNoteHistory[i] - strainTime) > identicalStrainTolerance)
                {
                    pattern = refNoteHistory.Take(i+1).ToList();
                    break;
                }
            }
                
            // If pattern length is 0, then that means that there are no changing straintimes
            if (pattern.Count == 0)
            {
                refNoteHistory.Reverse();
                return 1;
            }
            
            // If longer than half of the refNoteHistory length then just look at how often
            if (pattern.Count > refNoteHistory.Count / 2.0)
            {
                refNoteHistory.Reverse();
                return (double)pattern.Count / (double)refNoteHistory.Count;
            }
            
            int minSize = pattern.Count;
            int maxSize = pattern.Count;
            double maxRepetition = 0;
            for (int k = minSize; k < refNoteHistory.Count / 2; k++) // See how many times each pattern from reference size to half the main list repeats, get the maximum value
            {
                pattern = refNoteHistory.Take(k).ToList();
                
                int patternInstance = 0;
                int reversePatternInstance = 0;
                for (int i = pattern.Count; i < refNoteHistory.Count; i++)
                {
                    List<double> patternCompare = refNoteHistory.Skip(i).Take(pattern.Count).ToList();

                    if (patternCompare.Count != pattern.Count)
                        break;
                    
                    bool samePattern = true;
                    for (int j = 0; j < pattern.Count; j++)
                    {
                        if (Math.Abs(pattern[j] - patternCompare[j]) > identicalStrainTolerance)
                        {
                            samePattern = false;
                            break;
                        }
                    }
                    
                    if (samePattern)
                        patternInstance++;
                    else
                    {
                        patternCompare.Reverse();
                        bool reverseSamePattern = true;
                        for (int j = 0; j < pattern.Count; j++)
                        {
                            if (Math.Abs(pattern[j] - patternCompare[j]) > identicalStrainTolerance)
                            {
                                reverseSamePattern = false;
                                break;
                            }
                        }

                        if (reverseSamePattern)
                            reversePatternInstance++;
                    }
                }

                int possibleInstances = (int)Math.Ceiling((refNoteHistory.Count - pattern.Count - (pattern.Count - 1)) / 2.0);
                double ratio = Math.Min(1, (double)Math.Max(patternInstance, reversePatternInstance) / (double)possibleInstances);
                // There are cases where it's possible the counter makes this ratio more than 1 due to the checking method being if notes
                // fall within a range of 16 ms. As a result a max is required to cap at 1.

                if (ratio > maxRepetition)
                {
                    maxRepetition = ratio;
                    maxSize = pattern.Count;
                }

                // No need to loop anymore since 1 is the highest possible value
                if (maxRepetition == 1)
                    break;
            }

            // Punish patterns that are longer more, pattern size of 2 gets 0 value while pattern size 8+ get 1
            double patternLength = Math.Pow(Math.Sin(Math.PI * (Math.Min(maxSize, 8) - 2) / 12), 2.0);

            var fractionMultiplier = compareStrains(strainTime, refNoteHistory[1], prevFractionSpline);

            refNoteHistory.Reverse();
            double repetitionVal = Math.Min(1.0, Math.Sqrt(maxRepetition) + patternLength);

            // Check if note even existed before, anomalyVal is high and repetitionVal is low
            if (!exists)
            {
                // A count of 1 gets 1, a count of 8+ gets 0
                double uniqueScale = Math.Pow(
                    Math.Pow(- Math.Min(7.0, anomalyVal - 1.0) / 7.0, 5.0) + 1.0,
                2.0);
                repetitionVal = Math.Max(Math.Min(1, repetitionVal + uniqueScale - fractionMultiplier), 0.0);
            }

            return repetitionVal;
        }
        private double calculateDowntime(double strainTime, List<double> refNoteHistory)
        {
            int longNoteCount = 0;
            for (int i = 0; i < refNoteHistory.Count; i++)
            {
                if (refNoteHistory[i] > strainTime * 2 - identicalStrainTolerance)
                    longNoteCount++;
            }

            double longNoteFraction = Math.Max(0.5, (double)longNoteCount / (double)refNoteHistory.Count);

            return Math.Pow(Math.Sin(Math.PI * (longNoteFraction - 1.0)), 2.0);
        }

        private double strainAppearance(double strainTime, List<double> refNoteHistory)
        {
            int strainApperance = 0;
            for (int i = 0; i < refNoteHistory.Count; i++)
            {
                if (Math.Abs(refNoteHistory[i] - strainTime) < identicalStrainTolerance)
                    strainApperance++;
            }

            double strainAppearanceFraction = Math.Max(0.5, (double)strainApperance / (double)refNoteHistory.Count);

            return Math.Pow(Math.Sin(Math.PI * (strainAppearanceFraction - 1.0)), 2.0);
        }

        private double strainValueOf(OsuHitObject current, double strainTime, double virtualStrainTime, double prevStrainTime, double prevVirtualStrainTime, Vector<double> tapStrain)
        {
            if (current is Spinner)
                return 0;

            noteHistory.Add(strainTime);
            noteHistoryVirtual.Add(virtualStrainTime);

            while (noteHistory.Sum() > 4 || noteHistory.Count > 32)
                noteHistory.RemoveAt(0);

            while (noteHistory.Count < noteHistoryVirtual.Count)
                noteHistoryVirtual.RemoveAt(0);

            double repetitionVal = 0;
            double downtimeScale = 1;
            double appearanceScale = 1;
            double uniqueScale = 1;
            if (noteHistory.Count > 2)
            {
                double repetition = 1.0 - calculateExpectancy(noteHistory);
                double virtualRepetition = 1.0 - calculateExpectancy(noteHistoryVirtual);
                double repetitionExponent = Math.Min(2.0, 48.75 * Math.Min(strainTime, virtualStrainTime) - 1.65625);
                repetitionVal = Math.Pow(Math.Min(repetition, virtualRepetition), repetitionExponent);

                // When there is major downtime / not much actually happening
                downtimeScale = Math.Min(calculateDowntime(strainTime, noteHistory), calculateDowntime(virtualStrainTime, noteHistoryVirtual));
                
                // When there's a huge stream before a pack of doubles / triples
                appearanceScale = Math.Min(strainAppearance(strainTime, noteHistory), strainAppearance(virtualStrainTime, noteHistoryVirtual));

                // When there's a ton of unique strains that means that it's a wild BPM area
                (double uniqueVal, _) = checkAnomaly(noteHistory);
                (double virtualUniqueVal, _) = checkAnomaly(noteHistoryVirtual);
                uniqueScale = 1.0 + Math.Pow((Math.Min(uniqueVal, virtualUniqueVal) - 1.0) / 11.0, 4.0);
            }

            double multiplier = Math.Min(
                Math.Min(compareStrains(strainTime, prevStrainTime, prevFractionSpline), compareStrains(strainTime, prevVirtualStrainTime, prevFractionSpline)),
                Math.Min(compareStrains(virtualStrainTime, prevStrainTime, prevFractionSpline), compareStrains(virtualStrainTime, prevVirtualStrainTime, prevFractionSpline))
            );
            if (current is Slider)
                multiplier /= 2;

            var tapCorrection = 1.0;
            if (tapStrain != null)
            {
                var mean = Mean.PowerMean(tapStrain, 2);
                tapCorrection = 1 + SpecialFunctions.Logistic((mean - 10) / 2) * 0.20;
            }

            return repetitionVal * multiplier * downtimeScale * appearanceScale * uniqueScale * tapCorrection / strainTime;
        }

        public FingerAttributes CalculateFingerControlDiff(List<OsuHitObject> hitObjects, double clockRate, List<Vector<double>> tapStrainHistory, double greatHitWindow)
        {
            if (hitObjects.Count == 0)
                return new FingerAttributes();

            double prevTime = hitObjects[0].StartTime / 1000.0;
            double prevStrainTime = 0;
            double prevVirtualStrainTime = 0;
            double currStrain = 0;
            List<double> strainHistory = new List<double> { 0 };
            List<double> specificStrainHistory = new List<double> { 0 };
            var sw = new StringWriter();
            sw.WriteLine($"{hitObjects[1].StartTime / 1000.0} 0 0");

            var hardStrainsAmount = 0;

            identicalStrainTolerance = greatHitWindow / 1000;

            // calculate strain value for each hit object
            for (int i = 1; i < hitObjects.Count; i++)
            {
                double currTime = hitObjects[i].StartTime / 1000.0;
                double deltaTime = (currTime - prevTime) / clockRate;

                double strainTime = Math.Max(deltaTime, 0.035);
                double virtualStrainTime = strainTime;
                double strainDecayBase = Math.Pow(0.75, 1 / Math.Min(strainTime, 0.2));

                currStrain *= Math.Pow(strainDecayBase, deltaTime);

                strainHistory.Add(currStrain);

                if (hitObjects[i-1] is Slider prevSlider)
                    virtualStrainTime = Math.Max((currTime - prevSlider.EndTime / 1000.0) / clockRate, 0.035);

                double strain = strain_multiplier * strainValueOf(hitObjects[i], strainTime, virtualStrainTime, prevStrainTime, prevVirtualStrainTime, tapStrainHistory[i]);
                
                if (i < hitObjects.Count - 1)
                {
                    double nextTime = hitObjects[i+1].StartTime / 1000.0;
                    double nextStrainTime = Math.Max((nextTime - currTime) / clockRate, 0.035);
                    double nextVirtualStrainTime = 0;
                    if (hitObjects[i] is Slider currSlider)
                        nextVirtualStrainTime = Math.Max((nextTime - currSlider.EndTime / 1000.0) / clockRate, 0.035);

                    double multiplier = Math.Min(
                        Math.Min(compareStrains(strainTime, nextStrainTime, nextFractionSpline), compareStrains(strainTime, nextVirtualStrainTime, nextFractionSpline)),
                        Math.Min(compareStrains(virtualStrainTime, nextStrainTime, nextFractionSpline), compareStrains(virtualStrainTime, nextVirtualStrainTime, nextFractionSpline))
                    );
                    if (hitObjects[i+1] is Slider)
                        multiplier /= 2;

                    strain *= multiplier;
                }

                specificStrainHistory.Add(strain);
                
                currStrain += strain;

                if (currStrain > hard_strain_threshold)
                    hardStrainsAmount++;

                sw.WriteLine($"{currTime} {currStrain} {strain}");

                prevTime = currTime;
                
                if (deltaTime > 0.035)
                {
                    prevStrainTime = strainTime;
                    prevVirtualStrainTime = virtualStrainTime;
                }
            }

            string graphText = sw.ToString();
            sw.Dispose();

            var strainHistoryArray = strainHistory.ToArray();

            Array.Sort(strainHistoryArray);
            Array.Reverse(strainHistoryArray);

            double diff = 0;
            double k = 0.98;

            for (int i = 0; i < hitObjects.Count; i++)
                diff += strainHistoryArray[i] * Math.Pow(k, i);

            return new FingerAttributes
            {
                FingerDifficulty = diff * (1 - k),
                StrainHistory = specificStrainHistory,
                HardStrainAmount = hardStrainsAmount,
                Graph = graphText
            };
        }
    }
}
