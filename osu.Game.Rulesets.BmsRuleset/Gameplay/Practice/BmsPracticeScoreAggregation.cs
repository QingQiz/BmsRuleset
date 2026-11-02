using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;

internal static class BmsPracticeScoreAggregation
{
    internal static ScoreInfo CreateScore(BmsPracticeSession session)
    {
        var scores = session.AttemptResults.Select(attempt => attempt.Score).ToArray();
        var aggregate = BmsScoreAggregation.CreateScore(scores, session.Source.BeatmapInfo, session.Mods,
            scores.Length > 0 && scores.All(score => score.Passed));
        // Practice plots use times relative to the selected interval.
        aggregate.HitEvents = session.AttemptResults.SelectMany(attempt => attempt.Statistics.HitEvents).ToList();
        return aggregate;
    }

    internal static BmsResultStatisticsData CreateStatistics(BmsPracticeSession session)
        => BmsResultStatisticsData.Combine(session.AttemptResults.Select(attempt => attempt.Statistics).ToArray());
}
