using System;
using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Rules;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

/// <summary>
/// Owns active membership and derived lookup order. It queries state but never changes rule state.
/// </summary>
internal sealed class BmsColumnNoteIndex
{
    private readonly IReadOnlyDictionary<BmsHitObject, int> noteSlots;
    private readonly BmsNoteRegistration[] noteRegistrations;
    private readonly BmsNoteJudgementWindows[] noteWindows;
    private readonly BmsHoldRegistry holds;
    private readonly IBmsJudgementOutput noteOutput;
    private readonly bool canBatchUpdates;
    private readonly bool[] activeNotes;
    private readonly long[] noteOrders;
    private readonly double[] noteHeadTimes;
    private readonly int[] activeOrderRanks;
    private readonly int[] proposedOrderRanks;
    private readonly List<int> proposedOrder = [];
    private readonly Comparison<int> compareActiveOrder;
    private readonly List<int> activeNoteSlots = [];
    private readonly List<int> pendingLongNoteTailSlots = [];
    private readonly List<int> activeBodySlots = [];
    private readonly List<BmsLongNote> activeLongNotes = [];
    private readonly SortedSet<(double Time, long Order, int Slot)> pendingNoteHeads = [];
    private long nextNoteOrder;
    private bool passiveDeadlineDirty = true;

    internal int RankOf(int slot) => activeOrderRanks[slot];

    internal IReadOnlyList<int> ActiveSlots => activeNoteSlots;

    internal IReadOnlyList<int> PendingTails => pendingLongNoteTailSlots;

    internal IReadOnlyList<int> ActiveBodies => activeBodySlots;

    internal IReadOnlyList<BmsLongNote> ActiveLongNotes => activeLongNotes;

    internal double NextPassiveUpdateTime { get; private set; }

    internal BmsColumnNoteIndex(IReadOnlyDictionary<BmsHitObject, int> slots, BmsNoteRegistration[] registrations,
                                BmsNoteJudgementWindows[] windows, BmsHoldRegistry holds,
                                IBmsJudgementOutput output, bool batchUpdates)
    {
        noteSlots = slots;
        noteRegistrations = registrations;
        noteWindows = windows;
        this.holds = holds;
        noteOutput = output;
        canBatchUpdates = batchUpdates;
        activeNotes = new bool[registrations.Length];
        noteOrders = new long[registrations.Length];
        noteHeadTimes = new double[registrations.Length];
        activeOrderRanks = new int[registrations.Length];
        proposedOrderRanks = new int[registrations.Length];
        Array.Fill(activeOrderRanks, -1);
        Array.Fill(proposedOrderRanks, -1);
        for (var slot = 0; slot < registrations.Length; slot++) noteHeadTimes[slot] = registrations[slot].Object.StartTime;
        compareActiveOrder = (left, right) => activeOrderRanks[left].CompareTo(activeOrderRanks[right]);
    }

    private BmsLongNoteState getLongNoteState(BmsLongNote note) => holds.GetState(noteSlots[note]);

    internal void InvalidateDeadline() => passiveDeadlineDirty = true;

    internal bool RefreshTiming(BmsHitObject note)
    {
        var slot = noteSlots[note];
        if (noteHeadTimes[slot] == note.StartTime) return false;

        pendingNoteHeads.Remove((noteHeadTimes[slot], noteOrders[slot], slot));
        noteHeadTimes[slot] = note.StartTime;
        Refresh(note);
        passiveDeadlineDirty = true;
        // A moved head can now be due earlier than the cached bound; the host must not defer that check.
        NextPassiveUpdateTime = double.NegativeInfinity;
        return true;
    }

    internal void SetActivationOrder(IEnumerable<BmsHitObject> notes)
    {
        // Validate before publishing: a rejected plan must not corrupt the live candidate or hold order.
        foreach (var slot in proposedOrder) proposedOrderRanks[slot] = -1;
        proposedOrder.Clear();
        foreach (var note in notes)
        {
            if (!noteSlots.TryGetValue(note, out var slot) || !activeNotes[slot] || proposedOrderRanks[slot] >= 0)
                throw new InvalidOperationException("The activation plan must contain each active note exactly once.");

            proposedOrderRanks[slot] = proposedOrder.Count;
            proposedOrder.Add(slot);
        }

        if (proposedOrder.Count != activeNoteSlots.Count)
            throw new InvalidOperationException("The activation plan omitted an active note.");

        activeNoteSlots.Clear();
        activeNoteSlots.AddRange(proposedOrder);
        refreshOrderRanks();
        reorderLongNoteSlots();
    }

    private void refreshOrderRanks()
    {
        for (var i = 0; i < activeNoteSlots.Count; i++) activeOrderRanks[activeNoteSlots[i]] = i;
    }

