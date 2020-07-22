﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;

using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Preprocessing
{
    public static class NoteDensity
    {
        /// <summary>
        /// Calculates note density for every note
        /// </summary>
        public static List<double> CalculateNoteDensities(List<OsuHitObject> hitObjects, double preempt)
        {
            List<double> noteDensities = new List<double>();

            Queue<OsuHitObject> window = new Queue<OsuHitObject>();

            int next = 0;

            for (int i = 0; i < hitObjects.Count; i++)
            {
                while (next < hitObjects.Count && hitObjects[next].StartTime < hitObjects[i].StartTime + preempt)
                {
                    window.Enqueue(hitObjects[next]);
                    next++;
                }

                while (window.Peek().StartTime < hitObjects[i].StartTime - preempt)
                {
                    window.Dequeue();
                }

                noteDensities.Add(calculateNoteDensity(hitObjects[i].StartTime, preempt, window));
            }

            return noteDensities;
        }

        public static List<double> CalculateVisibleCircles(List<OsuHitObject> hitObjects, double preempt)
        {
            List<double> noteDensities = new List<double>();

            Queue<OsuHitObject> window = new Queue<OsuHitObject>();

            int next = 0;

            for (int i = 0; i < hitObjects.Count; i++)
            {
                while (next < hitObjects.Count && hitObjects[next].StartTime < hitObjects[i].StartTime + preempt)
                {
                    window.Enqueue(hitObjects[next]);
                    next++;
                }

                while (window.Peek().StartTime < hitObjects[i].StartTime - preempt)
                {
                    window.Dequeue();
                }

                noteDensities.Add(window.Count);
            }

            return noteDensities;
        }


        private static double calculateNoteDensity(double time, double preempt, Queue<OsuHitObject> window)
        {
            double noteDensity = 0;

            foreach (var hitObject in window)
            {
                noteDensity += 1 - Math.Abs(hitObject.StartTime - time) / preempt;
            }

            return noteDensity;
        }
    }
}
