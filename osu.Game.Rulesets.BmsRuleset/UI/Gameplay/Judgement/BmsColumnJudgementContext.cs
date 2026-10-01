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
    private readonly BmsPlayfield playfield = null!;
    private readonly BmsNotePressHistory pressHistory = new();
    private readonly BmsHitObject[] columnNotes;
    private readonly bool canBatchUpdates;
    private readonly List<BmsJudgementCandidate> pressJudgementCandidates = [];
    private readonly List<DrawableBmsHitObject> activeLongNotes = [];
    private int pressedDirections;
    private bool passiveDeadlineDirty = true;

    internal BmsColumnJudgementContext(BmsPlayfield playfield, BmsColumn column)
    {
        this.playfield = playfield;
        Column = column;
        Container = (BmsColumnHitObjectContainer)column.HitObjectContainer;
        canBatchUpdates = column.CanBatchUpdates;
        columnNotes = playfield.Beatmap.HitObjects.Where(h => h.Column == Index && h is not BmsLandmine and not BmsInvisibleNote)
                              .OrderBy(h => h.StartTime).ToArray();
        drawableNoteOutput = new BmsDrawableJudgementOutput(playfield, column);
        initialiseNoteExecution(drawableNoteOutput);
    }

    internal BmsColumn Column { get; } = null!;
    internal BmsColumnHitObjectContainer Container { get; } = null!;
    internal int Index => Column?.Index ?? noteColumnIndex;
    private bool IsScratch => Column?.IsScratch ?? BmsLayout.IsScratchColumn(Index, LayoutVariant);
    private BmsLayoutVariant LayoutVariant => Column?.LayoutVariant ?? noteLayout;
    internal IReadOnlyList<DrawableBmsHitObject> ActiveLongNotes => activeLongNotes;
    internal double NextPassiveUpdateTime { get; private set; }

    internal bool HasPmsMistake(BmsHitObject note) => BmsJudgementProfileProvider.IsPms(LayoutVariant) && pressHistory.Get(note).Mistake;

    internal void RecordResult(BmsHitObject note, double time) => pressHistory.Record(note, time, true);

    internal void RewindPressHistory(double time) => pressHistory.Rewind(time);

    internal void RefreshPassiveDeadline(double now) => refreshNoteDeadline(now);

    internal void ObserveLongNoteState(DrawableBmsHitObject drawable, BmsLongNoteStateObserver observer)
        => drawableNoteOutput!.ObserveLongNoteState(drawable, observer);

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
        if (!RequiresActiveLongNoteUpdate(activeLongNotes[index].HitObject))
            activeLongNotes.RemoveAt(index);
    }

    internal void Register(DrawableBmsHitObject note)
    {
        if (!noteSlots.ContainsKey(note.HitObject))
            return;

        drawableNoteOutput!.Bind(note);
        ActivateNote(note.HitObject);
        bindScheduledRepresentation(note);
        refreshActiveNoteOrder();
    }

    internal void Unregister(DrawableBmsHitObject note)
    {
        if (!noteSlots.ContainsKey(note.HitObject))
            return;

        BeginNoteRemoval(note.HitObject);
        if (TryGetScheduledObject(note, out var scheduled))
            Schedule.BeginRemoval(scheduled);
        refreshActiveNoteOrder();
    }

    private void trackActiveLongNote(DrawableBmsHitObject note)
    {
        if (RequiresActiveLongNoteUpdate(note.HitObject) && !activeLongNotes.Contains(note))
            activeLongNotes.Add(note);
    }

    internal void UpdateHellChargeBodies(bool holding)
    {
        for (var i = activeLongNotes.Count - 1; i >= 0; i--)
            RetireInactiveLongNoteAt(i);
        updateDrawableBodies(holding);
    }

    internal void CompleteLongNotesAtPause(double currentTime)
    {
        foreach (var note in GetHoldingNotes().ToArray())
            longNoteBinding!.CompleteAtPause(noteSlots[note], new BmsFrameContext(currentTime,
                (Column.Clock as IGameplayClock)?.GetTrueGameplayRate() ?? Column.Clock.Rate));
    }

    internal void RewindLongNotes(double time)
    {
        Schedule.Rewind();
        // Inactive objects retain history so pool activation cannot resurrect a future head.
        foreach (var note in longNotes)
        {
            if (note != null)
                longNoteBinding!.Rewind(noteSlots[note], time);
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
