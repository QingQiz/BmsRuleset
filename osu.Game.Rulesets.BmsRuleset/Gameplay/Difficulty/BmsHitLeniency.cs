using System;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Difficulty;

internal static class BmsHitLeniency
{
    public static double FromRank(int rank, BmsLayoutVariant layout)
        => FromGreatWindow(getGreatWindow(layout, BmsEngineRules.RateForRank(layout, rank)));

    public static double FromJudgementRate(double judgementRate, BmsLayoutVariant layout)
        => FromGreatWindow(getGreatWindow(layout, judgementRate));

    public static double FromGreatWindow(double greatWindowMs)
    {
        var x = 0.3 * Math.Sqrt(greatWindowMs / 500.0);
        return Math.Min(x, 0.6 * (x - 0.09) + 0.09);
    }

    private static double getGreatWindow(BmsLayoutVariant layout, double rate)
    {
        // Standalone rating calls have no selected mods; the caller supplies a resolved scalar for modded rules.

        var column = BmsLayout.IsScratchColumn(0, layout) ? 1 : 0;
        var table = BmsEngineRules.GetTiming(layout, column, rate, false);
        return table.FrameworkWindowFor(HitResult.Great);
    }
}
