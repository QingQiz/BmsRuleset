using System;
using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

internal sealed class BmsAutoScratchController
{
    private readonly bool[] automaticHolds;
    private readonly List<BmsScheduledObject> automaticPlan = [];
    private readonly List<int> automaticOrder = [];
    private readonly Comparison<int> compareAutomaticOrder;
    private bool automaticOrderDirty = true;
    private (long Epoch, long Step) lastAutomaticStep = (-1, -1);

    private readonly BmsColumnJudgementRuntime column;
    private readonly BmsHoldRegistry holds;
    private readonly IBmsJudgementOutput noteOutput;
    internal bool NeedsStep => lastAutomaticStep != (column.Schedule.Epoch, column.Schedule.Step);

    internal BmsAutoScratchController(BmsColumnJudgementRuntime column, BmsHoldRegistry holds, IBmsJudgementOutput output)
    {
        this.column = column;
        this.holds = holds;
        noteOutput = output;
        automaticHolds = new bool[holds.SlotCount];
        compareAutomaticOrder = (left, right) =>
        {
            var timeOrder = column.NoteRegistrations[left].Object.StartTime.CompareTo(column.NoteRegistrations[right].Object.StartTime);
            return timeOrder != 0 ? timeOrder : column.Notes.RankOf(left).CompareTo(column.Notes.RankOf(right));
        };
        column.PressedDirections = 0;
    }

    internal void InvalidateOrder() => automaticOrderDirty = true;

    internal bool IsAutomaticallyHeld(BmsLongNote note)
        => automaticHolds[column.GetNoteSlot(note)] && column.GetLongNoteState(note).IsHolding;

    internal void ResetAutomaticHold(BmsHitObject note)
    {
        if (note is BmsLongNote)
            automaticHolds[column.GetNoteSlot(note)] = false;
    }

    internal void RestoreHold(BmsLongNote note)
    {
        automaticHolds[column.GetNoteSlot(note)] = column.GetLongNoteState(note).IsHolding;
    }

    internal void PrepareAutomaticHolds()
    {
        if (lastAutomaticStep == (column.Schedule.Epoch, column.Schedule.Step))
            return;
        // A restored or rebound hold must be available to body sampling before this step's new endpoints.
        var tails = column.Notes.PendingTails;
        for (var i = 0; i < tails.Count; i++)
            RestoreHold((BmsLongNote)column.NoteRegistrations[tails[i]].Object);
    }

    internal void UpdateAutoScratch(in BmsFrameContext frame)
    {
        if (frame.IsRewinding || lastAutomaticStep == (column.Schedule.Epoch, column.Schedule.Step))
            return;
        lastAutomaticStep = (column.Schedule.Epoch, column.Schedule.Step);

        var order = automaticOrder;
        var plan = automaticPlan;
        if (automaticOrderDirty)
        {
            order.Clear();
            var active = column.Notes.ActiveSlots;
            for (var i = 0; i < active.Count; i++) order.Add(active[i]);
            // Match the framework's stable start-time ordering without sorting on every simulation slice.
            order.Sort(compareAutomaticOrder);
            automaticOrderDirty = false;
        }

        // Bindings protect this simulation batch from synchronous removal or pool reuse in result callbacks.
        plan.Clear();
        foreach (var slot in order)
        {
            if (column.Schedule.TryGetCurrent(column.NoteRegistrations[slot].Object, out var item))
                plan.Add(item);
        }

        foreach (var item in plan)
        {
            if (!column.Schedule.TryGetCurrent(item.Object, out var current) || current != item)
                continue;

            var note = item.Object;
            var slot = column.GetNoteSlot(note);
            if (frame.SimulationTime < note.StartTime)
            {
                ResetAutomaticHold(note);
                continue;
            }

            if (note is BmsLongNote hold)
            {
                var state = column.GetLongNoteState(hold);
                if (state.IsHolding)
                {
                    RestoreHold(hold);
                    if (frame.SimulationTime >= hold.EndTime)
                    {
                        var completed = holds.AutomaticTail(slot, frame);
                        if (completed)
                            noteOutput.PlayTailSample(hold);
                        if (!column.GetLongNoteState(hold).IsHolding)
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

            var table = BmsJudgementProfileProvider.GetTable(column.LayoutVariant, column.Index, note.EffectiveJudgementRate, tail: false);
            var result = table.ResultForOffset(frame.SimulationTime - note.StartTime);
            if (result == HitResult.None)
                continue;

            // Taps play their keysound before the result; holds play it after the head has started.
            if (note is BmsNote)
                noteOutput.PlaySample(note);
            if (column.TryPressRegisteredNote(note, result, new BmsPressContext(frame.SimulationTime, frame)) && note is BmsLongNote started)
            {
                noteOutput.PlaySample(started);
                if (column.Schedule.TryGetCurrent(note, out current) && current == item)
                    RestoreHold(started);
            }
        }
    }
}
