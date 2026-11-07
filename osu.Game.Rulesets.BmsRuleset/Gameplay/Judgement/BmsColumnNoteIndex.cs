using System;
using System.Collections.Generic;
using System.Numerics;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

/// <summary>
/// Tracks pending endpoints and bodies in stable chart order, independently of representation lifetime.
/// </summary>
internal sealed class BmsColumnNoteIndex
{
    private readonly IReadOnlyDictionary<BmsHitObject, int> noteSlots;

    // Slots follow the final chart order; only judgement state changes during play.
    private readonly BmsNoteRegistration[] registrations;
    private readonly BmsHoldRegistry holds;
    private readonly IBmsJudgementOutput output;
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
        pendingHeads = new ulong[(registrations.Length + 63) / 64];
        Rebuild();
    }

    internal BmsHitObject? FirstPendingHead => firstPendingHead < registrations.Length ? registrations[firstPendingHead].Object : null;

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
        if (registrations.Length == 0 || (forward && position >= registrations.Length) || (!forward && position < 0))
            return forward ? registrations.Length : -1;

        position = Math.Clamp(position, 0, registrations.Length - 1);
        var word = position / 64;
        var bits = pendingHeads[word] & (forward ? ulong.MaxValue << (position % 64) : ulong.MaxValue >> (63 - position % 64));
        while (bits == 0)
        {
            word += forward ? 1 : -1;
            if (word < 0 || word >= pendingHeads.Length) return forward ? registrations.Length : -1;

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

    internal void Refresh(BmsHitObject note)
    {
        if (!noteSlots.TryGetValue(note, out var slot)) return;

        var state = note is BmsLongNote ? holds.GetState(slot) : default;
        var mask = 1UL << (slot % 64);
        if (note is BmsLongNote ? !state.HeadJudged : !output.GetMainResult(note).HasResult)
        {
            pendingHeads[slot / 64] |= mask;
            firstPendingHead = Math.Min(firstPendingHead, slot);
        }
        else
        {
            pendingHeads[slot / 64] &= ~mask;
            if (slot == firstPendingHead) firstPendingHead = FindPendingHead(slot + 1, true);
        }

        if (note is not BmsLongNote) return;

        setMembership(pendingTails, slot, state.HasPendingTail);
        setMembership(activeBodies, slot, state.HasActiveBody);
    }

    internal void Rebuild()
    {
        Array.Clear(pendingHeads);
        firstPendingHead = registrations.Length;
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
