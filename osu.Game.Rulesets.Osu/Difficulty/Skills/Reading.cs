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
        private const double multiplier = 10.0;

        /// <summary>
        /// Calculates reading difficulty of the map
        /// </summary>
        public static (double, string) CalculateReadingDiff(List<OsuHitObject> hitObjects, List<double> noteDensities, List<double> fingerStrains, double clockRate)
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
                    
                    rhythmReadingComplexity = calculateRhythmReading(visibleObjects, currentObject, fingerStrains[i]);
                    aimReadingComplexity = calculateAimReading(visibleObjects, currentObject, hitObjects[i + 1]);
                }

                var strain = rhythmReadingComplexity + aimReadingComplexity * multiplier;
                strainHistory.Add(strain);

                sw.WriteLine($"{hitObjects[i].StartTime / 1000.0} {strain} {strain}");
            }

            // aggregate strain values to compute difficulty
            var strainHistoryArray = strainHistory.ToArray();

            Array.Sort(strainHistoryArray);
            Array.Reverse(strainHistoryArray);

            double diff = 0;

            const double k = 0.95;

            for (int i = 0; i < hitObjects.Count; i++)
            {
                diff += strainHistoryArray[i] * Math.Pow(k, i);
            }

            return (diff * (1 - k) * 1.1, sw.ToString());
        }

        private static double calculateRhythmReading(List<OsuHitObject> visibleObjects, OsuHitObject currentObject, double currentFingerStrain)
        {
            var overlapness = 0.0;
            var currentPosition = Vector<double>.Build.Dense(new[] { currentObject.StackedPosition.X, (double)currentObject.StackedPosition.Y });

            // calculate how much visible objects overlap current object
            foreach (var visibleObject in visibleObjects)
            {
                var visibleObjectPosition = Vector<double>.Build.Dense(new[] { visibleObject.StackedPosition.X, (double)visibleObject.StackedPosition.Y });
                var visibleDistance = ((currentPosition - visibleObjectPosition) / (2 * currentObject.Radius)).L2Norm();

                overlapness += SpecialFunctions.Logistic((0.5 - visibleDistance) / 0.1) - 0.2;

                // this is temp until sliders get proper reading impl
                if (visibleObject is Slider)
                    overlapness /= 2.0;

                overlapness = Math.Max(0, overlapness);
            }

            return Math.Pow(0.3, 2 / currentFingerStrain) * overlapness;
        }

        private static double calculateAimReading(List<OsuHitObject> visibleObjects, OsuHitObject currentObject, OsuHitObject nextObject)
        {
            var intersections = 0.0;

            var currentPosition = Vector<double>.Build.Dense(new[] { currentObject.StackedPosition.X, (double)currentObject.StackedPosition.Y });
            var nextPosition = Vector<double>.Build.Dense(new[] { nextObject.StackedPosition.X, (double)nextObject.StackedPosition.Y });
            var nextVector = currentPosition - nextPosition;
            var movementDistance = ((nextPosition - currentPosition) / (2 * currentObject.Radius)).L2Norm();

            // calculate amount of circles intersecting the movement
            foreach (var visibleObject in visibleObjects)
            {
                var visibleObjectPosition = Vector<double>.Build.Dense(new[] { visibleObject.StackedPosition.X, (double)visibleObject.StackedPosition.Y });
                var visibleVector = currentPosition - visibleObjectPosition;
                var visibleToNextDistance = ((nextPosition - visibleObjectPosition) / (2 * currentObject.Radius)).L2Norm();

                // scale the bonus by distance of movement and distance between intersected object and movement end object
                var intersectionBonus = checkMovementIntersect(nextVector, nextObject.Radius * 2, visibleVector) *
                                        SpecialFunctions.Logistic((movementDistance - 3) / 0.7) *
                                        SpecialFunctions.Logistic((3 - visibleToNextDistance) / 0.7);

                // this is temp until sliders get proper reading impl
                if (visibleObject is Slider)
                    intersectionBonus *= 2.0;

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
                    return 1.0;
                }

                // here t1 didn't intersect so we are either started
                // inside the sphere or completely past it
                if (t2 >= 0 && t2 <= 1)
                {
                    return 0.5;
                }

                return 0.0;
            }
        }
    }
}
