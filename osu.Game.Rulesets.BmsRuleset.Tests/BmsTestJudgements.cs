#nullable enable
using System.Collections.Immutable;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.Scoring;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Bms;
using RhythmEngine.Profiles.Common;
using JudgementResult = osu.Game.Rulesets.Judgements.JudgementResult;
using EngineResult = RhythmEngine.Core.JudgementResult;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

/// <summary>Supplies authoritative reducer output to isolated framework processor tests.</summary>
internal static class BmsTestJudgements
{
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
        var health = processor.CreateEngineHealth([note]);
        var score = bms.EngineResult?.Score ?? new ScoreState();
        bms.EngineResult = new EngineResult(judgement, score,
            health.Reduce(new HealthContext(judgement, health.InitialState, score, chartFor(note).ScorePlan)));
        processor.ApplyResult(bms);
    }

    internal static void ApplyTestNonConsumingJudgement(this BmsScoreProcessor processor, HitResult result, double time, double expected, int column)
    {
        var note = new BmsNote { StartTime = expected, Column = column, Beatmap = (BmsBeatmap)processor.Beatmap.Value! };
        var judgement = new JudgementEvent(0, 0, column, NoteParts.EMPTY, BmsEngineChart.ResultId(result),
            Time.FromMilliseconds(time), Time.FromMilliseconds(expected), Time.FromMilliseconds(time - expected));
        var configuration = BmsProfiles.CreateScore(processor.EmptyPoorBreaksCombo
                                                    ?? BmsJudgementProfileProvider.EmptyPoorBreaksCombo(note.Beatmap.LayoutVariant));
        var state = configuration.Reduce(scoreState(processor), new ScoreContext(judgement, null, chartFor(note).ScorePlan));
        processor.RegisterNonConsumingJudgement(result, time, expected, column, state);
    }

    internal static void ApplyTestEmptyPoor(this BmsScoreProcessor processor, double time = 0)
        => processor.ApplyTestEmptyPoor(time, time, 0);

    internal static void ApplyTestEmptyPoor(this BmsScoreProcessor processor, double time, double expected, int column)
        => processor.ApplyTestNonConsumingJudgement(HitResult.Miss, time, expected, column);

    internal static void ApplyTestEmptyPoor(this BmsHealthProcessor processor)
        => processor.ApplyTestNonConsumingJudgement(HitResult.Miss);

    internal static void ApplyTestNonConsumingJudgement(this BmsHealthProcessor processor, HitResult result)
        => reduceHealth(processor, NoteParts.EMPTY, BmsEngineChart.ResultId(result));

    internal static void ApplyTestBodyTick(this BmsHealthProcessor processor, bool holding)
        => reduceHealth(processor, NoteParts.BODY, holding ? BmsResults.SUSTAIN_GAIN : BmsResults.SUSTAIN_LOSS);

    internal static BmsLongNoteJudgementResult ApplyTestSyntheticEndpoint(
        this BmsScoreProcessor processor,
        BmsLongNoteEndpointResult endpoint, bool suppressPenalty = false)
    {
        var source = endpoint.Source.CreateSyntheticEndpoint(endpoint.ExpectedTime);
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
        var configuration = BmsProfiles.CreateScore(processor.EmptyPoorBreaksCombo
                                                    ?? BmsJudgementProfileProvider.EmptyPoorBreaksCombo(note.Beatmap.LayoutVariant));
        var opportunity = chart.ScorePlan.Opportunities.SingleOrDefault(o => o.Part == judgement.Part);
        var state = configuration.Reduce(scoreState(processor), new ScoreContext(judgement, opportunity, chart.ScorePlan));
        return new EngineResult(judgement, state, default);
    }

    private static ScoreState scoreState(BmsScoreProcessor processor)
    {
        var statistics = processor.GetScoreProcessorStatistics();
        var mines = statistics.AccuracyJudgementCount - (int)(statistics.MaximumBaseScore / 2);
        var counters = processor.Statistics.Where(p => p.Key is HitResult.Perfect or HitResult.Great or HitResult.Good or HitResult.Ok or HitResult.Meh or HitResult.Miss)
                                .ToImmutableDictionary(p => BmsEngineChart.ResultId(p.Key).Value, p => (double)p.Value);
        counters = counters.SetItem(BmsResults.POOR.Value, counters.GetValueOrDefault(BmsResults.POOR.Value) - mines)
                           .SetItem(BmsResults.MINE_HIT.Value, mines);
        return new ScoreState
        {
            Score = statistics.BaseScore, AccuracyPoints = statistics.BaseScore, AccuracyMaximum = statistics.MaximumBaseScore,
            Combo = processor.Combo.Value, MaximumCombo = processor.HighestCombo.Value, Counters = counters,
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
            note is BmsLandmine ? BmsResults.MINE_HIT : BmsEngineChart.ResultId(result.Type),
            Time.FromMilliseconds(result.EventTime), Time.FromMilliseconds(note.StartTime), Time.FromMilliseconds(result.TimeOffset))
        {
            SuppressPenalty = result.SuppressPenalty,
        };
        return (note, judgement);
    }

    private static Chart chartFor(BmsHitObject note) => BmsEngineChart.Compile([note], note.Column + 1);

    private static void reduceHealth(BmsHealthProcessor processor, PartId part, ResultId result)
    {
        var beatmap = new BmsBeatmap { TotalColumns = 1, LockedLongNoteMode = BmsLongNoteMode.HellChargeNote };
        BmsHitObject note = part == NoteParts.BODY ? new BmsLongNote { Duration = 1000 } : new BmsNote();
        note.Beatmap = beatmap;
        beatmap.HitObjects.Add(note);
        var health = processor.CreateEngineHealth([note]);
        var judgement = new JudgementEvent(0, 0, 0, part == NoteParts.EMPTY && result != BmsResults.EMPTY_POOR ? NoteParts.TAP : part, result, 200001, 0, null);
        processor.ApplyEngineTick(health.Reduce(new HealthContext(judgement, health.InitialState, new ScoreState(), chartFor(note).ScorePlan)));
    }
}
