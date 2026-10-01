using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal sealed partial class BmsColumnJudgementContext
{
    private readonly BmsNotePressHistory pressHistory = new();
    private readonly BmsHitObject[] columnNotes;
    private readonly bool canBatchUpdates;
    private readonly List<BmsJudgementCandidate> pressJudgementCandidates = [];
    private readonly List<BmsLongNote> activeLongNotes = [];
    private int pressedDirections;
    private bool passiveDeadlineDirty = true;

    internal BmsJudgementRules Rules { get; }

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

    internal int PressedDirections
    {
        get => pressedDirections;
        set => pressedDirections = value;
    }

    internal bool LastPressWasReverseScratch { get; private set; }

}
