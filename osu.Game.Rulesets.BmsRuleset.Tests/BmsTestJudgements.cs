#nullable enable
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using System.Collections.Immutable;
using System.Linq;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Bms;
using RhythmEngine.Profiles.Common;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.Scoring;
using EngineResult = RhythmEngine.Core.JudgementResult;
using JudgementResult = osu.Game.Rulesets.Judgements.JudgementResult;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

/// <summary>Supplies authoritative reducer output to isolated framework processor tests.</summary>
internal static class BmsTestJudgements
{
    internal static BmsNote CreateEndpoint(BmsLongNote source, double time) => new()
    {
        StartTime = time,
        Column = source.Column,
        Beatmap = source.Beatmap,
        HitWindows = source.HitWindows,
    };

    private static BmsEngineChart healthChart(BmsHealthProcessor processor, BmsHitObject eventSource)
    {
        var source = (IBeatmap?)processor.Beatmap ?? (IBeatmap)eventSource.Beatmap;
        var notes = source.HitObjects.Count > 0 ? source.HitObjects.Cast<BmsHitObject>().ToArray() : [eventSource];
        foreach (var note in notes)
            note.Beatmap = (IBmsBeatmap)source;
        return new BmsEngineChart(notes, ((IBmsBeatmap)source).LayoutVariant, notes.Select(note => note.Column).Distinct().ToArray(), GameplayProfile.FromMods(processor.Mods));
    }

    internal static HitResult HitAt(this BmsTimingDefinition timing, double milliseconds)
    {
        var result = timing.Windows.ResultFor(Time.FromMilliseconds(milliseconds));
        return result == BmsResults.EMPTY_POOR ? HitResult.None : BmsGameplayProfile.INSTANCE.HitResultFor(result);
    }

    internal static bool EmptyPoorAt(this BmsTimingDefinition timing, double milliseconds)
        => timing.Windows.ResultFor(Time.FromMilliseconds(milliseconds)) == BmsResults.EMPTY_POOR;

    internal static bool PastDeadline(this BmsTimingDefinition timing, double milliseconds)
        => Time.FromMilliseconds(milliseconds) > timing.PassiveDeadline;

    internal static void ApplyTestResult(this BmsScoreProcessor processor, JudgementResult result)
    {
        var bms = (BmsJudgementResult)result;
        bms.EngineResult = scoreResult(processor, bms);
        processor.ApplyResult(bms);
    }

    internal static void ApplyTestResult(this BmsHealthProcessor processor, JudgementResult result)
    {
        var bms = (BmsJudgementResult)result;
        var (note, judgement) = describe(bms);
        var compiled = healthChart(processor, note);
        var health = processor.CreateEngineHealth(compiled);
        if (note is BmsLandmine)
            judgement = judgement with { NoteId = compiled.Ids[note] };
        var score = bms.EngineResult?.Score ?? new ScoreState();
        bms.EngineResult = new EngineResult(judgement, score,
            health.Reduce(new HealthContext(judgement, health.InitialState, score, chartFor(note).ScorePlan)));
        processor.ApplyResult(bms);
    }

    internal static void ApplyTestNonConsumingJudgement(this BmsScoreProcessor processor, HitResult result, double time, double expected, int column)
    {
        var note = new BmsNote { StartTime = expected, Column = column, Beatmap = (BmsBeatmap)processor.Beatmap.Value! };
        var judgement = new JudgementEvent(0, 0, column, NoteParts.EMPTY, BmsGameplayProfile.ResultIdFor(result),
            Time.FromMilliseconds(time), Time.FromMilliseconds(expected), Time.FromMilliseconds(time - expected));
        var configuration = processor.EngineChart.Score;
        var state = configuration.Reduce(scoreState(processor), new ScoreContext(judgement, null, chartFor(note).ScorePlan));
        processor.RegisterNonConsumingJudgement(result, time, expected, column, new EngineResult(judgement, state, new HealthState()));
    }

    internal static void ApplyTestEmptyPoor(this BmsScoreProcessor processor, double time = 0)
        => processor.ApplyTestEmptyPoor(time, time, 0);

    internal static void ApplyTestEmptyPoor(this BmsScoreProcessor processor, double time, double expected, int column)
        => processor.ApplyTestNonConsumingJudgement(HitResult.Miss, time, expected, column);

    internal static void ApplyTestEmptyPoor(this BmsHealthProcessor processor)
        => processor.ApplyTestNonConsumingJudgement(HitResult.Miss);

    internal static void ApplyTestNonConsumingJudgement(this BmsHealthProcessor processor, HitResult result)
        => reduceHealth(processor, NoteParts.EMPTY, BmsGameplayProfile.ResultIdFor(result));

    internal static void ApplyTestBodyTick(this BmsHealthProcessor processor, bool holding)
        => reduceHealth(processor, NoteParts.BODY, holding ? BmsResults.SUSTAIN_GAIN : BmsResults.SUSTAIN_LOSS);

