using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;

internal sealed record BmsPracticeAttemptResult(
    int Attempt,
    ScoreInfo Score,
    IReadOnlyList<BmsGaugeStateSnapshot> Gauges,
    IReadOnlyList<BmsGaugeHistoryEvent> GaugeHistory,
    int Fast,
    int Slow,
    BmsResultStatisticsData Statistics)
{
    internal int ExScore => BmsExScore.Calculate(Score.Statistics);

    internal double Health => Gauges.FirstOrDefault(state => !state.Failed)?.Health ?? Gauges.LastOrDefault()?.Health ?? 0;

    internal static BmsPracticeAttemptResult Create(int attempt, ScoreInfo score, BmsHealthProcessor health,
        IReadOnlyList<BmsGaugeHistoryEvent> gaugeHistory, BmsBeatmap beatmap, BmsPracticeSettings settings,
        IReadOnlyList<BmsJudgementEvent> judgements)
    {
        var timedHits = score.HitEvents.Where(hit => hit.Result is HitResult.Perfect or HitResult.Great or HitResult.Good).ToArray();
        return new BmsPracticeAttemptResult(attempt, score.DeepClone(), health.CurrentGaugeStates.ToArray(), gaugeHistory.ToArray(),
            timedHits.Count(hit => hit.TimeOffset < 0), timedHits.Count(hit => hit.TimeOffset > 0),
            createStatistics(score, beatmap, settings, judgements, gaugeHistory));
    }

    private static BmsResultStatisticsData createStatistics(ScoreInfo score, BmsBeatmap beatmap, BmsPracticeSettings settings,
        IReadOnlyList<BmsJudgementEvent> judgements, IReadOnlyList<BmsGaugeHistoryEvent> gaugeHistory)
    {
        // Statistics use a separate zero-based view so late-song intervals fill the existing result plots.
        var view = new BmsBeatmap { LayoutVariant = beatmap.LayoutVariant, TotalColumns = beatmap.TotalColumns, Total = beatmap.Total };
        view.HitObjects = beatmap.HitObjects.Select(source =>
        {
            BmsHitObject note = source switch
            {
                BmsLongNote hold => new BmsLongNote { Duration = hold.Duration },
                BmsLandmine => new BmsLandmine(),
                _ => new BmsNote(),
            };
            note.Column = source.Column;
            note.StartTime = source.StartTime - settings.StartTime;
            return note;
        }).ToList();
        var relativeJudgements = judgements.Select(judgement => new BmsJudgementEvent(
            judgement.Source with { StartTime = judgement.Source.StartTime - settings.StartTime }, judgement.Result,
            judgement.TimingObservations.Select(observation => observation with
            {
                ExpectedTime = observation.ExpectedTime - settings.StartTime,
                ActualTime = observation.ActualTime - settings.StartTime,
            }), judgement.SuppressPenalty)).ToArray();
        var timing = BmsJudgementEventProjection.CreateTimingHitEvents(relativeJudgements);
        var scoring = BmsJudgementEventProjection.CreateScoringHitEvents(relativeJudgements);
        var duration = Math.Max(settings.EndTime - settings.StartTime,
            view.HitObjects.Select(note => note.GetEndTime()).DefaultIfEmpty(0).Max());
        var relativeGauges = gaugeHistory.Select(entry => entry with { Time = Math.Clamp(entry.Time - settings.StartTime, 0, duration) }).ToArray();
        return BmsResultStatisticsData.Create(score, view, timing, scoring, relativeGauges, duration);
    }
}
