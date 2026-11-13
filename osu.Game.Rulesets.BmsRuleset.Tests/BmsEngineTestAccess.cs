#nullable enable
using System.Collections.Generic;
using System;
using System.Linq;
using System.Reflection;
using RhythmEngine;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.Presentation;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

internal static class BmsEngineTestAccess
{
    internal static JudgementSession Session(this BmsJudgementEngine engine)
        => (JudgementSession)typeof(BmsJudgementEngine).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;

    internal static BmsJudgementEngine Engine(this BmsJudgementView column)
        => (BmsJudgementEngine)typeof(BmsJudgementView).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(BmsJudgementEngine)).GetValue(column)!;

    internal static IEnumerable<BmsHitObject> Notes(this BmsJudgementView column)
        => ((BmsHitObject[])typeof(BmsJudgementEngine).GetField("objects", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(column.Engine())!).Where(n => n.Column == column.Index && n is BmsNote or BmsLongNote);

    internal static IEnumerable<BmsHitObject> GetNoteCandidates(this BmsJudgementView column)
        => column.Notes().Where(n => !column.IsKeySoundFinished(n, 0));

    internal static BmsHitObject? FirstPendingNote(this BmsJudgementView column)
        => column.Notes().FirstOrDefault(n => !column.GetNote(n).Parts[0].Resolved);

    internal static IEnumerable<BmsLongNote> GetPendingLongNoteTails(this BmsJudgementView column)
        => column.Notes().OfType<BmsLongNote>().Where(n => column.GetNote(n).Parts[0].Resolved && !column.GetNote(n).Parts[2].Resolved);

    internal static IEnumerable<BmsLongNote> GetHoldingNotes(this BmsJudgementView column)
        => column.GetPendingLongNoteTails().Where(n => column.GetNote(n).Parts.Single(p => p.Part == "body").Active);

    internal static IEnumerable<BmsLongNote> GetActiveBodies(this BmsJudgementView column)
        => column.Notes().OfType<BmsLongNote>().Where(n => n.EffectiveMode == BmsLongNoteMode.HellChargeNote && column.GetNote(n).Parts[0].Resolved && !column.GetNote(n).Parts[1].Resolved);

    internal static BmsLongNoteState GetLongNoteState(this BmsJudgementView column, BmsLongNote note)
        => BmsLongNoteState.From(note, column.GetNote(note), column.Engine().CurrentTime);

    internal static PressOutcome HandleNotePress(this BmsJudgementView column, in BmsPressContext press,
        BmsJudgementAlgorithm? algorithm = BmsJudgementAlgorithm.Combo)
    {
        var engine = column.Engine();
        var sessionField = typeof(BmsJudgementEngine).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var start = ((JudgementSession?)sessionField.GetValue(engine))?.TransitionCount ?? 0;
        engine.QueueInput(press.InputTime, column.Index, true, press.ReverseScratch);
        engine.ProcessFrame(press.Frame with { SimulationTime = Math.Max(press.InputTime, press.Frame.SimulationTime) }, 0, algorithm);
        var transitions = ((JudgementSession)sessionField.GetValue(engine)!).GetTransitionsSince(start)
            .Where(t => t.Input is { Pressed: true }).ToArray();
        var empty = transitions.SelectMany(t => t.Judgements).FirstOrDefault(j => j.Event.Part == "empty" && j.Event.Result == "empty-poor");
        return empty != null ? PressOutcome.ForEmptyPoor(empty.Event.ExpectedTime / 1000.0, column.Index)
            : transitions.Length > 0 ? PressOutcome.Hit : PressOutcome.Empty;
    }

    internal static void HandleExecutionRelease(this BmsJudgementView column, double time, in BmsFrameContext frame, bool reverseScratch = false)
    {
        var engine = column.Engine();
        engine.QueueInput(time, column.Index, false, reverseScratch);
        engine.ProcessFrame(frame with { SimulationTime = Math.Max(time, frame.SimulationTime) }, 0);
    }
}

internal readonly record struct BmsMainResultState(HitResult Type, double TimeAbsolute, double TimeOffset, double? GameplayRate, bool SuppressPenalty)
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
