using System.Collections.Generic;
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
    private readonly SortedSet<(double Time, int Slot)> pendingHeads = [];
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
        for (var i = 0; i < registrations.Length; i++) headTimes[i] = registrations[i].Object.StartTime;
        Rebuild();
    }

    internal BmsHitObject? FirstPendingNote
    {
        get
        {
            if (pendingHeads.Count == 0) return null;

            var first = registrations[pendingHeads.Min.Slot].Object;
            foreach (var slot in pendingTails)
            {
                var held = registrations[slot].Object;
                if (!output.GetMainResult(held).HasResult && held.StartTime <= first.StartTime) return null;
            }

            return first;
        }
    }

    internal bool IsNoteCandidate(BmsHitObject note) => noteSlots.ContainsKey(note) && !output.GetMainResult(note).HasResult;

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

        pendingHeads.Remove((headTimes[slot], slot));
        headTimes[slot] = note.StartTime;
        Refresh(note);
        return true;
    }

    internal void Refresh(BmsHitObject note)
    {
        if (!noteSlots.TryGetValue(note, out var slot)) return;

        var state = note is BmsLongNote ? holds.GetState(slot) : default;
        if (note is BmsLongNote ? !state.HeadJudged : !output.GetMainResult(note).HasResult)
            pendingHeads.Add((headTimes[slot], slot));
        else pendingHeads.Remove((headTimes[slot], slot));
        if (note is not BmsLongNote) return;

        setMembership(pendingTails, slot, state.HasPendingTail);
        setMembership(activeBodies, slot, state.HasActiveBody);
    }

    internal void Rebuild()
    {
        pendingHeads.Clear();
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
