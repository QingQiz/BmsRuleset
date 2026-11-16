using System.Collections.Generic;
using System.Linq;
using System;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;

internal sealed record BmsPracticeAttemptResult(
    int Attempt,
    ScoreInfo Score,
    int Fast,
    int Slow,
    BmsResultStatisticsData Statistics)
{
    internal double Health => Statistics.EndingHealth?.Value ?? 0;

    internal static BmsPracticeAttemptResult Create(int attempt, ScoreInfo score,
        IReadOnlyList<GameplayHealthHistoryEvent> gaugeHistory, BmsBeatmap beatmap, BmsPracticeSettings settings,
        IReadOnlyList<BmsJudgementEvent> judgements)
    {
        var profile = GameplayProfile.FromMods(score.Mods);
        var timedHits = score.HitEvents.Where(hit => profile.IsTimingEvent(hit) && profile.CountsForFastSlow(hit.Result)).ToArray();
        return new BmsPracticeAttemptResult(attempt, score.DeepClone(),
            timedHits.Count(hit => hit.TimeOffset < 0), timedHits.Count(hit => hit.TimeOffset > 0),
            createStatistics(score, beatmap, settings, judgements, gaugeHistory));
    }

    private static BmsResultStatisticsData createStatistics(ScoreInfo score, BmsBeatmap beatmap, BmsPracticeSettings settings,
        IReadOnlyList<BmsJudgementEvent> judgements, IReadOnlyList<GameplayHealthHistoryEvent> gaugeHistory)
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
        var timing = BmsJudgementEventProjection.CreateTimingHitEvents(judgements, settings.StartTime);
        var scoring = BmsJudgementEventProjection.CreateScoringHitEvents(judgements, settings.StartTime);
        var duration = Math.Max(settings.EndTime - settings.StartTime,
            view.HitObjects.Select(note => note.GetEndTime()).DefaultIfEmpty(0).Max());
        var relativeGauges = gaugeHistory.Select(entry => entry with { Time = Math.Clamp(entry.Time - settings.StartTime, 0, duration) }).ToArray();
        return BmsResultStatisticsData.Create(score, view, timing, scoring, relativeGauges, duration);
    }
}
