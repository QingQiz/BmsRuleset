using System.Collections.Generic;
using System.Linq;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Course;

internal static class BmsCourseScoreAggregation
{
    internal static ScoreInfo CreateScore(BmsCourseSession session)
    {
        var played = session.Stages.Where(stage => stage.Score != null).Select(stage => stage.Score!).ToArray();
        var first = played.FirstOrDefault();
        var beatmap = first?.BeatmapInfo ?? session.Stages[0].Stage.Beatmap;
        var totalWeight = played.Sum(scoreWeight);
        var accuracy = totalWeight == 0 ? 0 : played.Sum(score => score.Accuracy * scoreWeight(score)) / totalWeight;
        var statistics = sumStatistics(played.Select(score => score.Statistics));
        var rank = session.Status == BmsCourseStatus.Passed
            ? new BmsScoreProcessor().RankFromScore(accuracy, statistics)
            : ScoreRank.F;

        return new ScoreInfo
        {
            User = first?.User ?? new APIUser(),
            BeatmapInfo = beatmap,
            BeatmapHash = beatmap.Hash,
            Ruleset = beatmap.Ruleset,
            Passed = session.Status == BmsCourseStatus.Passed,
            Rank = rank,
            TotalScore = played.Sum(score => score.TotalScore),
            TotalScoreWithoutMods = played.Sum(score => score.TotalScoreWithoutMods),
            Accuracy = accuracy,
            MaxCombo = played.Select(score => score.MaxCombo).DefaultIfEmpty().Max(),
            PP = played.All(score => score.PP.HasValue) ? played.Sum(score => score.PP!.Value) : null,
            Mods = session.Mods.Select(mod => mod.DeepClone()).ToArray(),
            Statistics = statistics,
            MaximumStatistics = sumStatistics(played.Select(score => score.MaximumStatistics)),
            HitEvents = played.SelectMany(score => score.HitEvents).ToList(),
            Date = played.Select(score => score.Date).DefaultIfEmpty().Max(),
        };
    }

    private static Dictionary<HitResult, int> sumStatistics(IEnumerable<IReadOnlyDictionary<HitResult, int>> statistics) =>
        statistics.SelectMany(values => values)
            .GroupBy(value => value.Key)
            .ToDictionary(group => group.Key, group => group.Sum(value => value.Value));

    private static int scoreWeight(ScoreInfo score)
    {
        var maximum = score.MaximumStatistics.Values.Sum();
        return maximum > 0 ? maximum : score.Statistics.Values.Sum();
    }
}
