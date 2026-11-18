#nullable enable
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

internal static class BmsEngineTestAccess
{
    internal static BmsJudgementEngine CreateEngine(BmsBeatmap beatmap,
                                                    BmsJudgementColumnDefinition[] definitions, Func<BmsEngineChart, HealthConfiguration>? healthFactory = null, GameplayProfile? profile = null)
        => new(new BmsEngineChart(beatmap, profile), definitions, healthFactory);

    internal static BmsJudgementEngine CreateEngine(BmsBeatmap beatmap, int column, IBmsJudgementOutput output, GameplayProfile? profile = null)
        => CreateEngine(beatmap, [new BmsJudgementColumnDefinition(column, output)], profile: profile);

    internal static JudgementSession Session(this BmsJudgementEngine engine)
        => (JudgementSession)typeof(BmsJudgementEngine).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;

    internal static BmsJudgementEngine Engine(this BmsJudgementView column)
        => (BmsJudgementEngine)typeof(BmsJudgementView).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(BmsJudgementEngine)).GetValue(column)!;

    internal static IEnumerable<BmsHitObject> Notes(this BmsJudgementView column)
        => ((BmsEngineChart)typeof(BmsJudgementEngine).GetField("compiled", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(column.Engine())!).Objects.Where(n => n.Column == column.Index && n is BmsNote or BmsLongNote);

    internal static IEnumerable<BmsHitObject> GetNoteCandidates(this BmsJudgementView column)
        => column.Notes().Where(n => !column.GetNote(n).Parts.Single(part => part.Part == (n is BmsLongNote
            ? column.Engine().ScoresHead(n) ? NoteParts.HEAD : NoteParts.TAIL
            : NoteParts.TAP)).Resolved);

    internal static BmsHitObject? FirstPendingNote(this BmsJudgementView column)
        => column.Notes().FirstOrDefault(n => !column.GetNote(n).Parts[0].Resolved);

    internal static IEnumerable<BmsLongNote> GetPendingLongNoteTails(this BmsJudgementView column)
        => column.Notes().OfType<BmsLongNote>().Where(n => column.GetNote(n).Parts[0].Resolved && !column.GetNote(n).Parts[2].Resolved);

    internal static IEnumerable<BmsLongNote> GetHoldingNotes(this BmsJudgementView column)
        => column.GetPendingLongNoteTails().Where(n => column.GetNote(n).Parts.Single(p => p.Part == NoteParts.BODY).Active);

    internal static IEnumerable<BmsLongNote> GetActiveBodies(this BmsJudgementView column)
        => column.Notes().OfType<BmsLongNote>().Where(n => n.Kind == KeyNoteKind.SustainHold && column.GetNote(n).Parts[0].Resolved && !column.GetNote(n).Parts[1].Resolved);

    internal static BmsLongNoteState GetLongNoteState(this BmsJudgementView column, BmsLongNote note)
        => BmsLongNoteState.From(note, column.GetNote(note), column.Engine().CurrentTime, column.Profile);

    internal static PressOutcome HandleNotePress(this BmsJudgementView column, in BmsPressContext press,
                                                 BmsJudgementAlgorithm? algorithm = BmsJudgementAlgorithm.Combo)
    {
        var engine = column.Engine();
        var outputs = (Dictionary<int, IBmsJudgementOutput>)typeof(BmsJudgementEngine).GetField("outputs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;
        var output = (BmsRecordingJudgementOutput)outputs[column.Index];
        var outcome = PressOutcome.Empty;
        var inputTime = press.InputTime;
        output.NonConsumingJudgement += nonConsuming;
        output.ResultApplied += resultApplied;
        output.StatePublished += statePublished;

        try
        {
            engine.QueueInput(inputTime, column.Index, true, press.ReverseScratch);
            engine.ProcessFrame(press.Frame with { SimulationTime = Math.Max(inputTime, press.Frame.SimulationTime) }, 0, algorithm);
            return outcome;
        }
        finally
        {
            output.NonConsumingJudgement -= nonConsuming;
            output.ResultApplied -= resultApplied;
            output.StatePublished -= statePublished;
        }

        void nonConsuming(double expected, int lane, HitResult result)
            => outcome = result == HitResult.Miss ? PressOutcome.ForEmptyPoor(expected, lane) : PressOutcome.Hit;

        void resultApplied(BmsHitObject note, BmsFrameContext frame)
        {
            if (frame.SimulationTime == inputTime && frame.EngineResult?.Event.Offset != null)
                outcome = PressOutcome.Hit;
        }

        void statePublished(BmsLongNote note, BmsLongNoteState state, BmsFrameContext frame)
        {
            if (frame.SimulationTime == inputTime) outcome = PressOutcome.Hit;
        }
    }

    internal static void HandleExecutionRelease(this BmsJudgementView column, double time, in BmsFrameContext frame, bool reverseScratch = false)
    {
        var engine = column.Engine();
        engine.QueueInput(time, column.Index, false, reverseScratch);
        engine.ProcessFrame(frame with { SimulationTime = Math.Max(time, frame.SimulationTime) }, 0);
    }
}

internal readonly record struct BmsMainResultState(HitResult Type, double TimeOffset)
{
    internal bool HasResult => Type != HitResult.None;
}

internal readonly record struct BmsPressContext(double InputTime, BmsFrameContext Frame, bool ReverseScratch = false);

internal enum PressOutcomeKind
{
    Hit,
    EmptyPoor,
    Empty,
}

internal readonly record struct PressOutcome(PressOutcomeKind Kind, double? ExpectedTime = null, int Column = 0)
{
    public static PressOutcome Hit => new(PressOutcomeKind.Hit);

    public static PressOutcome Empty => new(PressOutcomeKind.Empty);

    public static PressOutcome ForEmptyPoor(double expectedTime, int column) => new(PressOutcomeKind.EmptyPoor, expectedTime, column);
}
