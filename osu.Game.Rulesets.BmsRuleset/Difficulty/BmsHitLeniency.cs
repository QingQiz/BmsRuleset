using System;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Difficulty;

internal static class BmsHitLeniency
{
    public static double FromRank(int rank, BmsLayoutVariant layout)
        => FromGreatWindow(getGreatWindow(rank, layout));

    public static double FromJudgementRate(double judgementRate, BmsLayoutVariant layout)
        => FromGreatWindow(getGreatWindow(judgementRate, layout));

    public static double FromGreatWindow(double greatWindowMs)
    {
        var x = 0.3 * Math.Sqrt(greatWindowMs / 500.0);
        return Math.Min(x, 0.6 * (x - 0.09) + 0.09);
    }

    private static double getGreatWindow(int rank, BmsLayoutVariant layout)
    {
        var column = BmsLayout.IsScratchColumn(0, layout) ? 1 : 0;
        var table = BmsJudgementProfileProvider.GetTable(layout, column, rank, tail: false);
        return table.FrameworkWindowFor(HitResult.Great);
    }

    private static double getGreatWindow(double judgementRate, BmsLayoutVariant layout)
    {
        var column = BmsLayout.IsScratchColumn(0, layout) ? 1 : 0;
        var table = BmsJudgementProfileProvider.GetTable(layout, column, judgementRate, tail: false);
        return table.FrameworkWindowFor(HitResult.Great);
    }
}