    internal static BmsLongNoteJudgementResult ApplyTestSyntheticEndpoint(
        this BmsScoreProcessor processor,
        BmsLongNoteEndpointResult endpoint, bool suppressPenalty = false)
    {
        var source = BmsTestJudgements.CreateEndpoint(endpoint.Source, endpoint.ExpectedTime);
        var result = new BmsLongNoteJudgementResult(source, source.CreateJudgement(), [endpoint])
        {
            Type = endpoint.Result,
            SuppressPenalty = suppressPenalty,
        };
        result.SetTiming(endpoint.ApplicationTime ?? endpoint.EventTime, endpoint.GameplayRate);
        return processor.ApplySyntheticLongNoteEndpoint(endpoint, suppressPenalty, scoreResult(processor, result));
    }

    private static EngineResult scoreResult(BmsScoreProcessor processor, BmsJudgementResult result)
    {
        var (note, judgement) = describe(result);
        var chart = chartFor(note);
        var configuration = processor.EngineChart.Score;
        var opportunity = chart.ScorePlan.Opportunities.SingleOrDefault(o => o.Part == judgement.Part);
        var state = configuration.Reduce(scoreState(processor), new ScoreContext(judgement, opportunity, chart.ScorePlan));
        return new EngineResult(judgement, state, default);
    }

    private static ScoreState scoreState(BmsScoreProcessor processor)
    {
        var statistics = processor.GetScoreProcessorStatistics();
        var mines = statistics.AccuracyJudgementCount - processor.ScoringJudgementEventCount;
        var counters = processor.Statistics.Where(p => p.Key is HitResult.Perfect or HitResult.Great or HitResult.Good or HitResult.Ok or HitResult.Meh or HitResult.Miss)
            .ToImmutableDictionary(p => BmsGameplayProfile.ResultIdFor(p.Key), p => p.Value);
        counters = counters.SetItem(BmsResults.POOR, counters.GetValueOrDefault(BmsResults.POOR) - mines)
            .SetItem(BmsResults.MINE_HIT, mines);
        return new ScoreState
        {
            Score = statistics.BaseScore, AccuracyPoints = statistics.BaseScore, AccuracyMaximum = statistics.MaximumBaseScore,
            Combo = processor.Combo.Value, MaximumCombo = processor.HighestCombo.Value, ResultCounts = counters, JudgedOpportunities = processor.ScoringJudgementEventCount,
        };
    }

    private static (BmsHitObject Note, JudgementEvent Event) describe(BmsJudgementResult result)
    {
        var endpoint = (result as BmsLongNoteJudgementResult)?.EndpointResults.LastOrDefault();
        var note = endpoint?.Source ?? (BmsHitObject)result.HitObject;
        var part = note is BmsLandmine ? NoteParts.MINE
            : endpoint is { } e ? e.Kind == BmsLongNoteEndpointKind.Head ? NoteParts.HEAD : NoteParts.TAIL
            : NoteParts.TAP;
        // Isolated processor fixtures may supply a note before assigning its containing beatmap.
        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        note.Beatmap ??= new BmsBeatmap { TotalColumns = note.Column + 1, HitObjects = { note } };
        var judgement = new JudgementEvent(0, 0, note.Column, part,
            note is BmsLandmine ? BmsResults.MINE_HIT : BmsGameplayProfile.ResultIdFor(result.Type),
            Time.FromMilliseconds(result.EventTime), Time.FromMilliseconds(note.StartTime), Time.FromMilliseconds(result.TimeOffset))
        {
            SuppressPenalty = result.SuppressPenalty,
        };
        return (note, judgement);
    }

    private static Chart chartFor(BmsHitObject note) => new BmsEngineChart([note], note.Beatmap.LayoutVariant, [note.Column]).Chart;

    private static void reduceHealth(BmsHealthProcessor processor, PartId part, ResultId result)
    {
        var beatmap = new BmsBeatmap { TotalColumns = 1, LockedLongNoteMode = BmsLongNoteMode.HellChargeNote };
        BmsHitObject note = part == NoteParts.BODY ? new BmsLongNote { Kind = KeyNoteKind.SustainHold, Duration = 1000 } : new BmsNote();
        note.Beatmap = beatmap;
        beatmap.HitObjects.Add(note);
        var compiled = healthChart(processor, note);
        var health = processor.CreateEngineHealth(compiled);
        var judgement = new JudgementEvent(0, 0, 0, part == NoteParts.EMPTY && result != BmsResults.EMPTY_POOR ? NoteParts.TAP : part, result, 200001, 0, null);
        processor.ApplyEngineTick(new EngineResult(judgement, new ScoreState(), health.Reduce(new HealthContext(judgement, health.InitialState, new ScoreState(), chartFor(note).ScorePlan))));
    }
}
