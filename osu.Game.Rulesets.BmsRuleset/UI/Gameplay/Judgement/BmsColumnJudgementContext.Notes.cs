using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal sealed partial class BmsColumnJudgementContext
{
    private readonly int noteColumnIndex;
    private readonly BmsLayoutVariant noteLayout;
    private IBmsJudgementOutput? noteOutput;
    private BmsDrawableJudgementOutput? drawableNoteOutput;
    private readonly Dictionary<BmsHitObject, int> noteSlots = new(ReferenceEqualityComparer.Instance);
    private readonly List<int> activeNoteSlots = [];
    private readonly SortedSet<(double Time, long Order, int Slot)> pendingNoteHeads = [];
    private readonly List<(int Slot, BmsJudgementCandidate Candidate)> noteCandidates = [];
    private bool[] activeNotes = [];
    private long[] noteOrders = [];
    private double[] noteHeadTimes = [];
    private long nextNoteOrder;
    private BmsLongNoteOperationBinding? longNoteBinding;
    private readonly List<int> pendingLongNoteTailSlots = [];
    private readonly List<int> activeBodySlots = [];
    private readonly Dictionary<DrawableBmsHitObject, (long Generation, BmsScheduledObject Handle)> scheduledRepresentations = [];
    internal BmsCoreJudgementSchedule? Schedule { get; private set; }

    internal void EnableCoreScheduling()
    {
        Schedule = new BmsCoreJudgementSchedule(this);
        foreach (var drawable in Container.AliveEntries.Values.OfType<DrawableBmsHitObject>())
        {
            if (noteSlots.ContainsKey(drawable.HitObject)) bindScheduledRepresentation(drawable);
        }
        Container.InvalidateCoreOrder();
    }

    private void bindScheduledRepresentation(DrawableBmsHitObject drawable)
    {
        if (Schedule != null)
            scheduledRepresentations[drawable] = (drawable.BindingGeneration, Schedule.Bind(drawable.HitObject));
    }

    internal bool TryGetScheduledObject(DrawableBmsHitObject drawable, out BmsScheduledObject item)
    {
        if (scheduledRepresentations.TryGetValue(drawable, out var binding) && binding.Generation == drawable.BindingGeneration
            && ReferenceEquals(binding.Handle.Object, drawable.HitObject))
        {
            item = binding.Handle;
            return true;
        }
        item = default;
        return false;
    }

    internal void CompleteScheduledRemoval(DrawableBmsHitObject drawable, in BmsFrameContext frame)
    {
        if (TryGetScheduledObject(drawable, out var item)) Schedule!.CompleteRemoval(item, frame);
    }

    internal BmsNoteRegistration[] NoteRegistrations { get; private set; } = [];
    internal BmsNoteJudgementWindows[] NoteWindows { get; private set; } = [];
    internal BmsLongNoteRegistration[] LongNoteRegistrations { get; private set; } = [];

    internal BmsColumnJudgementContext(BmsHitObject[] notes, BmsLayoutVariant layout, int column, bool batchUpdates, IBmsJudgementOutput output)
    {
        noteColumnIndex = column;
        noteLayout = layout;
        canBatchUpdates = batchUpdates;
        columnNotes = notes.Where(n => n.Column == column).OrderBy(n => n.StartTime).ToArray();
        initialiseNoteExecution(output);
    }

    // This composition entry is deliberately opt-in until plan generation and all object types are verified.
    internal void EnableNoteExecution(bool includeLongNotes = false)
    {
        if (noteOutput != null)
            throw new InvalidOperationException("Note execution is already configured.");
        if (!includeLongNotes && columnNotes.Any(n => n is not BmsNote))
            throw new InvalidOperationException("The bounded note path requires an ordinary-note scoring column.");

        validateExecutionTypes();
        var alive = Container.AliveEntries.Values.OfType<DrawableBmsHitObject>().Where(d => d.HitObject is BmsNote or BmsLongNote).ToArray();
        foreach (var drawable in alive)
            Unregister(drawable);

        drawableNoteOutput = new BmsDrawableJudgementOutput(playfield, Column);
        initialiseNoteExecution(drawableNoteOutput);
        foreach (var drawable in alive)
            Register(drawable);
    }

    private void initialiseNoteExecution(IBmsJudgementOutput output)
    {
        validateExecutionTypes();

        noteOutput = output;
        var binding = new BmsNoteOperationBinding(this, output);
        var containsLongNotes = columnNotes.Any(n => n is BmsLongNote);
        if (containsLongNotes)
            longNoteBinding = new BmsLongNoteOperationBinding(this, output);
        NoteRegistrations = new BmsNoteRegistration[columnNotes.Length];
        NoteWindows = new BmsNoteJudgementWindows[columnNotes.Length];
        LongNoteRegistrations = containsLongNotes ? new BmsLongNoteRegistration[columnNotes.Length] : [];
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
                if (!longNoteControllers.TryGetValue(longNote, out var controller))
                    longNoteControllers.Add(longNote, controller = new BmsLongNoteJudgementController(longNote));
                LongNoteRegistrations[i] = new BmsLongNoteRegistration(longNote, controller);
                NoteRegistrations[i] = new BmsNoteRegistration(note, i, longNoteBinding!.Press, longNoteBinding.Passive);
            }
            else
            {
                NoteRegistrations[i] = new BmsNoteRegistration(note, i, binding.Press, binding.Passive);
                NoteWindows[i] = new BmsNoteJudgementWindows(note);
            }
        }

        output.SetResultCheckpoint(updateNoteIndexes);
    }

    private void validateExecutionTypes()
    {
        if (columnNotes.Any(n => n is not BmsNote and not BmsLongNote
                                || n is BmsLongNote ln && ln.Beatmap.LockedLongNoteMode is not (BmsLongNoteMode.Undefined or BmsLongNoteMode.LongNote or BmsLongNoteMode.ChargeNote or BmsLongNoteMode.HellChargeNote)))
            throw new InvalidOperationException("Only ordinary notes and long-note operations are supported by this composition.");
    }

    internal BmsLongNoteState GetLongNoteState(BmsLongNote note) => longNoteControllers[note].State;

    internal IEnumerable<BmsLongNote> GetPendingLongNoteTails()
    {
        foreach (var slot in pendingLongNoteTailSlots)
            yield return LongNoteRegistrations[slot].Object;
    }

    internal IEnumerable<BmsLongNote> GetActiveBodies()
    {
        foreach (var slot in activeBodySlots)
            yield return LongNoteRegistrations[slot].Object;
    }

    internal IEnumerable<BmsLongNote> GetHoldingNotes()
    {
        foreach (var slot in pendingLongNoteTailSlots)
        {
            var registration = LongNoteRegistrations[slot];
            if (registration.State.IsHolding)
                yield return registration.Object;
        }
    }

    internal void RefreshExecutionState(BmsLongNote note)
    {
        updateNoteIndexes(note);
        passiveDeadlineDirty = true;
    }

    internal BmsMainResultState GetNoteResult(BmsHitObject note) => noteOutput!.GetMainResult(note);

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
                if (!noteOutput!.GetMainResult(held).HasResult && held.StartTime <= first.StartTime)
                    return null;
            }
            return first;
        }
    }

    private bool hasPendingExecutionHead(BmsHitObject note)
        => note is BmsLongNote ln ? !longNoteControllers[ln].HeadJudged : !noteOutput!.GetMainResult(note).HasResult;

    internal bool IsNoteCandidate(BmsHitObject note)
        => noteSlots.TryGetValue(note, out var slot) && activeNotes[slot] && !noteOutput!.GetMainResult(note).HasResult;

    internal IEnumerable<BmsHitObject> GetNoteCandidates()
    {
        foreach (var slot in activeNoteSlots)
        {
            var note = NoteRegistrations[slot].Object;
            if (!noteOutput!.GetMainResult(note).HasResult)
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
        updateNoteIndexes(note);
        passiveDeadlineDirty = true;
    }

    internal void BeginNoteRemoval(BmsHitObject note)
    {
        var slot = noteSlots[note];
        activeNotes[slot] = false;
        activeNoteSlots.Remove(slot);
        pendingLongNoteTailSlots.Remove(slot);
        activeBodySlots.Remove(slot);
        if (drawableNoteOutput != null && note is BmsLongNote)
            activeLongNotes.Remove(drawableNoteOutput.GetRepresentation(note));
        pendingNoteHeads.Remove((noteHeadTimes[slot], noteOrders[slot], slot));
    }

    internal void UnbindRemovedNote(DrawableBmsHitObject drawable) => UnbindNoteRepresentation(drawable, drawable.BindingGeneration);

    internal void UnbindNoteRepresentation(DrawableBmsHitObject drawable, long generation)
    {
        // A late free belongs to that representation, even when its HitObject was cleared or replaced.
        if (scheduledRepresentations.TryGetValue(drawable, out var binding) && binding.Generation == generation)
        {
            scheduledRepresentations.Remove(drawable);
            Schedule!.Unbind(binding.Handle);
        }
        drawableNoteOutput?.Unbind(drawable, generation);
    }

    internal void RefreshNoteRepresentation(DrawableBmsHitObject drawable)
    {
        var note = drawable.HitObject;
        // Reapply has no AddDrawable callback. Replace only the representation, retaining ordering and histories.
        if (drawableNoteOutput != null && noteSlots.TryGetValue(note, out var slot) && activeNotes[slot])
        {
            drawableNoteOutput.Bind(drawable);
            bindScheduledRepresentation(drawable);
            Container.InvalidateCoreOrder();
        }
        RefreshNoteConfiguration(note);
    }

    private void refreshActiveNoteOrder()
    {
        // Dictionary slot reuse affects equal-candidate winners after activation changes.
        // Capture the framework's actual order at lifecycle boundaries, not on every input.
        activeNoteSlots.Clear();
        foreach (var drawable in Container.AliveEntries.Values)
        {
            if (drawable is DrawableBmsHitObject && noteSlots.TryGetValue((BmsHitObject)drawable.HitObject, out var slot) && activeNotes[slot])
                activeNoteSlots.Add(slot);
        }
        reorderLongNoteSlots();
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

    internal void RefreshNoteConfiguration(BmsHitObject note)
    {
        if (noteOutput == null || !noteSlots.TryGetValue(note, out var slot))
            return;

        if (note is BmsNote)
            NoteWindows[slot] = new BmsNoteJudgementWindows(note);
        else if (note is BmsLongNote longNote)
            longNoteControllers[longNote].RefreshConfiguration(Column?.PreserveLongNoteHistory ?? true);
        if (noteHeadTimes[slot] != note.StartTime)
        {
            pendingNoteHeads.Remove((noteHeadTimes[slot], noteOrders[slot], slot));
            noteHeadTimes[slot] = note.StartTime;
            // Configuration changes must retain the pre-existing order among equal-time objects.
            columnNotes.OrderBy(n => n.StartTime).ToArray().CopyTo(columnNotes, 0);
        }
        updateNoteIndexes(note);
        passiveDeadlineDirty = true;
    }

    private void updateNoteIndexes(BmsHitObject note)
    {
        var slot = noteSlots[note];
        var key = (noteHeadTimes[slot], noteOrders[slot], slot);
        // Removal precedes the last passive check; a synchronous result must never reactivate it.
        if (canBatchUpdates && activeNotes[slot] && hasPendingExecutionHead(note))
            pendingNoteHeads.Add(key);
        else
            pendingNoteHeads.Remove(key);
        if (note is BmsLongNote ln && activeNotes[slot] && longNoteControllers[ln].State.HasPendingTail)
        {
            if (!pendingLongNoteTailSlots.Contains(slot))
            {
                pendingLongNoteTailSlots.Add(slot);
                reorderLongNoteSlots();
            }
        }
        else
            pendingLongNoteTailSlots.Remove(slot);
        if (note is BmsLongNote body && activeNotes[slot] && longNoteControllers[body].State.HasActiveBody)
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
        if (note is BmsLongNote && activeNotes[slot] && drawableNoteOutput != null)
            trackActiveLongNote(drawableNoteOutput.GetRepresentation(note));
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
                noteOutput!.PublishState(note, longNoteControllers[note].State, frame);
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
                    ? longNoteControllers[ln].NextHeadJudgementTime : NoteWindows[slot].NextPassiveJudgementTime(note));
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
            if (longNoteBinding!.Repress(slot, press))
            {
                var registration = LongNoteRegistrations[slot];
                if (!registration.State.IsHolding)
                    noteOutput!.PlayTailSample(registration.Object);
                return PressOutcome.Hit;
            }
        }

        if (FirstPendingNote is { } first && first.StartTime == press.InputTime && IsNoteCandidate(first)
            && BmsJudgementProfileProvider.GetTable(LayoutVariant, Index, first.EffectiveJudgementRate, false).ResultForOffset(0) == HitResult.Perfect
            && TryPressRegisteredNote(first, HitResult.Perfect, press))
        {
            pressHistory.Record(first, press.InputTime, true);
            noteOutput!.PlaySample(first);
            return PressOutcome.Hit;
        }

        noteCandidates.Clear();
        pressJudgementCandidates.Clear();
        foreach (var slot in activeNoteSlots)
        {
            var note = NoteRegistrations[slot].Object;
            if (noteOutput!.GetMainResult(note).HasResult)
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
                    noteOutput!.ApplyNonConsumingBad(note, press.Frame);
                    noteOutput.PlaySample(note);
                    return PressOutcome.Hit;
                }

                if (TryPressRegisteredNote(note, selection.Result, press))
                {
                    pressHistory.Record(note, press.InputTime, true);
                    noteOutput!.PlaySample(note);
                    return PressOutcome.Hit;
                }
                break;
            }
        }

        noteOutput!.PlayKeySound();
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
            var registration = LongNoteRegistrations[slot];
            if (!registration.State.IsHolding)
                continue;
            var note = registration.Object;
            if (held == null || note.EndTime < held.EndTime)
                held = note;
        }
        if (held != null && longNoteBinding!.Release(noteSlots[held], inputTime, frame, reverseScratch))
            noteOutput!.PlayTailSample(held);
    }

    internal void UpdateRegisteredLongNote(BmsLongNote note, in BmsFrameContext frame, double elapsed, bool holding)
        => longNoteBinding!.PostResult(noteSlots[note], frame, elapsed, holding);

    internal void UpdateRegisteredBodies(in BmsFrameContext frame, double elapsed, bool holding)
    {
        for (var i = 0; i < activeBodySlots.Count;)
        {
            var slot = activeBodySlots[i];
            longNoteBinding!.UpdateBody(slot, frame, elapsed, holding);
            // Ending a body removes only that member while preserving the next object's turn.
            if (i < activeBodySlots.Count && activeBodySlots[i] == slot)
                i++;
        }
    }

    private void updateDrawableBodies(bool holding)
    {
        for (var i = 0; i < activeBodySlots.Count;)
        {
            var slot = activeBodySlots[i];
            var drawable = drawableNoteOutput!.GetRepresentation(LongNoteRegistrations[slot].Object);
            longNoteBinding!.UpdateBody(slot, noteFrame(drawable), drawable.Time.Elapsed,
                holding || ((ILongNoteHolder)drawable).IsAutomaticallyHeld);
            if (i < activeBodySlots.Count && activeBodySlots[i] == slot)
                i++;
        }
    }

    private static BmsFrameContext noteFrame(DrawableBmsHitObject note)
        => new(note.Time.Current, (note.Clock as IGameplayClock)?.GetTrueGameplayRate() ?? note.Clock.Rate,
            (note.Clock as IGameplayClock)?.IsRewinding == true);
}
