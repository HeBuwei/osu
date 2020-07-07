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
        /// <summary>
        /// Calculates reading difficulty of the map
        /// </summary>
        public static (double, string) CalculateReadingDiff(List<OsuHitObject> hitObjects, List<double> noteDensities, List<double> fingerStrains, double clockRate)
        {
            if (hitObjects.Count == 0)
                return (0, string.Empty);

            var sw = new StringWriter();
            sw.WriteLine($"{hitObjects[0].StartTime / 1000.0} 0 0");

            double currStrain = 0;
            var strainHistory = new List<double> { 0, 0 }; // first and last objects are 0

            for (int i = 1; i < hitObjects.Count - 1; i++)
            {
                var currentObject = hitObjects[i];
                var currentPosition = Vector<double>.Build.Dense(new[] { currentObject.Position.X, (double)currentObject.Position.Y });
                var currentFingerStrain = fingerStrains[i];

                if (currentObject is Spinner)
                {
                    strainHistory.Add(0);
                    continue;
                }

                var noteDensity = (int)Math.Floor(noteDensities[i]) - 1; // exclude current circle
                if (noteDensity > hitObjects.Count - i)
                    noteDensity = hitObjects.Count - i;

                var overlapness = 0.0;
                var intersections = 0.0;
                if (noteDensity > 1 )
                {
                   var visibleObjects = hitObjects.GetRange(i, noteDensity);
                    foreach (var visibleObject in visibleObjects)
                    {
                        // calculate how much visible objects overlap current object
                        var visibleObjectPosition = Vector<double>.Build.Dense(new[] { visibleObject.Position.X, (double)visibleObject.Position.Y });
                        var distance = ((currentPosition - visibleObjectPosition) / (2 * currentObject.Radius)).L2Norm();
                        overlapness += SpecialFunctions.Logistic((0.5 - distance) / 0.1) - 0.2;
                        overlapness = Math.Max(0, overlapness);
                    }

                    var nextObject = hitObjects[i + 1];
                    var nextPosition = Vector<double>.Build.Dense(new[] { nextObject.Position.X, (double)nextObject.Position.Y });
                    var nextVector = currentPosition - nextPosition;

                    foreach (var visibleObject in visibleObjects)
                    {
                        // calculate amount of circles intersecting the movement
                        var visibleObjectPosition = Vector<double>.Build.Dense(new[] { visibleObject.Position.X, (double)visibleObject.Position.Y });
                        var visibleVector = currentPosition - visibleObjectPosition;
                        intersections += checkMovementIntersect(nextVector, nextObject.Radius * 2, visibleVector);
                    }
                    
                }

                var strain = (currentFingerStrain * overlapness) + intersections;
                currStrain += strain;
                strainHistory.Add(strain);

                sw.WriteLine($"{hitObjects[i].StartTime / 1000.0} {currStrain} {strain}");
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
                    // ExitWound
                    return 0.5;
                }

                return 0.0;
            }
        }
    }
}
