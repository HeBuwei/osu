// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Skills;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Difficulty.MathUtil;
using osu.Game.Rulesets.Osu.Scoring;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Osu.Difficulty
{
    public class OsuDifficultyCalculator : DifficultyCalculator
    {
        private const double aim_multiplier = 0.641;
        private const double tap_multiplier = 0.641;
        private const double finger_control_multiplier = 1.245;

        private const double sr_exponent = 0.83;

        public OsuDifficultyCalculator(Ruleset ruleset, WorkingBeatmap beatmap)
            : base(ruleset, beatmap)
        {
        }

        protected override DifficultyAttributes Calculate(IBeatmap beatmap, Mod[] mods, double clockRate)
        {
            var hitObjects = beatmap.HitObjects as List<OsuHitObject>;

            double mapLength = 0;
            if (beatmap.HitObjects.Count > 0)
                mapLength = (beatmap.HitObjects.Last().StartTime - beatmap.HitObjects.First().StartTime) / 1000 / clockRate;

            double preemptNoClockRate = BeatmapDifficulty.DifficultyRange(beatmap.BeatmapInfo.BaseDifficulty.ApproachRate, 1800, 1200, 450);
            var noteDensities = NoteDensity.CalculateNoteDensities(hitObjects, preemptNoClockRate);

            HitWindows hitWindows = new OsuHitWindows();
            hitWindows.SetDifficulty(beatmap.BeatmapInfo.BaseDifficulty.OverallDifficulty);

            // Todo: These int casts are temporary to achieve 1:1 results with osu!stable, and should be removed in the future
            double hitWindowGreat = (int)(hitWindows.WindowFor(HitResult.Great)) / clockRate;
            double preempt = (int)BeatmapDifficulty.DifficultyRange(beatmap.BeatmapInfo.BaseDifficulty.ApproachRate, 1800, 1200, 450) / clockRate;

            // Tap
            var tapAttributes = Tap.CalculateTapAttributes(hitObjects, clockRate);

            // Finger Control
            (double fingerControlDiff, string fingerGraph, List<double> fingerStrainHistory, int hardFingerStrainAmount) = new FingerControl().CalculateFingerControlDiff(hitObjects, clockRate, strainHistory, hitWindowGreat);

            // Aim
            var aimAttributes = Aim.CalculateAimAttributes(hitObjects, clockRate, tapAttributes.StrainHistory, noteDensities);

            // graph for aim
            string graphFilePath = Path.Combine("cache", $"graph_{beatmap.BeatmapInfo.OnlineBeatmapID}_{string.Join(string.Empty, mods.Select(x => x.Acronym))}.txt");
            File.WriteAllText(graphFilePath, graphText);

            // graph for tap
            string graphFilePathTap = Path.Combine("cache", $"graph_{beatmap.BeatmapInfo.OnlineBeatmapID}_{string.Join(string.Empty, mods.Select(x => x.Acronym))}_tap.txt");
            File.WriteAllText(graphFilePathTap, graphTextTap);

            // graph for finger
            string graphFingerFilePath = Path.Combine("cache", $"graph_{beatmap.BeatmapInfo.OnlineBeatmapID}_{string.Join(string.Empty, mods.Select(x => x.Acronym))}_finger.txt");
            File.WriteAllText(graphFingerFilePath, fingerGraph);
            double tapSr = tap_multiplier * Math.Pow(tapAttributes.TapDifficulty, sr_exponent);
            double aimSr = aim_multiplier * Math.Pow(aimAttributes.FcProbabilityThroughput, sr_exponent);
            double fingerControlSr = finger_control_multiplier * Math.Pow(fingerControlDiff, sr_exponent);

            var valuesSorted = new List<double> { aimSr, tapSr, fingerControlSr };
            valuesSorted.Sort();
            valuesSorted.Reverse();

            double lowestValue = valuesSorted.Last();
            double highestValue = valuesSorted.First();
            double differenceRatio = highestValue / lowestValue;

            double sr = Mean.PowerMean(new double[] { tapSr, aimSr, fingerControlSr, lowestValue * Math.Max(1.0, differenceRatio / 4) }, 7) * 1.131 * 1.0;

            //double sr = Mean.PowerMean(new double[] { tapSr, aimSr, fingerControlSr }, 7) * 1.131;

            int maxCombo = beatmap.HitObjects.Count;
            // Add the ticks + tail of the slider. 1 is subtracted because the head circle would be counted twice (once for the slider itself in the line above)
            maxCombo += beatmap.HitObjects.OfType<Slider>().Sum(s => s.NestedHitObjects.Count - 1);

            return new OsuDifficultyAttributes
            {
                StarRating = sr,
                Mods = mods,
                Length = mapLength,

                TapSr = tapSr,
                TapDiff = tapAttributes.TapDifficulty,
                StreamNoteCount = tapAttributes.StreamNoteCount,
                MashTapDiff = tapAttributes.MashedTapDifficulty,

                FingerControlSr = fingerControlSr,
                FingerControlDiff = fingerControlDiff,
                FingerControlHardStrains = hardFingerStrainAmount,

                AimSr = aimSr,
                AimDiff = aimAttributes.FcProbabilityThroughput,
                AimHiddenFactor = aimAttributes.HiddenFactor,
                ComboTps = aimAttributes.ComboThroughputs,
                MissTps = aimAttributes.MissThroughputs,
                MissCounts = aimAttributes.MissCounts,
                CheeseNoteCount = aimAttributes.CheeseNoteCount,
                CheeseLevels = aimAttributes.CheeseLevels,
                CheeseFactors = aimAttributes.CheeseFactors,

                ApproachRate = preempt > 1200 ? (1800 - preempt) / 120 : (1200 - preempt) / 150 + 5,
                OverallDifficulty = (80 - hitWindowGreat) / 6,
                MaxCombo = maxCombo
            };
        }

        protected override Skill[] CreateSkills(IBeatmap beatmap)
        {
            throw new NotImplementedException();
        }

        protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(IBeatmap beatmap, double clockRate)
        {
            throw new NotImplementedException();
        }

        protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills, double clockRate)
        {
            throw new NotImplementedException();
        }

        protected override Mod[] DifficultyAdjustmentMods => new Mod[]
        {
            new OsuModDoubleTime(),
            new OsuModHalfTime(),
            new OsuModEasy(),
            new OsuModHardRock(),
        };
    }
}
