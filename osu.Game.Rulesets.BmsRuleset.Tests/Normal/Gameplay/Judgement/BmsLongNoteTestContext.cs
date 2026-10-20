#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.Presentation;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

/// <summary>
/// Drives the production column operations without creating a Drawable or a second rule implementation.
/// </summary>
internal sealed class BmsLongNoteTestContext
{
    internal BmsLongNote Note { get; }
    internal BmsColumnJudgementRuntime Column { get; private set; }
    internal BmsRecordingJudgementOutput Output { get; } = new();
    internal BmsLongNoteState State => Column.GetLongNoteState(Note);
    internal List<(HitResult Result, IReadOnlyList<BmsLongNoteEndpointResult> Endpoints)> MainResults { get; } = [];
    internal List<(BmsLongNoteState State, BmsFrameContext Frame)> Publications { get; } = [];
    internal IEnumerable<HitResult> Results => MainResults.Select(r => r.Result);
    internal IEnumerable<(double endpointTime, double eventTime, HitResult result)> MainEndpoints
        => MainResults.SelectMany(r => r.Endpoints.Select(e => (e.ExpectedTime, e.EventTime, e.Result)));
    internal IEnumerable<(double endpointTime, double eventTime, HitResult result)> SyntheticEndpoints
        => Output.SyntheticEndpoints.Select(e => (e.ExpectedTime, e.EventTime, e.Result));
    internal int HeadPublications { get; private set; }
    internal int FinishedPublications => Publications.Count(p => !p.Frame.IsRewinding && p.State.TailJudged && !p.State.LongNoteStarted);
    private BmsLongNoteState lastState;
    private int sampleCount;
    private readonly List<Operation> operations = [];
    private bool replaying;
    private double currentTime = double.NegativeInfinity;
    internal Action? BeforeRebuild;
    internal Action<double>? BeforeOperation;

    internal BmsLongNoteTestContext(BmsLongNoteMode mode, double start = 1000, double duration = 500,
                                   int column = 1, int rank = 2, BmsLayoutVariant layout = BmsLayoutVariant.Bme7K)
        : this(new BmsLongNote
        {
            StartTime = start, Duration = duration, Column = column,
            Beatmap = new BmsBeatmap { LayoutVariant = layout, TotalColumns = layout == BmsLayoutVariant.Pms9K ? 9 : 8, Rank = rank, LockedLongNoteMode = mode },
        })
    {
    }

    internal BmsLongNoteTestContext(BmsLongNote note)
    {
        Note = note;
        Column = new BmsJudgementEngine([note], note.Beatmap.LayoutVariant, note.Column, Output).GetColumn(note.Column);

        Output.BeforeResultCheckpoint = (_, _) => MainResults.Add((Output.GetMainResult(Note).Type, Output.GetEndpoints(Note).ToArray()));
        Output.StatePublished = (_, state, frame) =>
        {
            Publications.Add((state, frame));
            if (!frame.IsRewinding && state.HeadJudged && !lastState.HeadJudged && state.HeadResult != HitResult.Meh)
                HeadPublications++;
            lastState = state;
        };
        Output.Feedback = feedback =>
        {
            if (feedback.StartsWith("sample:")) sampleCount++;
        };
    }

    internal bool Press(double time, HitResult result, double rate = 1)
    {
        record(time, () => Press(time, result, rate));
        Column.PressedDirections = 1;
        return Column.TryPressRegisteredNote(Note, result, new BmsPressContext(time, new BmsFrameContext(time, rate)));
    }

    internal bool Repress(double time)
    {
        record(time, () => Repress(time));
        return Column.HandleNotePress(new BmsPressContext(time, new BmsFrameContext(time, 1))) == PressOutcome.Hit;
    }

    internal bool Release(double inputTime, double? simulationTime = null)
    {
        record(simulationTime ?? inputTime, () => Release(inputTime, simulationTime));
        // These rule cases specify a physical release independently of earlier input batches.
        Column.PressedDirections = 1;
        var before = sampleCount;
        Column.HandleExecutionRelease(inputTime, new BmsFrameContext(simulationTime ?? inputTime, 1));
        return sampleCount != before;
    }

    internal void Frame(double time, double elapsed, bool holding)
    {
        if (!replaying && time < currentTime)
        {
            Rewind(time);
            return;
        }
        record(time, () => Frame(time, elapsed, holding), holding, time - elapsed);
        Column.UpdateRegisteredLongNote(Note, new BmsFrameContext(time, 1), elapsed, holding);
    }

    internal void Passive(double time)
    {
        record(time, () => Passive(time));
        Column.CheckRegisteredNotePassive(Note, new BmsFrameContext(time, 1));
    }

    internal void End(double time)
    {
        record(time, () => End(time));
        Column.CompleteLongNotesAtEnd(new BmsFrameContext(time, 1));
    }

    internal void Rewind(double time)
    {
        var previous = operations.ToArray();
        operations.Clear();
        BeforeRebuild?.Invoke();
        Output.Reset();
        MainResults.Clear();
        Publications.Clear();
        HeadPublications = sampleCount = 0;
        lastState = default;
        currentTime = double.NegativeInfinity;
        Column = new BmsJudgementEngine([Note], Note.Beatmap.LayoutVariant, Note.Column, Output).GetColumn(Note.Column);
        replaying = true;
        try
        {
            foreach (var operation in previous)
            {
                if (operation.Time > time)
                {
                    if (operation.StartTime <= time && operation.Holding is { } holding)
                    {
                        var elapsed = time - operation.StartTime.Value;
                        operations.Add(new Operation(time, () => Frame(time, elapsed, holding), holding, operation.StartTime));
                        Frame(time, elapsed, holding);
                    }
                    break;
                }
                operation.Apply();
                operations.Add(operation);
            }
        }
        finally
        {
            replaying = false;
            currentTime = time;
        }
    }

    private void record(double time, Action apply, bool? holding = null, double? startTime = null)
    {
        BeforeOperation?.Invoke(time);
        currentTime = time;
        if (!replaying) operations.Add(new Operation(time, apply, holding, startTime));
    }

    private sealed record Operation(double Time, Action Apply, bool? Holding, double? StartTime);

    internal BmsLongNotePresentation Presentation(bool held = false, double time = 0)
        => BmsLongNotePresentation.SelectMapping(Note.Beatmap.LockedLongNoteMode)(Note, State, new BmsLongNotePresentationContext(time, held));
}
