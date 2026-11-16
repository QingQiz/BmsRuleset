using RhythmEngine.Core;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using RhythmEngine.Profiles.Bms;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System;
using osu.Game.Beatmaps;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
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

    internal HealthState? EndingHealth { get; init; }

    internal static BmsResultStatisticsData Create(ScoreInfo score, IBeatmap beatmap, IReadOnlyList<GameplayHealthHistoryEvent>? gauges = null)
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
        IReadOnlyList<HitEvent> scoringEvents, IReadOnlyList<GameplayHealthHistoryEvent> gauges, double minimumDuration = 0)
    {
        var profile = GameplayProfile.FromMods(score.Mods);
        var timeline = BmsTimelineStatistic.CreateData(profile, beatmap, hitEvents, scoringEvents, minimumDuration);
        return new BmsResultStatisticsData(
            BmsGaugeHistoryGraph.CreateSeries(score, beatmap, scoringEvents, gauges, minimumDuration),
            [],
            timeline,
            BmsHitScatterStatistic.CreateStatistics(profile, beatmap, hitEvents, minimumDuration),
            BmsHitOffsetStatistic.CreateStatistics(profile, beatmap, hitEvents))
        {
            HitEvents = hitEvents,
            GaugeDuration = Math.Max(timeline.Duration, gauges.Select(entry => entry.Time).DefaultIfEmpty(0).Max()),
            EndingHealth = gauges.LastOrDefault()?.Health,
        };
    }

    internal static BmsResultStatisticsData CreateCourse(IReadOnlyList<BmsResultStatisticsData> stages)
    {
        var played = stages.Where(stage => stage.HitEvents.Count > 0).ToArray();
        return combine(stages,
            BmsHitScatterStatistic.CombineStatistics(played.Select(stage => stage.Scatter).ToArray()),
            BmsHitOffsetStatistic.CombineStatistics(played.Select(stage => stage.Offset).ToArray()));
    }

    internal static BmsResultStatisticsData Combine(IReadOnlyList<BmsResultStatisticsData> attempts)
        => combine(attempts, BmsHitScatterStatistic.CombineStatistics(attempts.Select(attempt => attempt.Scatter).ToArray()),
            BmsHitOffsetStatistic.CombineStatistics(attempts.Select(attempt => attempt.Offset).ToArray()));

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
