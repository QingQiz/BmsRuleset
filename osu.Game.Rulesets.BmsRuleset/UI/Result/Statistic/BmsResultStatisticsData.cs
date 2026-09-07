using System.Collections.Generic;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal sealed record BmsResultStatisticsData(
    IReadOnlyList<BmsGaugeHistoryGraph.GaugeSeries> Gauges,
    IReadOnlyList<float> StageBoundaries,
    BmsTimelineStatistic.TimelineData Timeline,
    BmsHitScatterStatistic.HitScatterStatistics Scatter,
    BmsHitOffsetStatistic.HitOffsetStatistics Offset)
{
    internal static BmsResultStatisticsData Create(ScoreInfo score, IBeatmap beatmap) => new(
        BmsGaugeHistoryGraph.CreateSeries(score, beatmap),
        [],
        BmsTimelineStatistic.CreateData(score, beatmap),
        BmsHitScatterStatistic.CreateStatistics(beatmap, score.HitEvents),
        BmsHitOffsetStatistic.CreateStatistics(beatmap, score.HitEvents));

    internal static BmsResultStatisticsData Empty() => Create(new ScoreInfo(), new BmsBeatmap());
}