    private void reorderLongNoteSlots()
    {
        pendingLongNoteTailSlots.Sort(compareActiveOrder);
        // Homogeneous columns retain head-registration order for bodies; mixed columns follow the alive map.
        if (!canBatchUpdates) activeBodySlots.Sort(compareActiveOrder);
    }

    private void reorderSlots(List<int> slots) => slots.Sort(compareActiveOrder);

    internal BmsHitObject? FirstPendingNote
    {
        get
        {
            if (!canBatchUpdates || pendingNoteHeads.Count == 0)
                return null;

            var first = noteRegistrations[pendingNoteHeads.Min.Slot].Object;
            foreach (var slot in pendingLongNoteTailSlots)
            {
                var held = noteRegistrations[slot].Object;
                if (!noteOutput.GetMainResult(held).HasResult && held.StartTime <= first.StartTime)
                    return null;
            }

            return first;
        }
    }

    private bool hasPendingExecutionHead(BmsHitObject note)
        => note is BmsLongNote ln ? !getLongNoteState(ln).HeadJudged : !noteOutput.GetMainResult(note).HasResult;

    internal bool IsNoteActive(BmsHitObject note)
        => noteSlots.TryGetValue(note, out var slot) && activeNotes[slot];

    internal bool IsNoteCandidate(BmsHitObject note)
        => noteSlots.TryGetValue(note, out var slot) && activeNotes[slot] && !noteOutput.GetMainResult(note).HasResult;

    internal IEnumerable<BmsHitObject> GetNoteCandidates()
    {
        foreach (var slot in activeNoteSlots)
        {
            var note = noteRegistrations[slot].Object;
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
        activeOrderRanks[slot] = activeNoteSlots.Count;
        activeNoteSlots.Add(slot);
        Refresh(note);
        passiveDeadlineDirty = true;
    }

    internal void BeginNoteRemoval(BmsHitObject note)
    {
        var slot = noteSlots[note];
        activeNotes[slot] = false;
        activeNoteSlots.Remove(slot);
        activeOrderRanks[slot] = -1;
        refreshOrderRanks();
        pendingLongNoteTailSlots.Remove(slot);
        activeBodySlots.Remove(slot);
        if (note is BmsLongNote longNote)
        {
            activeLongNotes.Remove(longNote);
        }

        pendingNoteHeads.Remove((noteHeadTimes[slot], noteOrders[slot], slot));
    }

    internal void Refresh(BmsHitObject note)
    {
        if (!noteSlots.TryGetValue(note, out var slot))
            return;

        var key = (noteHeadTimes[slot], noteOrders[slot], slot);
        // Removal precedes the last passive check; a synchronous result must never reactivate it.
        if (canBatchUpdates && activeNotes[slot] && hasPendingExecutionHead(note))
            pendingNoteHeads.Add(key);
        else
            pendingNoteHeads.Remove(key);
        if (note is BmsLongNote ln && activeNotes[slot] && getLongNoteState(ln).HasPendingTail)
        {
            if (!pendingLongNoteTailSlots.Contains(slot))
            {
                pendingLongNoteTailSlots.Add(slot);
                reorderLongNoteSlots();
            }
        }
        else
            pendingLongNoteTailSlots.Remove(slot);

        if (note is BmsLongNote body && activeNotes[slot] && getLongNoteState(body).HasActiveBody)
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

    internal void Rebuild()
    {
        pendingNoteHeads.Clear();
        pendingLongNoteTailSlots.Clear();
        activeBodySlots.Clear();
        activeLongNotes.Clear();
        foreach (var slot in activeNoteSlots)
            Refresh(noteRegistrations[slot].Object);
        passiveDeadlineDirty = true;
    }

    internal void RefreshDeadline(double now)
    {
        if (!canBatchUpdates || (!passiveDeadlineDirty && now <= NextPassiveUpdateTime))
            return;

        NextPassiveUpdateTime = double.PositiveInfinity;
        foreach (var slot in activeNoteSlots)
        {
            var note = noteRegistrations[slot].Object;
            if (hasPendingExecutionHead(note))
                NextPassiveUpdateTime = Math.Min(NextPassiveUpdateTime, note is BmsLongNote
                    ? holds.HeadDeadline(slot)
                    : noteWindows[slot].NextPassiveJudgementTime(note));
        }

        passiveDeadlineDirty = false;
    }

    internal bool CanSkipPassiveChecks(double now)
    {
        if (!canBatchUpdates || activeLongNotes.Count != 0)
            return false;

        if (passiveDeadlineDirty)
            RefreshDeadline(now);

        // A head bound cannot cover release resolution or tails. At the bound itself,
        // retain each rule's subtraction comparison rather than relying on rounded addition.
        return now < NextPassiveUpdateTime;
    }

    internal bool RequiresActiveLongNoteUpdate(BmsHitObject note)
    {
        if (note is not BmsLongNote longNote)
            return false;

        var state = getLongNoteState(longNote);
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
}
