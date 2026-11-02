using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal sealed record BmsResultStatisticsData(
    IReadOnlyList<BmsGaugeHistoryGraph.GaugeSeries> Gauges,
    IReadOnlyList<float> StageBoundaries,
    BmsTimelineStatistic.TimelineData Timeline,
    BmsHitScatterStatistic.HitScatterStatistics Scatter,
    BmsHitOffsetStatistic.HitOffsetStatistics Offset)
{
    internal IReadOnlyList<HitEvent> HitEvents { get; init; } = [];

    internal double GaugeDuration { get; init; }

    internal IReadOnlyList<BmsGaugeStateSnapshot> EndingGaugeStates { get; init; } = [];

    internal static BmsResultStatisticsData Create(ScoreInfo score, IBeatmap beatmap, IReadOnlyList<BmsGaugeHistoryEvent>? gauges = null)
        => Create(score, beatmap, score.HitEvents, score.HitEvents, gauges ?? []);

    internal static BmsResultStatisticsData? Load(ScoreInfo score, IBeatmap beatmap, Replay replay, CancellationToken cancellationToken,
                                                  IReadOnlyList<BmsGaugeStateSnapshot>? courseGaugeStates = null, BmsGaugeProfileFamily? courseGaugeProfileFamily = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (beatmap is not BmsBeatmap bmsBeatmap || !replay.HasReceivedAllFrames || replay.Frames.Count == 0)
            return score.HitEvents.Count > 0 ? Create(score, beatmap) : null;

        var simulated = BmsReplaySimulation.Run(bmsBeatmap, replay, score, cancellationToken, courseGaugeStates, courseGaugeProfileFamily);
        cancellationToken.ThrowIfCancellationRequested();
        return Create(score, beatmap,
            BmsJudgementEventProjection.CreateTimingHitEvents(simulated.Judgements),
            BmsJudgementEventProjection.CreateScoringHitEvents(simulated.Judgements), simulated.Gauges);
    }

    internal static BmsResultStatisticsData Create(ScoreInfo score, IBeatmap beatmap, IReadOnlyList<HitEvent> hitEvents,
        IReadOnlyList<HitEvent> scoringEvents, IReadOnlyList<BmsGaugeHistoryEvent> gauges, double minimumDuration = 0)
    {
        var timeline = BmsTimelineStatistic.CreateData(beatmap, hitEvents, scoringEvents, minimumDuration);
        return new BmsResultStatisticsData(
            BmsGaugeHistoryGraph.CreateSeries(score, beatmap, scoringEvents, gauges, minimumDuration),
            [],
            timeline,
            BmsHitScatterStatistic.CreateStatistics(beatmap, hitEvents, minimumDuration),
            BmsHitOffsetStatistic.CreateStatistics(beatmap, hitEvents))
        {
            HitEvents = hitEvents,
            GaugeDuration = Math.Max(timeline.Duration, gauges.Select(entry => entry.Time).DefaultIfEmpty(0).Max()),
            EndingGaugeStates = gauges.LastOrDefault()?.States ?? [],
        };
    }

    internal static BmsResultStatisticsData CreateCourse(IReadOnlyList<(IBeatmap Beatmap, BmsResultStatisticsData Data)> stages)
    {
        var played = stages.Where(stage => stage.Data.HitEvents.Count > 0).Select(stage => (stage.Beatmap, stage.Data.HitEvents)).ToArray();
        return combine(stages.Select(stage => stage.Data).ToArray(),
            BmsHitScatterStatistic.CreateCourseStatistics(played), BmsHitOffsetStatistic.CreateCourseStatistics(played));
    }

    internal static BmsResultStatisticsData Combine(IReadOnlyList<BmsResultStatisticsData> attempts)
        => combine(attempts, BmsHitScatterStatistic.CombineStatistics(attempts.Select(attempt => attempt.Scatter).ToArray()),
            BmsHitOffsetStatistic.CombineStatistics(attempts.Select(attempt => (attempt.Offset, attempt.HitEvents)).ToArray()));

    private static BmsResultStatisticsData combine(IReadOnlyList<BmsResultStatisticsData> stages,
        BmsHitScatterStatistic.HitScatterStatistics scatter, BmsHitOffsetStatistic.HitOffsetStatistics offset)
    {
        var durations = stages.Select(stage => stage.GaugeDuration).ToArray();
        return new BmsResultStatisticsData(
            BmsGaugeHistoryGraph.CreateCourseSeries(stages.Select(stage => stage.Gauges).ToArray(), durations),
            BmsGaugeHistoryGraph.CreateCourseStageBoundaries(durations),
            BmsTimelineStatistic.CreateCourseData(stages.Select(stage => stage.Timeline).ToArray()), scatter, offset)
        {
            HitEvents = stages.SelectMany(stage => stage.HitEvents).ToArray(),
            GaugeDuration = durations.Sum(),
        };
    }

    internal static BmsResultStatisticsData Empty() => Create(new ScoreInfo(), new BmsBeatmap());
}
