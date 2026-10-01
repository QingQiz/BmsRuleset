using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal sealed partial class BmsColumnJudgementContext
{
    private BmsPlayfield playfield = null!;
    private readonly BmsNotePressHistory pressHistory = new();
    private readonly BmsHitObject[] columnNotes;
    private readonly bool canBatchUpdates;
    private readonly List<BmsJudgementCandidate> pressJudgementCandidates = [];
    private readonly List<BmsLongNote> activeLongNotes = [];
    private int pressedDirections;
    private bool passiveDeadlineDirty = true;

    internal BmsJudgementRules Rules { get; }

    internal void AttachRepresentationServices(BmsPlayfield playfield, BmsColumn column)
    {
        this.playfield = playfield;
        Column = column;
        Container = (BmsColumnHitObjectContainer)column.HitObjectContainer;
        drawableNoteOutput = (BmsDrawableJudgementOutput)noteOutput;
        column.Judgements = this;
        Container.Judgements = this;
    }

    internal BmsColumn Column { get; private set; } = null!;
    internal BmsColumnHitObjectContainer Container { get; private set; } = null!;
    internal int Index => noteColumnIndex;
    internal bool IsScratch => BmsLayout.IsScratchColumn(Index, LayoutVariant);
    private BmsLayoutVariant LayoutVariant => noteLayout;
    internal IReadOnlyList<BmsLongNote> ActiveLongNotes => activeLongNotes;
    internal double NextPassiveUpdateTime { get; private set; }
    internal void ApplyEmptyPoor(double expectedTime, in BmsFrameContext frame) => noteOutput.ApplyEmptyPoor(expectedTime, Index, frame);

    internal bool HasPmsMistake(BmsHitObject note) => BmsJudgementProfileProvider.IsPms(LayoutVariant) && pressHistory.Get(note).Mistake;

    internal void RecordResult(BmsHitObject note, double time) => pressHistory.Record(note, time, true);

    internal void RewindPressHistory(double time) => pressHistory.Rewind(time);

    internal void RefreshPassiveDeadline(double now) => refreshNoteDeadline(now);

    internal void ObserveLongNoteState(DrawableBmsHitObject drawable, BmsLongNoteStateObserver observer)
        => drawableNoteOutput!.ObserveLongNoteState(drawable, observer);

    internal void ObserveLandmineState(DrawableBmsHitObject drawable, BmsLandmineStateObserver observer)
        => drawableNoteOutput!.ObserveLandmineState(drawable, observer);

    internal DrawableBmsHitObject GetRepresentation(BmsHitObject note) => drawableNoteOutput!.GetRepresentation(note);

    internal DrawableBmsHitObject? FirstPendingHead
        => FirstPendingNote is { } note ? drawableNoteOutput!.GetRepresentation(note) : null;

    internal bool RequiresActiveLongNoteUpdate(BmsHitObject note)
    {
        if (note is not BmsLongNote longNote)
            return false;
        var state = GetLongNoteState(longNote);
        return state.LongNoteStarted || state.HasPendingTail || state.HasActiveBody;
    }

    internal void RetireInactiveLongNoteAt(int index)
    {
        if (!RequiresActiveLongNoteUpdate(activeLongNotes[index]))
            activeLongNotes.RemoveAt(index);
    }

    internal void Register(DrawableBmsHitObject note)
    {
        if (note.HitObject is BmsLandmine mine)
        {
            drawableNoteOutput!.Bind(note);
            ActivateLandmine(mine);
            refreshActiveNoteOrder();
            return;
        }
        if (!noteSlots.ContainsKey(note.HitObject))
            return;

        drawableNoteOutput!.Bind(note);
        ActivateNote(note.HitObject);
        bindScheduledRepresentation(note);
        refreshActiveNoteOrder();
    }

    internal void Unregister(DrawableBmsHitObject note)
    {
        if (note.HitObject is BmsLandmine mine)
        {
            RemoveLandmine(mine);
            return;
        }
        if (!noteSlots.ContainsKey(note.HitObject))
            return;

        BeginNoteRemoval(note.HitObject);
        if (TryGetScheduledObject(note, out var scheduled))
            Schedule.BeginRemoval(scheduled);
        refreshActiveNoteOrder();
    }

    private void trackActiveLongNote(BmsLongNote note)
    {
        if (RequiresActiveLongNoteUpdate(note) && !activeLongNotes.Contains(note))
            activeLongNotes.Add(note);
    }

    internal void CompleteLongNotesAtPause(in BmsFrameContext frame)
    {
        foreach (var note in GetHoldingNotes().ToArray())
            pauseOperations[holdOperationSlots[noteSlots[note]]](noteSlots[note], frame);
    }

    internal void RewindLongNotes(double time)
    {
        Schedule.Rewind();
        // Inactive objects retain history so pool activation cannot resurrect a future head.
        foreach (var note in longNotes)
        {
            if (note != null)
                rewindOperations[holdOperationSlots[noteSlots[note]]](noteSlots[note], time);
        }
    }

    internal void RebuildRewoundIndexes()
        => RestoreExecutionState(new BmsFrameContext(Column.Time.Current,
            (Column.Clock as IGameplayClock)?.GetTrueGameplayRate() ?? Column.Clock.Rate));

    internal int PressedDirections
    {
        get => pressedDirections;
        set => pressedDirections = value;
    }

    internal bool LastPressWasReverseScratch { get; private set; }

    internal PressOutcome HandlePress(double time, bool reverseScratch = false)
        => HandleNotePress(new BmsPressContext(time,
            new BmsFrameContext(Column.Time.Current, (Column.Clock as IGameplayClock)?.GetTrueGameplayRate() ?? Column.Clock.Rate,
                (Column.Clock as IGameplayClock)?.IsRewinding == true || Column.Time.Elapsed < 0), reverseScratch, playfield.IsResumeRewinding),
            playfield.JudgementAlgorithm);

    internal void HandleRelease(double time, bool reverseScratch = false)
        => HandleExecutionRelease(time, new BmsFrameContext(Column.Time.Current,
            (Column.Clock as IGameplayClock)?.GetTrueGameplayRate() ?? Column.Clock.Rate,
            (Column.Clock as IGameplayClock)?.IsRewinding == true || Column.Time.Elapsed < 0), reverseScratch);
}
