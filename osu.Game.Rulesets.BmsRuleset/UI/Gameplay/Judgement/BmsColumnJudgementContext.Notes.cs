using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal sealed partial class BmsColumnJudgementContext
{
    private readonly int noteColumnIndex;
    private readonly BmsLayoutVariant noteLayout;
    private IBmsJudgementOutput noteOutput = null!;
    private readonly Dictionary<BmsHitObject, int> noteSlots = new(ReferenceEqualityComparer.Instance);
    private readonly List<int> activeNoteSlots = [];
    private readonly SortedSet<(double Time, long Order, int Slot)> pendingNoteHeads = [];
    private readonly List<(int Slot, BmsJudgementCandidate Candidate)> noteCandidates = [];
    private bool[] activeNotes = [];
    private long[] noteOrders = [];
    private double[] noteHeadTimes = [];
    private long nextNoteOrder;
    private readonly List<int> pendingLongNoteTailSlots = [];
    private readonly List<int> activeBodySlots = [];
    internal BmsCoreJudgementSchedule Schedule { get; private set; } = null!;

    internal BmsNoteRegistration[] NoteRegistrations { get; private set; } = [];
    internal BmsNoteJudgementWindows[] NoteWindows { get; private set; } = [];
    private BmsLongNote[] longNotes = [];

    internal BmsColumnJudgementContext(BmsHitObject[] notes, BmsLayoutVariant layout, int column, bool batchUpdates, IBmsJudgementOutput output, BmsJudgementRules rules)
    {
        Rules = rules;
        noteColumnIndex = column;
        noteLayout = layout;
        canBatchUpdates = batchUpdates;
        columnNotes = notes.Where(n => n.Column == column && n is BmsNote or BmsLongNote).OrderBy(n => n.StartTime).ToArray();
        initialiseLandmines(notes);
        initialiseNoteExecution(output);
    }

    private void initialiseNoteExecution(IBmsJudgementOutput output)
    {
        Schedule = new BmsCoreJudgementSchedule(this);
        noteOutput = output;
        var binding = new BmsNoteOperationBinding(this, output);
        var holdCount = columnNotes.Count(n => n is BmsLongNote);
        var containsLongNotes = holdCount > 0;
        var holdIndex = 0;
        LongNoteOperations? lnOperations = null;
        ChargeNoteOperations? cnOperations = null;
        HellChargeNoteOperations? hcnOperations = null;
        if (containsLongNotes)
            initialiseHoldOperations(columnNotes.Length, holdCount);
        NoteRegistrations = new BmsNoteRegistration[columnNotes.Length];
        NoteWindows = new BmsNoteJudgementWindows[columnNotes.Length];
        longNotes = containsLongNotes ? new BmsLongNote[columnNotes.Length] : [];
        activeNotes = new bool[columnNotes.Length];
        noteOrders = new long[columnNotes.Length];
        noteHeadTimes = new double[columnNotes.Length];
        for (var i = 0; i < columnNotes.Length; i++)
        {
            var note = columnNotes[i];
            noteSlots.Add(note, i);
            noteHeadTimes[i] = note.StartTime;
            if (note is BmsLongNote longNote)
            {
                longNotes[i] = longNote;
                holdOperationSlots[i] = holdIndex++;
                switch (holdMode(longNote))
                {
                    case BmsLongNoteMode.LongNote:
                        (lnOperations ??= new LongNoteOperations(this, output, Rules.LongNote)).Register(longNote, i);
                        break;
                    case BmsLongNoteMode.ChargeNote:
                        (cnOperations ??= new ChargeNoteOperations(this, output, Rules.ChargeNote)).Register(longNote, i);
                        break;
                    case BmsLongNoteMode.HellChargeNote:
                        (hcnOperations ??= new HellChargeNoteOperations(this, output, Rules.HellChargeNote)).Register(longNote, i);
                        break;
                    default:
                        throw new InvalidOperationException("Unknown long-note mode.");
                }
            }
            else
            {
                NoteRegistrations[i] = new BmsNoteRegistration(note, i, binding.Press, binding.Passive);
                NoteWindows[i] = new BmsNoteJudgementWindows(note);
            }
        }

        output.SetResultCheckpoint(updateNoteIndexes);
    }

    internal BmsLongNoteState GetLongNoteState(BmsLongNote note) => holdQueries[holdOperationSlots[noteSlots[note]]](noteSlots[note]);

    internal IEnumerable<BmsLongNote> GetPendingLongNoteTails()
    {
        foreach (var slot in pendingLongNoteTailSlots)
            yield return longNotes[slot];
    }

    internal IEnumerable<BmsLongNote> GetActiveBodies()
    {
        foreach (var slot in activeBodySlots)
            yield return longNotes[slot];
    }

    internal IEnumerable<BmsLongNote> GetHoldingNotes()
    {
        foreach (var slot in pendingLongNoteTailSlots)
        {
            var longNote = longNotes[slot];
            if (GetLongNoteState(longNote).IsHolding)
                yield return longNote;
        }
    }

    internal void RefreshExecutionState(BmsLongNote note)
    {
        updateNoteIndexes(note);
        passiveDeadlineDirty = true;
    }

    internal BmsMainResultState GetNoteResult(BmsHitObject note) => noteOutput.GetMainResult(note);

    internal BmsHitObject? FirstPendingNote
    {
        get
        {
            if (!canBatchUpdates || pendingNoteHeads.Count == 0)
                return null;
            var first = NoteRegistrations[pendingNoteHeads.Min.Slot].Object;
            foreach (var slot in pendingLongNoteTailSlots)
            {
                var held = NoteRegistrations[slot].Object;
                if (!noteOutput.GetMainResult(held).HasResult && held.StartTime <= first.StartTime)
                    return null;
            }
            return first;
        }
    }

    private bool hasPendingExecutionHead(BmsHitObject note)
        => note is BmsLongNote ln ? !GetLongNoteState(ln).HeadJudged : !noteOutput.GetMainResult(note).HasResult;

    internal bool IsNoteActive(BmsHitObject note)
        => noteSlots.TryGetValue(note, out var slot) && activeNotes[slot];

    internal bool IsNoteCandidate(BmsHitObject note)
        => noteSlots.TryGetValue(note, out var slot) && activeNotes[slot] && !noteOutput.GetMainResult(note).HasResult;

    internal IEnumerable<BmsHitObject> GetNoteCandidates()
    {
        foreach (var slot in activeNoteSlots)
        {
            var note = NoteRegistrations[slot].Object;
            if (!noteOutput.GetMainResult(note).HasResult)
                yield return note;
        }
    }

    internal void ActivateNote(BmsHitObject note)
    {
        var slot = noteSlots[note];
        if (activeNotes[slot])
            throw new InvalidOperationException("An ordinary note already has an active registration.");

        activeNotes[slot] = true;
        noteOrders[slot] = ++nextNoteOrder;
        activeNoteSlots.Add(slot);
        automaticOrderDirty = true;
        updateNoteIndexes(note);
        passiveDeadlineDirty = true;
    }

    internal void BeginNoteRemoval(BmsHitObject note)
    {
        var slot = noteSlots[note];
        activeNotes[slot] = false;
        activeNoteSlots.Remove(slot);
        automaticOrderDirty = true;
        pendingLongNoteTailSlots.Remove(slot);
        activeBodySlots.Remove(slot);
        if (note is BmsLongNote longNote)
        {
            activeLongNotes.Remove(longNote);
            ResetAutomaticHold(longNote);
        }
        pendingNoteHeads.Remove((noteHeadTimes[slot], noteOrders[slot], slot));
    }

    internal void SetNoteActivationOrder(IEnumerable<BmsHitObject> notes)
    {
        var remaining = activeNoteSlots.Count;
        activeNoteSlots.Clear();
        foreach (var note in notes)
        {
            var slot = noteSlots[note];
            if (!activeNotes[slot] || activeNoteSlots.Contains(slot))
                throw new InvalidOperationException("The activation plan must contain each active note exactly once.");
            activeNoteSlots.Add(slot);
        }
        if (activeNoteSlots.Count != remaining)
            throw new InvalidOperationException("The activation plan omitted an active note.");
        reorderLongNoteSlots();
        automaticOrderDirty = true;
    }

    private void reorderLongNoteSlots()
    {
        reorderSlots(pendingLongNoteTailSlots);
        // The old homogeneous path visits bodies in head-registration order; mixed columns use alive-map order.
        if (!canBatchUpdates)
            reorderSlots(activeBodySlots);
    }

    private void reorderSlots(List<int> slots)
    {
        // Only membership changes need the alive-map ordering; input visits the cached holds.
        for (var i = 1; i < slots.Count; i++)
        {
            var slot = slots[i];
            var order = activeNoteSlots.IndexOf(slot);
            var previous = i - 1;
            while (previous >= 0 && activeNoteSlots.IndexOf(slots[previous]) > order)
            {
                slots[previous + 1] = slots[previous];
                previous--;
            }
            slots[previous + 1] = slot;
        }
    }

    internal void RefreshNoteConfiguration(BmsHitObject note, bool preserveHistory = true)
    {
        if (!noteSlots.TryGetValue(note, out var slot))
            return;

        if (note is BmsNote)
            NoteWindows[slot] = new BmsNoteJudgementWindows(note);
        else if (note is BmsLongNote)
            refreshHoldConfiguration(slot, preserveHistory);
        if (noteHeadTimes[slot] != note.StartTime)
        {
            pendingNoteHeads.Remove((noteHeadTimes[slot], noteOrders[slot], slot));
            noteHeadTimes[slot] = note.StartTime;
            automaticOrderDirty = true;
            // Configuration changes must retain the pre-existing order among equal-time objects.
            columnNotes.OrderBy(n => n.StartTime).ToArray().CopyTo(columnNotes, 0);
        }
        updateNoteIndexes(note);
        passiveDeadlineDirty = true;
    }

    private void updateNoteIndexes(BmsHitObject note)
    {
        if (!noteSlots.TryGetValue(note, out var slot))
            return;
        var key = (noteHeadTimes[slot], noteOrders[slot], slot);
        // Removal precedes the last passive check; a synchronous result must never reactivate it.
        if (canBatchUpdates && activeNotes[slot] && hasPendingExecutionHead(note))
            pendingNoteHeads.Add(key);
        else
            pendingNoteHeads.Remove(key);
        if (note is BmsLongNote ln && activeNotes[slot] && GetLongNoteState(ln).HasPendingTail)
        {
            if (!pendingLongNoteTailSlots.Contains(slot))
            {
                pendingLongNoteTailSlots.Add(slot);
                reorderLongNoteSlots();
            }
        }
        else
            pendingLongNoteTailSlots.Remove(slot);
        if (note is BmsLongNote body && activeNotes[slot] && GetLongNoteState(body).HasActiveBody)
        {
            if (!activeBodySlots.Contains(slot))
            {
                activeBodySlots.Add(slot);
                if (!canBatchUpdates)
                    reorderSlots(activeBodySlots);
            }
        }
        else
            activeBodySlots.Remove(slot);
        // Visual retirement can outlast a POOR tail; it must not keep a judged endpoint selectable.
        if (note is BmsLongNote activeHold && activeNotes[slot])
            trackActiveLongNote(activeHold);
    }

    private void rebuildNoteIndexes()
    {
        pendingNoteHeads.Clear();
        pendingLongNoteTailSlots.Clear();
        activeBodySlots.Clear();
        activeLongNotes.Clear();
        foreach (var slot in activeNoteSlots)
            updateNoteIndexes(NoteRegistrations[slot].Object);
        passiveDeadlineDirty = true;
    }

    internal void RestoreExecutionState(in BmsFrameContext frame)
    {
        foreach (var slot in activeNoteSlots)
        {
            if (NoteRegistrations[slot].Object is BmsLongNote note)
                noteOutput.PublishState(note, holdQueries[holdOperationSlots[noteSlots[note]]](noteSlots[note]), frame);
        }
        rebuildNoteIndexes();
    }

    private void refreshNoteDeadline(double now)
    {
        if (!canBatchUpdates || (!passiveDeadlineDirty && now <= NextPassiveUpdateTime))
            return;

        NextPassiveUpdateTime = double.PositiveInfinity;
        foreach (var slot in activeNoteSlots)
        {
            var note = NoteRegistrations[slot].Object;
            if (hasPendingExecutionHead(note))
                NextPassiveUpdateTime = Math.Min(NextPassiveUpdateTime, note is BmsLongNote ln
                    ? headDeadlines[holdOperationSlots[slot]](slot) : NoteWindows[slot].NextPassiveJudgementTime(note));
        }
        passiveDeadlineDirty = false;
    }

    internal bool TryPressRegisteredNote(BmsHitObject note, HitResult result, in BmsPressContext press)
    {
        var registration = NoteRegistrations[noteSlots[note]];
        return activeNotes[registration.Slot] && registration.Press(registration.Slot, result, press);
    }

    internal void CheckRegisteredNotePassive(BmsHitObject note, in BmsFrameContext frame)
    {
        var registration = NoteRegistrations[noteSlots[note]];
        registration.Passive(registration.Slot, frame);
    }

    internal PressOutcome HandleNotePress(in BmsPressContext press, BmsJudgementAlgorithm? algorithm = BmsJudgementAlgorithm.Combo)
    {
        LastPressWasReverseScratch = IsScratch && press.ReverseScratch;
        pressedDirections |= LastPressWasReverseScratch ? 2 : 1;
        if (press.Frame.IsRewinding)
            return PressOutcome.Empty;

        foreach (var slot in pendingLongNoteTailSlots)
        {
            if (repressOperations[holdOperationSlots[slot]](slot, press))
            {
                var longNote = longNotes[slot];
                if (!GetLongNoteState(longNote).IsHolding)
                    noteOutput.PlayTailSample(longNote);
                return PressOutcome.Hit;
            }
        }

        if (FirstPendingNote is { } first && first.StartTime == press.InputTime && IsNoteCandidate(first)
            && BmsJudgementProfileProvider.GetTable(LayoutVariant, Index, first.EffectiveJudgementRate, false).ResultForOffset(0) == HitResult.Perfect
            && TryPressRegisteredNote(first, HitResult.Perfect, press))
        {
            pressHistory.Record(first, press.InputTime, true);
            noteOutput.PlaySample(first);
            return PressOutcome.Hit;
        }

        noteCandidates.Clear();
        pressJudgementCandidates.Clear();
        foreach (var slot in activeNoteSlots)
        {
            var note = NoteRegistrations[slot].Object;
            if (noteOutput.GetMainResult(note).HasResult)
                continue;

            var candidate = new BmsJudgementCandidate(note.StartTime, note.GetEndTime(), note.Column, note.EffectiveJudgementRate,
                note is BmsLongNote, !hasPendingExecutionHead(note), pressHistory.Get(note).Mistake);
            noteCandidates.Add((slot, candidate));
            pressJudgementCandidates.Add(candidate);
        }

        var low = 0;
        var high = columnNotes.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (columnNotes[middle].StartTime < press.InputTime - 175)
                low = middle + 1;
            else
                high = middle;
        }

        for (var i = low; i < columnNotes.Length && columnNotes[i].StartTime <= press.InputTime + 500; i++)
        {
            var note = columnNotes[i];
            var state = pressHistory.Get(note);
            if (state.Judged)
                pressJudgementCandidates.Add(new BmsJudgementCandidate(note.StartTime, note.GetEndTime(), note.Column,
                    note.EffectiveJudgementRate, note is BmsLongNote, true, state.Mistake));
        }

        var selection = BmsJudgementSelector.SelectPress(LayoutVariant, Index, pressJudgementCandidates, press.InputTime, algorithm);
        if (!selection.IsEmptyPoor && selection.Candidate is { } selected)
        {
            foreach (var candidate in noteCandidates)
            {
                if (!candidate.Candidate.Equals(selected))
                    continue;

                var note = NoteRegistrations[candidate.Slot].Object;
                if (selection.Result == HitResult.Ok && BmsJudgementProfileProvider.IsPms(LayoutVariant))
                {
                    pressHistory.Record(note, press.InputTime, false, true);
                    noteOutput.ApplyNonConsumingBad(note, press.Frame);
                    noteOutput.PlaySample(note);
                    return PressOutcome.Hit;
                }

                if (TryPressRegisteredNote(note, selection.Result, press))
                {
                    pressHistory.Record(note, press.InputTime, true);
                    noteOutput.PlaySample(note);
                    return PressOutcome.Hit;
                }
                break;
            }
        }

        noteOutput.PlayKeySound();
        if (selection is { IsEmptyPoor: true, Candidate: { } emptyPoor })
        {
            if (press.IsResumeRewinding)
                return PressOutcome.Empty;

            foreach (var candidate in noteCandidates)
            {
                if (candidate.Candidate.Equals(emptyPoor))
                    pressHistory.Record(NoteRegistrations[candidate.Slot].Object, press.InputTime, false, true);
            }
            return PressOutcome.ForEmptyPoor(emptyPoor.StartTime, emptyPoor.Column);
        }

        return PressOutcome.Empty;
    }

    internal void HandleExecutionRelease(double inputTime, in BmsFrameContext frame, bool reverseScratch = false)
    {
        var direction = IsScratch && reverseScratch ? 2 : 1;
        if ((pressedDirections & direction) == 0)
            return;
        pressedDirections &= ~direction;
        if (frame.IsRewinding)
            return;
        BmsLongNote? held = null;
        foreach (var slot in pendingLongNoteTailSlots)
        {
            var longNote = longNotes[slot];
            if (!GetLongNoteState(longNote).IsHolding)
                continue;
            var note = longNote;
            if (held == null || note.EndTime < held.EndTime)
                held = note;
        }
        if (held != null && releaseOperations[holdOperationSlots[noteSlots[held]]](noteSlots[held], new BmsReleaseContext(inputTime, frame, reverseScratch)))
            noteOutput.PlayTailSample(held);
    }

    internal void UpdateRegisteredLongNote(BmsLongNote note, in BmsFrameContext frame, double elapsed, bool holding)
        => finishOperations[holdOperationSlots[noteSlots[note]]](noteSlots[note], frame, elapsed, holding || IsAutomaticallyHeld(note));

    internal void UpdateRegisteredBodies(in BmsFrameContext frame, double elapsed, bool holding)
    {
        // A completed hold leaves the active schedule before the next batch's body observations.
        for (var i = activeLongNotes.Count - 1; i >= 0; i--)
            RetireInactiveLongNoteAt(i);
        for (var i = 0; i < activeBodySlots.Count;)
        {
            var slot = activeBodySlots[i];
            Rules.HellChargeNote.UpdateBody(hellChargeHoldStates[holdStateSlots[holdOperationSlots[slot]]],
                new BmsJudgementExecutionContext(this, noteOutput), frame, elapsed, holding || IsAutomaticallyHeld(longNotes[slot]));
            // Ending a body removes only that member while preserving the next object's turn.
            if (i < activeBodySlots.Count && activeBodySlots[i] == slot)
                i++;
        }
    }

}
