using System;
using System.Collections.Generic;
using System.IO;
using MathNet.Numerics;
using MathNet.Numerics.LinearAlgebra;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    public static class Reading
    {
        private const double rhythm_multiplier = 10.0;
        private const double aim_multiplier = 30.0;

        /// <summary>
        /// Calculates reading difficulty of the map
        /// </summary>
        public static (double, string) CalculateReadingDiff(List<OsuHitObject> hitObjects, List<double> noteDensities, List<double> fingerStrains, double clockRate, bool hidden = false)
        {
            if (hitObjects.Count == 0)
                return (0, string.Empty);

            var sw = new StringWriter();
            sw.WriteLine($"{hitObjects[0].StartTime / 1000.0} 0 0");

            //double currStrain = 0;
            var strainHistory = new List<double> { 0, 0 }; // first and last objects are 0

            for (int i = 1; i < hitObjects.Count - 1; i++)
            {
                var noteDensity = (int)Math.Floor(noteDensities[i]) - 1; // exclude current circle
                if (noteDensity > hitObjects.Count - i)
                    noteDensity = hitObjects.Count - i;

                // need better decaying or no decaying at all (why would reading decay anyway?)
                //currStrain *= 0.2 + SpecialFunctions.Logistic((noteDensity - 3) / 0.7) * 0.7;

                var currentObject = hitObjects[i];
                if (currentObject is Spinner)
                {
                    strainHistory.Add(0);
                    continue;
                }

                var rhythmReadingComplexity = 0.0;
                var aimReadingComplexity = 0.0;
                if (noteDensity > 1)
                {
                    var visibleObjects = hitObjects.GetRange(i, noteDensity);
                    var nextObject = hitObjects[i + 1];

                    rhythmReadingComplexity = calculateRhythmReading(visibleObjects, hitObjects[i - 1], currentObject, nextObject, fingerStrains[i], clockRate, hidden) * rhythm_multiplier;
                    aimReadingComplexity = calculateAimReading(visibleObjects, currentObject, nextObject, hidden) * aim_multiplier;
                }

                var strain = rhythmReadingComplexity + aimReadingComplexity;

                strainHistory.Add(strain);

                sw.WriteLine($"{currentObject.StartTime / 1000.0} {strain}");
            }

            // aggregate strain values to compute difficulty
            var strainHistoryArray = strainHistory.ToArray();

            Array.Sort(strainHistoryArray);
            Array.Reverse(strainHistoryArray);

            double diff = 0;

            const double k = 0.99;

            for (int i = 0; i < hitObjects.Count; i++)
            {
                diff += strainHistoryArray[i] * Math.Pow(k, i);
            }

            return (diff * (1 - k) * 1.1, sw.ToString());
        }

        private static double calculateRhythmReading(List<OsuHitObject> visibleObjects,
                                                     OsuHitObject prevObject,
                                                     OsuHitObject currentObject,
                                                     OsuHitObject nextObject,
                                                     double currentFingerStrain,
                                                     double clockRate,
                                                     bool hidden)
        {
            var overlapness = 0.0;
            var prevPosition = Vector<double>.Build.Dense(new[] { prevObject.StackedPosition.X, (double)prevObject.StackedPosition.Y });

            var currentPosition = Vector<double>.Build.Dense(new[] { currentObject.StackedPosition.X, (double)currentObject.StackedPosition.Y });
            var prevCurrDistance = ((currentPosition - prevPosition) / (2 * currentObject.Radius)).L2Norm();

            var nextPosition = Vector<double>.Build.Dense(new[] { nextObject.StackedPosition.X, (double)nextObject.StackedPosition.Y });
            var currNextDistance = ((nextPosition - currentPosition) / (2 * currentObject.Radius)).L2Norm();

            // buff overlapness if previous object was also overlapping
            overlapness += SpecialFunctions.Logistic((0.5 - prevCurrDistance) / 0.1) - 0.2;

            // calculate how much visible objects overlap current object
            for (int i = 1; i < visibleObjects.Count; i++)
            {
                var visibleObject = visibleObjects[i];
                var visibleObjectPosition = Vector<double>.Build.Dense(new[] { visibleObject.StackedPosition.X, (double)visibleObject.StackedPosition.Y });
                var visibleDistance = ((currentPosition - visibleObjectPosition) / (2 * currentObject.Radius)).L2Norm();

                overlapness += SpecialFunctions.Logistic((0.5 - visibleDistance) / 0.1) - 0.2;

                // this is temp until sliders get proper reading impl
                if (visibleObject is Slider)
                    overlapness /= 2.0;

                overlapness = Math.Max(0, overlapness);
            }
            overlapness /= visibleObjects.Count / 2.0;
            
            // calculate if rhythm change correlates to spacing change
            var tPrevCurr = (currentObject.StartTime - prevObject.StartTime) / clockRate;
            var tCurrNext = (nextObject.StartTime - currentObject.StartTime) / clockRate;
            var tRatio = tCurrNext / (tPrevCurr + 1e-10);

            var distanceRatio = currNextDistance / (prevCurrDistance + 1e-10);

            var changeRatio = distanceRatio * tRatio;
            var spacingChange = Math.Min(1.05, Math.Pow(changeRatio - 1, 2) * 1000) * Math.Min(1.00, Math.Pow(distanceRatio - 1, 2) * 1000);

            return Math.Pow(0.3, 2 / (currentFingerStrain + 1e-10)) * overlapness * spacingChange * (hidden ? 1.2 : 1.0);
        }

        private static double calculateAimReading(List<OsuHitObject> visibleObjects, OsuHitObject currentObject, OsuHitObject nextObject, bool hidden)
        {
            var intersections = 0.0;

            var currentPosition = Vector<double>.Build.Dense(new[] { currentObject.StackedPosition.X, (double)currentObject.StackedPosition.Y });
            var nextPosition = Vector<double>.Build.Dense(new[] { nextObject.StackedPosition.X, (double)nextObject.StackedPosition.Y });
            var nextVector = currentPosition - nextPosition;
            var movementDistance = ((nextPosition - currentPosition) / (2 * currentObject.Radius)).L2Norm();

            // calculate amount of circles intersecting the movement excluding current and next circles
            for (int i = 2; i < visibleObjects.Count; i++)
            {
                var visibleObject = visibleObjects[i];
                var visibleObjectPosition = Vector<double>.Build.Dense(new[] { visibleObject.StackedPosition.X, (double)visibleObject.StackedPosition.Y });
                var visibleToCurrentVector = currentPosition - visibleObjectPosition;
                var visibleToNextDistance = ((nextPosition - visibleObjectPosition) / (2 * currentObject.Radius)).L2Norm();

                // scale the bonus by distance of movement and distance between intersected object and movement end object
                var intersectionBonus = checkMovementIntersect(nextVector, nextObject.Radius * 2, visibleToCurrentVector) *
                                        SpecialFunctions.Logistic((movementDistance - 3) / 0.7) *
                                        SpecialFunctions.Logistic((3 - visibleToNextDistance) / 0.7);

                // this is temp until sliders get proper reading impl
                if (visibleObject is Slider)
                    intersectionBonus *= 2.0;

                // TODO: approach circle intersections

                intersections += intersectionBonus;
            }

            return intersections / visibleObjects.Count;
        }


        private static double checkMovementIntersect(Vector<double> direction, double radius, Vector<double> endPoint)
        {
            double a = direction.DotProduct(direction);
            double b = 2 * endPoint.DotProduct(direction);
            double c = endPoint.DotProduct(endPoint) - radius * radius;

            double discriminant = b * b - 4 * a * c;
            if (discriminant < 0)
            {
                // no intersection
                return 0.0;
            }
            else
            {
                discriminant = Math.Sqrt(discriminant);

                double t1 = (-b - discriminant) / (2 * a);
                double t2 = (-b + discriminant) / (2 * a);

                if (t1 >= 0 && t1 <= 1)
                {
                    // t1 is the intersection, and it's closer than t2
                    return t1;
                }

                // here t1 didn't intersect so we are either started
                // inside the sphere or completely past it
                if (t2 >= 0 && t2 <= 1)
                {
                    return t2 / 2.0;
                }
                
                return 0.0;
            }
        }
    }
}
