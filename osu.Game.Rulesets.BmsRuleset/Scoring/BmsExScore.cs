using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring;

public static class BmsExScore
{
    private const double accuracy_cutoff_x = 1;
    private const double accuracy_cutoff_s = 8.0 / 9.0;
    private const double accuracy_cutoff_a = 7.0 / 9.0;
    private const double accuracy_cutoff_b = 6.0 / 9.0;
    private const double accuracy_cutoff_c = 5.0 / 9.0;
    private const double accuracy_cutoff_d = 0;

    public static int Calculate(IReadOnlyDictionary<HitResult, int> statistics) =>
        statistics.GetValueOrDefault(HitResult.Perfect) * 2
        + statistics.GetValueOrDefault(HitResult.Great);

    public static int Calculate(ScoreInfo score, int maximumExScore)
    {
        if (score.Statistics.Count > 0)
            return Calculate(score.Statistics);

        return (int)Math.Clamp(Math.Round(score.Accuracy * maximumExScore), 0, maximumExScore);
    }

    public static int CountScoringEvents(IEnumerable<BmsJudgementEvent> events) => events.Count(isScoringEvent);

    public static int[] CreateProgression(IEnumerable<BmsJudgementEvent> events)
    {
        var progression = new List<int> { 0 };
        var score = 0;

        foreach (var judgementEvent in events.Where(isScoringEvent))
        {
            score += ValueForResult(judgementEvent.Result);
            progression.Add(score);
        }

        return progression.ToArray();
    }

    public static int ValueForResult(HitResult result) => result switch
    {
        HitResult.Perfect => 2,
        HitResult.Great => 1,
        _ => 0,
    };

    public static ScoreRank RankFromScore(int score, int maximumExScore)
    {
        if (maximumExScore <= 0)
            return ScoreRank.D;

        return RankFromAccuracy((double)score / maximumExScore);
    }

    public static ScoreRank RankFromAccuracy(double accuracy, bool allowX = true) => accuracy switch
    {
        >= accuracy_cutoff_x when allowX => ScoreRank.X,
        >= accuracy_cutoff_s => ScoreRank.S,
        >= accuracy_cutoff_a => ScoreRank.A,
        >= accuracy_cutoff_b => ScoreRank.B,
        >= accuracy_cutoff_c => ScoreRank.C,
        _ => ScoreRank.D,
    };

    public static ScoreRank NextRank(ScoreRank rank) => rank switch
    {
        ScoreRank.D => ScoreRank.C,
        ScoreRank.C => ScoreRank.B,
        ScoreRank.B => ScoreRank.A,
        ScoreRank.A => ScoreRank.S,
        ScoreRank.S or ScoreRank.SH => ScoreRank.X,
        ScoreRank.X or ScoreRank.XH => ScoreRank.X,
        _ => ScoreRank.C,
    };

    public static double AccuracyCutoffFromRank(ScoreRank rank) => rank switch
    {
        ScoreRank.X or ScoreRank.XH => accuracy_cutoff_x,
        ScoreRank.S or ScoreRank.SH => accuracy_cutoff_s,
        ScoreRank.A => accuracy_cutoff_a,
        ScoreRank.B => accuracy_cutoff_b,
        ScoreRank.C => accuracy_cutoff_c,
        ScoreRank.D => accuracy_cutoff_d,
        _ => throw new ArgumentOutOfRangeException(nameof(rank), rank, null),
    };

    public static int MinimumScoreForRank(ScoreRank rank, int maximumExScore) =>
        (int)Math.Ceiling(AccuracyCutoffFromRank(rank) * maximumExScore);

    private static bool isScoringEvent(BmsJudgementEvent judgementEvent) =>
        judgementEvent.Source.IsScoring;
}
