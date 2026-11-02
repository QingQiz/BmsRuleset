using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;

internal static class BmsScoreAggregation
{
    internal static ScoreInfo CreateScore(IReadOnlyList<ScoreInfo> scores, BeatmapInfo fallbackBeatmap, IReadOnlyList<Mod> mods, bool passed)
    {
        var first = scores.FirstOrDefault();
        var beatmap = first?.BeatmapInfo ?? fallbackBeatmap;
        var totalWeight = scores.Sum(scoreWeight);
        var accuracy = totalWeight == 0 ? 0 : scores.Sum(score => score.Accuracy * scoreWeight(score)) / totalWeight;
        var statistics = sumStatistics(scores.Select(score => score.Statistics));

        return new ScoreInfo
        {
            User = first?.User ?? new APIUser(),
            BeatmapInfo = beatmap,
            BeatmapHash = beatmap.Hash,
            Ruleset = beatmap.Ruleset,
            Passed = passed,
            Rank = scores.Count == 0 ? ScoreRank.F : new BmsScoreProcessor().RankFromScore(accuracy, statistics),
            TotalScore = scores.Sum(score => score.TotalScore),
            TotalScoreWithoutMods = scores.Sum(score => score.TotalScoreWithoutMods),
            Accuracy = accuracy,
            MaxCombo = scores.Select(score => score.MaxCombo).DefaultIfEmpty().Max(),
            PP = scores.All(score => score.PP.HasValue) ? scores.Sum(score => score.PP!.Value) : null,
            Mods = mods.Select(mod => mod.DeepClone()).ToArray(),
            Statistics = statistics,
            MaximumStatistics = sumStatistics(scores.Select(score => score.MaximumStatistics)),
            HitEvents = scores.SelectMany(score => score.HitEvents).ToList(),
            Date = scores.Select(score => score.Date).DefaultIfEmpty().Max(),
        };
    }

    private static Dictionary<HitResult, int> sumStatistics(IEnumerable<IReadOnlyDictionary<HitResult, int>> statistics) => statistics
        .SelectMany(values => values)
        .GroupBy(value => value.Key)
        .ToDictionary(group => group.Key, group => group.Sum(value => value.Value));

    private static int scoreWeight(ScoreInfo score)
    {
        var maximum = score.MaximumStatistics.Values.Sum();
        return maximum > 0 ? maximum : score.Statistics.Values.Sum();
    }
}
