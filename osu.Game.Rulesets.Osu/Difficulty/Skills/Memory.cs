using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    public static class Memory
    {
        private const float default_flashlight_size = 180;

        /// <summary>
        /// Calculates memory difficulty of the map
        /// </summary>
        public static (double, string) CalculateMemoryDiff(List<OsuHitObject> hitObjects, List<double> noteDensities, double clockRate, bool hidden = false)
        {
            if (hitObjects.Count == 0)
                return (0, string.Empty);

            var sw = new StringWriter();
            sw.WriteLine($"{hitObjects[0].StartTime / 1000.0} 0 0");

            //double currStrain = 0;
            var strainHistory = new List<double> { 0, 0 }; // first and last objects are 0

            var maxCombo = hitObjects.Count + hitObjects.Select(x => x.NestedHitObjects).Count();

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
                    strainHistory.Add(1.0);
                    continue;
                }

                var visibleObjects = 0.0;
                if (noteDensity > 1)
                {
                    var potentiallyVisibleObjects = hitObjects.GetRange(i, noteDensity);

                    var intersections = 0.0;

                    var currentPosition = Vector<double>.Build.Dense(new[] { currentObject.StackedPosition.X, (double)currentObject.StackedPosition.Y });

                    // calculate amount of circles intersecting fl area
                    for (int j = 1; j < potentiallyVisibleObjects.Count; j++)
                    {
                        var visibleObject = potentiallyVisibleObjects[j];
                        var visibleObjectPosition = Vector<double>.Build.Dense(new[] { visibleObject.StackedPosition.X, (double)visibleObject.StackedPosition.Y });

                        // scale the bonus by distance of movement and distance between intersected object and movement end object
                        var intersectionBonus = contains(currentPosition, visibleObjectPosition, getAreaSizeFor(maxCombo), visibleObject.Radius);

                        // this is temp until sliders get proper reading impl
                        //if (visibleObject is Slider)
                        //    intersectionBonus *= 2.0;

                        // TODO: approach circle intersections

                        intersections += intersectionBonus;
                    }

                    visibleObjects = intersections / (potentiallyVisibleObjects.Count - 1);
                }

                var strain = 0.0;

                //if (visibleObjects > 0)
                    strain = Math.Min(1.0, visibleObjects);

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

        private static double contains(Vector<double> areaPosition, Vector<double> objectPosition, double areaRadius, double objectRadius)
        {
            var d = Math.Sqrt(
                Math.Pow(objectPosition[0] - areaPosition[0], 2) +
                Math.Pow(objectPosition[1] - areaPosition[1], 2));

            if (areaRadius > (d + objectRadius))
                return (areaRadius - (d + objectRadius)) / areaRadius;

            return 0.0;
         }

        private static float getAreaSizeFor(int combo)
        {
            if (combo > 200)
                return default_flashlight_size * 0.8f;
            else if (combo > 100)
                return default_flashlight_size * 0.9f;
            else
                return default_flashlight_size;
        }
    }
}
