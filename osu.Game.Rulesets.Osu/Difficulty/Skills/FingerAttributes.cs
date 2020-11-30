// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    public class FingerAttributes
    {
        public double FingerDifficulty;
        public List<double> StrainHistory;
        public int HardStrainAmount;
        public string Graph;
    }
}
