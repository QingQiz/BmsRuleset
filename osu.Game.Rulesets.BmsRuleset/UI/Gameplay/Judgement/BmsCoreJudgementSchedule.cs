using System;
using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal readonly record struct BmsScheduledObject(BmsHitObject Object, long BindingToken);

/// <summary>
/// Generates the bounded Note/HCN passive and post-result plan from actual lifecycle order.
/// Ordering and representation availability are supplied by the host; operations belong to the core.
/// </summary>
internal sealed class BmsCoreJudgementSchedule(BmsColumnJudgementContext column)
{
    private readonly Dictionary<BmsHitObject, Binding> bindings = new(ReferenceEqualityComparer.Instance);
    private readonly List<BmsScheduledObject> plan = [];
    private long revision;
    private long bindingRevision;
    private long nextBindingToken;
    private (long Epoch, long Batch, long Bindings) lastLayout = (-1, -1, -1);
    private (long Epoch, long Batch, long Bindings) lastSubtree = (-1, -1, -1);
    private (long Epoch, long Batch, long Bindings) lastDeferred = (-1, -1, -1);
    private (long Epoch, long Step) lastRewind = (-1, -1);

    internal long Epoch { get; private set; }
    internal long Step { get; private set; }
    internal long Batch { get; private set; }

    internal void BeginBatch(long epoch, long step, long batch)
    {
        revision++;
        Epoch = epoch;
        Step = step;
        Batch = batch;
    }

    internal BmsScheduledObject Bind(BmsHitObject note)
    {
        // Drawable-local generations can collide when a different pooled representation replaces this object.
        var item = new BmsScheduledObject(note, checked(++nextBindingToken));
        bindings[note] = new Binding(item.BindingToken);
        InvalidateOrder();
        return item;
    }

    internal void Unbind(BmsScheduledObject item)
    {
        if (bindings.TryGetValue(item.Object, out var binding) && binding.Token == item.BindingToken)
        {
            bindings.Remove(item.Object);
            InvalidateOrder();
        }
    }

    internal void BeginRemoval(BmsScheduledObject item)
    {
        if (bindings.TryGetValue(item.Object, out var binding) && binding.Token == item.BindingToken)
            binding.Removing = true;
    }

    internal void Rewind()
    {
        revision++;
        bindingRevision++;
        // A restored frame may revisit a previously processed timestamp.
        foreach (var binding in bindings.Values)
            binding.LastPostTime = double.NaN;
    }

    internal void InvalidateOrder() => bindingRevision++;

    internal bool BeginRewindStep()
    {
        var next = (Epoch, Step);
        if (lastRewind == next) return false;
        lastRewind = next;
        return true;
    }

    internal void RunLayout(IReadOnlyList<BmsScheduledObject> order, in BmsFrameContext frame, double elapsed, bool frozen)
    {
        if (frozen || frame.IsRewinding || elapsed < 0 || !begin(ref lastLayout)) return;
        var version = snapshot(order);
        for (var i = 0; i < plan.Count; i++)
        {
            if (version != revision) return;
            var item = plan[i];
            if (current(item) && item.Object is BmsLongNote)
                updateLongNote(item, frame, elapsed, version);
        }
    }

    internal void RunSubtree(IReadOnlyList<BmsScheduledObject> order, in BmsFrameContext frame)
    {
        if (!begin(ref lastSubtree)) return;
        var version = snapshot(order);
        for (var i = 0; i < plan.Count; i++)
        {
            if (version != revision) return;
            var item = plan[i];
            if (current(item) && (item.Object is BmsNote || !frame.IsRewinding && !column.GetNoteResult(item.Object).HasResult))
                CheckPassive(item.Object, frame);
        }
    }

    internal void RunDeferred(IReadOnlyList<BmsScheduledObject> order, in BmsFrameContext frame, double elapsed)
    {
        if (!begin(ref lastDeferred)) return;
        var version = snapshot(order);
        for (var i = plan.Count - 1; i >= 0; i--)
        {
            if (version != revision) return;
            var item = plan[i];
            if (current(item)) updateLongNote(item, frame, elapsed, version);
        }
    }

    internal void CompleteRemoval(BmsScheduledObject item, in BmsFrameContext frame)
    {
        if (!bindings.TryGetValue(item.Object, out var binding) || binding.Token != item.BindingToken || !binding.Removing || binding.RemovalChecked)
            return;
        binding.RemovalChecked = true;
        if (item.Object is BmsNote || !column.GetNoteResult(item.Object).HasResult)
            CheckPassive(item.Object, frame);
    }

    private void updateLongNote(BmsScheduledObject item, in BmsFrameContext frame, double elapsed, long version)
    {
        if (!column.GetNoteResult(item.Object).HasResult)
            CheckPassive(item.Object, frame);
        // Synchronous output may remove or rebind this object. Never let the old plan borrow its replacement.
        if (version != revision || !current(item)) return;
        var binding = bindings[item.Object];
        if (binding.LastPostTime == frame.SimulationTime) return;
        binding.LastPostTime = frame.SimulationTime;
        FinishLongNote((BmsLongNote)item.Object, frame, elapsed, column.PressedDirections != 0);
    }

    internal void CheckPassive(BmsHitObject note, in BmsFrameContext frame)
        => column.CheckRegisteredNotePassive(note, frame);

    internal void FinishLongNote(BmsLongNote note, in BmsFrameContext frame, double elapsed, bool holding)
        => column.UpdateRegisteredLongNote(note, frame, elapsed, holding);

    private bool current(BmsScheduledObject item)
        => bindings.TryGetValue(item.Object, out var binding) && binding.Token == item.BindingToken && !binding.Removing;

    private bool begin(ref (long Epoch, long Batch, long Bindings) previous)
    {
        var next = (Epoch, Batch, bindingRevision);
        if (previous == next) return false;
        previous = next;
        return true;
    }

    private long snapshot(IReadOnlyList<BmsScheduledObject> order)
    {
        // Membership changes invalidate handles, not this cursor; removing the current item cannot skip its successor.
        plan.Clear();
        for (var i = 0; i < order.Count; i++) plan.Add(order[i]);
        return ++revision;
    }

    private sealed class Binding(long token)
    {
        internal long Token { get; } = token;
        internal bool Removing;
        internal bool RemovalChecked;
        internal double LastPostTime = double.NaN;
    }
}
