using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.Presentation;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

/// <summary>
/// Drives the production column operations without creating a Drawable or a second rule implementation.
/// </summary>
internal sealed class BmsLongNoteTestContext
{
    internal BmsLongNote Note { get; }
    internal BmsColumnJudgementRuntime Column { get; }
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
        Column = new BmsJudgementEngine([note], note.Beatmap.LayoutVariant, note.Column, true, Output).GetColumn(note.Column);
        Column.ActivateNote(note);
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
        Column.PressedDirections = 1;
        return Column.TryPressRegisteredNote(Note, result, new BmsPressContext(time, new BmsFrameContext(time, rate)));
    }

    internal bool Repress(double time) => Column.HandleNotePress(new BmsPressContext(time, new BmsFrameContext(time, 1))) == PressOutcome.Hit;

    internal bool Release(double inputTime, double? simulationTime = null)
    {
        // These rule cases specify a physical release independently of earlier input batches.
        Column.PressedDirections = 1;
        var before = sampleCount;
        Column.HandleExecutionRelease(inputTime, new BmsFrameContext(simulationTime ?? inputTime, 1));
        return sampleCount != before;
    }

    internal void Frame(double time, double elapsed, bool holding)
        => Column.UpdateRegisteredLongNote(Note, new BmsFrameContext(time, 1), elapsed, holding);

    internal void Passive(double time) => Column.CheckRegisteredNotePassive(Note, new BmsFrameContext(time, 1));
    internal void Pause(double time) => Column.CompleteLongNotesAtPause(new BmsFrameContext(time, 1));

    internal void Rewind(double time)
    {
        Output.RevertAfter(time);
        Output.RevertSyntheticAfter(time);
        Column.RewindLongNotes(time);
        Column.RestoreExecutionState(new BmsFrameContext(time, 1, true));
    }

    internal BmsLongNotePresentation Presentation(bool held = false, double time = 0)
        => BmsLongNotePresentation.SelectMapping(Note.Beatmap.LockedLongNoteMode)(Note, State, held, time);
}
