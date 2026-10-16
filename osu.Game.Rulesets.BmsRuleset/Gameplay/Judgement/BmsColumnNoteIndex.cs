using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

/// <summary>
/// Tracks pending endpoints and bodies in stable chart order, independently of representation lifetime.
/// </summary>
internal sealed class BmsColumnNoteIndex
{
    private readonly IReadOnlyDictionary<BmsHitObject, int> noteSlots;
    private readonly BmsNoteRegistration[] registrations;
    private readonly BmsHoldRegistry holds;
    private readonly IBmsJudgementOutput output;
    private readonly double[] headTimes;
    private readonly int[] headSlots;
    private readonly int[] headPositions;
    private readonly ulong[] pendingHeads;
    private int firstPendingHead;
    private readonly List<int> pendingTails = [];
    private readonly List<int> activeBodies = [];

    internal IReadOnlyList<int> PendingTails => pendingTails;

    internal IReadOnlyList<int> ActiveBodies => activeBodies;

    internal BmsColumnNoteIndex(IReadOnlyDictionary<BmsHitObject, int> slots, BmsNoteRegistration[] registrations,
                                BmsHoldRegistry holds, IBmsJudgementOutput output)
    {
        noteSlots = slots;
        this.registrations = registrations;
        this.holds = holds;
        this.output = output;
        headTimes = new double[registrations.Length];
        headSlots = new int[registrations.Length];
        headPositions = new int[registrations.Length];
        pendingHeads = new ulong[(registrations.Length + 63) / 64];
        for (var i = 0; i < registrations.Length; i++)
        {
            headTimes[i] = registrations[i].Object.StartTime;
            headSlots[i] = headPositions[i] = i;
        }

        Rebuild();
    }

    internal BmsHitObject? FirstPendingHead => firstPendingHead < headSlots.Length ? registrations[headSlots[firstPendingHead]].Object : null;

    internal BmsHitObject? FirstPendingNote
    {
        get
        {
            if (FirstPendingHead is not { } first) return null;

            foreach (var slot in pendingTails)
            {
                var held = registrations[slot].Object;
                if (!output.GetMainResult(held).HasResult && held.StartTime <= first.StartTime) return null;
            }

            return first;
        }
    }

    internal bool IsNoteCandidate(BmsHitObject note) => noteSlots.ContainsKey(note) && !output.GetMainResult(note).HasResult;

    // Dense bursts leave long stretches of consumed heads. One bit per ordered head lets
    // both forward and nearest searches skip those stretches without querying each result.
    internal int FindPendingHead(int position, bool forward)
    {
        if (headSlots.Length == 0 || (forward && position >= headSlots.Length) || (!forward && position < 0))
            return forward ? headSlots.Length : -1;

        position = Math.Clamp(position, 0, headSlots.Length - 1);
        var word = position / 64;
        var bits = pendingHeads[word] & (forward ? ulong.MaxValue << (position % 64) : ulong.MaxValue >> (63 - position % 64));
        while (bits == 0)
        {
            word += forward ? 1 : -1;
            if (word < 0 || word >= pendingHeads.Length) return forward ? headSlots.Length : -1;

            bits = pendingHeads[word];
        }

        return word * 64 + (forward ? BitOperations.TrailingZeroCount(bits) : 63 - BitOperations.LeadingZeroCount(bits));
    }

    internal IEnumerable<BmsHitObject> GetNoteCandidates()
    {
        foreach (var registration in registrations)
        {
            if (!output.GetMainResult(registration.Object).HasResult) yield return registration.Object;
        }
    }

    internal bool RefreshTiming(BmsHitObject note)
    {
        var slot = noteSlots[note];
        if (headTimes[slot] == note.StartTime) return false;

        headTimes[slot] = note.StartTime;
        headSlots.OrderBy(index => headTimes[index]).ToArray().CopyTo(headSlots, 0);
        for (var i = 0; i < headSlots.Length; i++) headPositions[headSlots[i]] = i;
        Rebuild();
        return true;
    }

    internal void Refresh(BmsHitObject note)
    {
        if (!noteSlots.TryGetValue(note, out var slot)) return;

        var state = note is BmsLongNote ? holds.GetState(slot) : default;
        var position = headPositions[slot];
        var mask = 1UL << (position % 64);
        if (note is BmsLongNote ? !state.HeadJudged : !output.GetMainResult(note).HasResult)
        {
            pendingHeads[position / 64] |= mask;
            firstPendingHead = Math.Min(firstPendingHead, position);
        }
        else
        {
            pendingHeads[position / 64] &= ~mask;
            if (position == firstPendingHead) firstPendingHead = FindPendingHead(position + 1, true);
        }

        if (note is not BmsLongNote) return;

        setMembership(pendingTails, slot, state.HasPendingTail);
        setMembership(activeBodies, slot, state.HasActiveBody);
    }

    internal void Rebuild()
    {
        Array.Clear(pendingHeads);
        firstPendingHead = headSlots.Length;
        pendingTails.Clear();
        activeBodies.Clear();
        foreach (var registration in registrations) Refresh(registration.Object);
    }

    private static void setMembership(List<int> members, int slot, bool needed)
    {
        var index = members.BinarySearch(slot);
        if (needed && index < 0) members.Insert(~index, slot);
        else if (!needed && index >= 0) members.RemoveAt(index);
    }
}
