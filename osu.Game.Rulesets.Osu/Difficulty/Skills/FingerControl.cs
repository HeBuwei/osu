using osu.Game.Rulesets.Osu.Objects;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MathNet.Numerics.Interpolation;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    class FingerControl
    {
        private const double strainMultiplier = 1.0;
        private const double repetitionWeight = 0.7;
        private static List<double> noteHistory = new List<double>();
        private static List<double> noteHistoryVirtual = new List<double>();
        private static LinearSpline prevFractionSpline = LinearSpline.InterpolateSorted(
            new double[] { 1.0, 1.5, 2.0, 3.0 },
            new double[] { 0.5, 1.5, 1  , 1   }
        );
        private static LinearSpline nextFractionSpline = LinearSpline.InterpolateSorted(
            new double[] { 1.0 , 7.0/6.0, 1.75, 2.0 , 3.0 , 4.0 },
            new double[] { 0.05, 1      , 1   , 0.5 , 0   , 0   }
        );

        private static double CompareStrains(double strain1, double strain2, LinearSpline fractionSpline)
        {
            if (strain1 == 0 || strain2 == 0)
                return 1;
            double fraction = Math.Max(strain1 / strain2, strain2 / strain1);
            return fractionSpline.Interpolate(fraction);
        }

        private static double calculateExpectancy(OsuHitObject current, List<double> refNoteHistory)
        {
            refNoteHistory.Reverse();

            // Get reference pattern
            List<double> pattern = new List<double>();
            double strainTime = refNoteHistory[0];
            for (int i = 1; i < refNoteHistory.Count; i++)
            {
                if (Math.Abs(refNoteHistory[i] - strainTime) > 0.008)
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

            // Punish patterns that are longer more, pattern size of 2 gets 0 value while pattern size 8+ get 1
            double patternLength = Math.Pow(Math.Sin(Math.PI * (Math.Min(pattern.Count, 8) - 2) / 12), 2.0);
            
            // See how many times this pattern repeats
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
                    if (Math.Abs(pattern[j] - patternCompare[j]) > 0.008)
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
                        if (Math.Abs(pattern[j] - patternCompare[j]) > 0.008)
                        {
                            reverseSamePattern = false;
                            break;
                        }
                    }

                    if (reverseSamePattern)
                        reversePatternInstance++;
                }
            }
            
            refNoteHistory.Reverse();

            int possibleInstances = (refNoteHistory.Count - pattern.Count) / pattern.Count;
            if (possibleInstances <= 0) // idk how this can happen but just in case
                return 0;

            return Math.Min(1, 
                repetitionWeight * (double)Math.Max(patternInstance, reversePatternInstance) / (double)possibleInstances + 
                (1.0 - repetitionWeight) * patternLength
            );
        }
        private static double calculateDowntime(double strainTime, List<double> refNoteHistory)
        {
            int longNoteCount = 0;
            for (int i = 0; i < refNoteHistory.Count; i++)
            {
                if (refNoteHistory[i] > strainTime * 2 - 0.008)
                    longNoteCount++;
            }

            double longNoteFraction = Math.Max(0.5, (double)longNoteCount / (double)refNoteHistory.Count);

            return Math.Pow(Math.Sin(Math.PI * (longNoteFraction - 1.0)), 2.0);
        }

        private static double strainAppearance(double strainTime, List<double> refNoteHistory)
        {
            int strainApperance = 0;
            for (int i = 0; i < refNoteHistory.Count; i++)
            {
                if (Math.Abs(refNoteHistory[i] - strainTime) < 0.008)
                    strainApperance++;
            }

            double strainAppearanceFraction = Math.Max(0.5, (double)strainApperance / (double)refNoteHistory.Count);

            return Math.Pow(Math.Sin(Math.PI * (strainAppearanceFraction - 1.0)), 2.0);
        }
        private static double StrainValueOf(OsuHitObject current, double strainTime, double virtualStrainTime, double prevStrainTime, double prevVirtualStrainTime)
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
            if (noteHistory.Count > 2)
            {
                double repetition = 1.0 - calculateExpectancy(current, noteHistory);
                double virtualRepetition = 1.0 - calculateExpectancy(current, noteHistoryVirtual);
                repetitionVal = Math.Pow(Math.Min(repetition, virtualRepetition), 2.0);

                // When there is major downtime / not much actually happening
                downtimeScale = Math.Min(calculateDowntime(strainTime, noteHistory), calculateDowntime(virtualStrainTime, noteHistoryVirtual));
                
                // When there's a huge stream before a pack of doubles / triples
                appearanceScale = Math.Min(strainAppearance(strainTime, noteHistory), strainAppearance(virtualStrainTime, noteHistoryVirtual));
            }

            double multiplier = Math.Min(
                Math.Min(CompareStrains(strainTime, prevStrainTime, prevFractionSpline), CompareStrains(strainTime, prevVirtualStrainTime, prevFractionSpline)),
                Math.Min(CompareStrains(virtualStrainTime, prevStrainTime, prevFractionSpline), CompareStrains(virtualStrainTime, prevVirtualStrainTime, prevFractionSpline))
            );

            return repetitionVal * multiplier * downtimeScale * appearanceScale / strainTime;
        }
        public static (double, string, List<double>) CalculateFingerControlDiff(List<OsuHitObject> hitObjects, double clockRate)
        {
            if (hitObjects.Count == 0)
                return (0, "", new List<double>());

            // Refresh
            noteHistory = new List<double>();
            noteHistoryVirtual = new List<double>();

            double prevTime = hitObjects[0].StartTime / 1000.0;
            double prevStrainTime = 0;
            double prevVirtualStrainTime = 0;
            double currStrain = 0;
            List<double> strainHistory = new List<double> { 0 };
            List<double> specificStrainHistory = new List<double> { 0 };
            var sw = new StringWriter();

            for (int i = 1; i < hitObjects.Count; i++)
            {
                double currTime = hitObjects[i].StartTime / 1000.0;
                double deltaTime = (currTime - prevTime) / clockRate;

                double strainTime = Math.Max(deltaTime, 0.035);
                double virtualStrainTime = strainTime;
                double strainDecayBase = Math.Pow(0.75, 1 / Math.Min(strainTime, 0.21));

                currStrain *= Math.Pow(strainDecayBase, deltaTime);

                strainHistory.Add(currStrain);

                if (hitObjects[i-1] is Slider prevSlider)
                    virtualStrainTime = Math.Max((currTime - prevSlider.EndTime / 1000.0) / clockRate, 0.035);

                double strain = strainMultiplier * StrainValueOf(hitObjects[i], strainTime, virtualStrainTime, prevStrainTime, prevVirtualStrainTime);
                
                if (i < hitObjects.Count - 1)
                {
                    double nextTime = hitObjects[i+1].StartTime / 1000.0;
                    double nextStrainTime = Math.Max((nextTime - currTime) / clockRate, 0.035);
                    double nextVirtualStrainTime = 0;
                    if (hitObjects[i] is Slider currSlider)
                        nextVirtualStrainTime = Math.Max((nextTime - currSlider.EndTime / 1000.0) / clockRate, 0.035);
                    strain *= Math.Min(
                        Math.Min(CompareStrains(strainTime, nextStrainTime, nextFractionSpline), CompareStrains(strainTime, nextVirtualStrainTime, nextFractionSpline)),
                        Math.Min(CompareStrains(virtualStrainTime, nextStrainTime, nextFractionSpline), CompareStrains(virtualStrainTime, nextVirtualStrainTime, nextFractionSpline))
                    );
                }

                specificStrainHistory.Add(strain);
                
                currStrain += strain;

                sw.WriteLine($"{currTime} {currStrain} {strain}");

                if (deltaTime > 0.035)
                {
                    prevTime = currTime;
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
            double k = 0.95;

            for (int i = 0; i < hitObjects.Count; i++)
                diff += strainHistoryArray[i] * Math.Pow(k, i);

            return (diff * (1 - k), graphText, specificStrainHistory);
        }
    }
}
