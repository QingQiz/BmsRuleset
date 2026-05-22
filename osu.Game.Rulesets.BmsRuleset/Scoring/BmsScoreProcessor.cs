using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring;

public partial class BmsScoreProcessor() : ScoreProcessor(new BmsRuleset())
{
    private const double combo_base = 4;

    public override int GetBaseScoreForResult(HitResult result)
    {
        return result switch
        {
            HitResult.Perfect => 305,
            _ => base.GetBaseScoreForResult(result),
        };
    }

    public override ScoreRank RankFromScore(double accuracy, IReadOnlyDictionary<HitResult, int> results)
    {
        var rank = base.RankFromScore(accuracy, results);

        if (rank != ScoreRank.S)
            return rank;

        var anyImperfect =
            results.GetValueOrDefault(HitResult.Good) > 0
            || results.GetValueOrDefault(HitResult.Ok) > 0
            || results.GetValueOrDefault(HitResult.Meh) > 0
            || results.GetValueOrDefault(HitResult.Miss) > 0;

        return anyImperfect ? rank : ScoreRank.X;
    }

    protected override IEnumerable<HitObject> EnumerateHitObjects(IBeatmap beatmap)
        => base.EnumerateHitObjects(beatmap).Order(JudgementOrderComparer.DEFAULT);

    protected override double ComputeTotalScore(double comboProgress, double accuracyProgress, double bonusPortion) => 150000 * comboProgress
        + 850000 * Math.Pow(Accuracy.Value, 2 + 2 * Accuracy.Value) * accuracyProgress
        + bonusPortion;

    protected override double GetComboScoreChange(JudgementResult result) => getBaseComboScoreForResult(result.Type)
                                                                             * Math.Min(Math.Max(0.5, Math.Log(result.ComboAfterJudgement, combo_base)),
                                                                                 Math.Log(400, combo_base));

    private int getBaseComboScoreForResult(HitResult result)
    {
        return result switch
        {
            HitResult.Perfect => 300,
            _ => GetBaseScoreForResult(result),
        };
    }

    private class JudgementOrderComparer : IComparer<HitObject>
    {
        public static readonly JudgementOrderComparer DEFAULT = new();

        public int Compare(HitObject? x, HitObject? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            var result = x.GetEndTime().CompareTo(y.GetEndTime());
            if (result != 0)
                return result;

            // Native BMS should judge objects with identical end times in chart/lane order.
            // This replaces the old mania-specific Note-before-HoldNote ordering without
            // introducing a dependency on mania object types.
            if (x is BmsHitObject bx && y is BmsHitObject by)
                return bx.Column.CompareTo(by.Column);

            return 0;
        }
    }
}
