using System;
using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal sealed partial class BmsColumnJudgementContext
{
    private bool[] automaticHolds = [];
    private readonly List<BmsScheduledObject> automaticPlan = [];
    private readonly List<int> automaticOrder = [];
    private Comparison<int>? compareAutomaticOrder;
    private bool automaticOrderDirty = true;
    private (long Epoch, long Step) lastAutomaticStep = (-1, -1);

    internal bool AutoScratchEnabled { get; private set; }

    internal void EnableAutoScratch()
    {
        if (!IsScratch || AutoScratchEnabled)
            return;

        AutoScratchEnabled = true;
        automaticHolds = new bool[holdStateSlots.Length];
        compareAutomaticOrder = (left, right) =>
        {
            var timeOrder = NoteRegistrations[left].Object.StartTime.CompareTo(NoteRegistrations[right].Object.StartTime);
            return timeOrder != 0 ? timeOrder : activeNoteSlots.IndexOf(left).CompareTo(activeNoteSlots.IndexOf(right));
        };
        pressedDirections = 0;
    }

    internal bool IsAutomaticallyHeld(BmsLongNote note)
        => AutoScratchEnabled && automaticHolds[holdOperationSlots[noteSlots[note]]] && GetLongNoteState(note).IsHolding;

    internal bool IsHoldInputActive(BmsLongNote note) => pressedDirections != 0 || IsAutomaticallyHeld(note);

    internal void ResetAutomaticHold(BmsHitObject note)
    {
        if (AutoScratchEnabled && note is BmsLongNote)
            automaticHolds[holdOperationSlots[noteSlots[note]]] = false;
    }

    private void restoreAutomaticHold(BmsLongNote note)
    {
        if (AutoScratchEnabled)
            automaticHolds[holdOperationSlots[noteSlots[note]]] = GetLongNoteState(note).IsHolding;
    }

    internal void PrepareAutomaticHolds()
    {
        if (!AutoScratchEnabled)
            return;
        // A restored or rebound hold must be available to body sampling before this step's new endpoints.
        foreach (var slot in pendingLongNoteTailSlots)
            restoreAutomaticHold(longNotes[slot]);
    }

    internal void UpdateAutoScratch(in BmsFrameContext frame)
    {
        if (!AutoScratchEnabled || frame.IsRewinding || lastAutomaticStep == (Schedule.Epoch, Schedule.Step))
            return;
        lastAutomaticStep = (Schedule.Epoch, Schedule.Step);

        if (automaticOrderDirty)
        {
            automaticOrder.Clear();
            automaticOrder.AddRange(activeNoteSlots);
            // Match the framework's stable start-time ordering without sorting on every simulation slice.
            automaticOrder.Sort(compareAutomaticOrder!);
            automaticOrderDirty = false;
        }

        // Bindings protect this simulation batch from synchronous removal or pool reuse in result callbacks.
        automaticPlan.Clear();
        foreach (var slot in automaticOrder)
        {
            if (Schedule.TryGetCurrent(NoteRegistrations[slot].Object, out var item))
                automaticPlan.Add(item);
        }

        foreach (var item in automaticPlan)
        {
            if (!Schedule.TryGetCurrent(item.Object, out var current) || current != item)
                continue;

            var note = item.Object;
            var slot = noteSlots[note];
            if (frame.SimulationTime < note.StartTime)
            {
                ResetAutomaticHold(note);
                continue;
            }

            if (note is BmsLongNote hold)
            {
                var state = GetLongNoteState(hold);
                if (state.IsHolding)
                {
                    restoreAutomaticHold(hold);
                    if (frame.SimulationTime >= hold.EndTime)
                    {
                        var completed = automaticTailOperations[holdOperationSlots[slot]](slot, frame);
                        if (completed)
                            noteOutput.PlayTailSample(hold);
                        if (!GetLongNoteState(hold).IsHolding)
                            ResetAutomaticHold(hold);
                    }
                    continue;
                }
                ResetAutomaticHold(hold);
                if (state.HeadJudged)
                    continue;
            }
            if (noteOutput.GetMainResult(note).HasResult)
                continue;

            var table = BmsJudgementProfileProvider.GetTable(LayoutVariant, Index, note.EffectiveJudgementRate, tail: false);
            var result = table.ResultForOffset(frame.SimulationTime - note.StartTime);
            if (result == HitResult.None)
                continue;

            // Taps play their keysound before the result; holds play it after the head has started.
            if (note is BmsNote)
                noteOutput.PlaySample(note);
            if (TryPressRegisteredNote(note, result, new BmsPressContext(frame.SimulationTime, frame)) && note is BmsLongNote started)
            {
                noteOutput.PlaySample(started);
                if (Schedule.TryGetCurrent(note, out current) && current == item)
                    restoreAutomaticHold(started);
            }
        }
    }
}
