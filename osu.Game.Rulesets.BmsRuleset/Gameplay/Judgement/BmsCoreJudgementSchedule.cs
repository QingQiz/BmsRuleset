using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

internal readonly record struct BmsScheduledObject(BmsCoreJudgementSchedule.Binding Binding)
{
    internal BmsHitObject Object => Binding?.Object!;

    internal long BindingToken => Binding?.Token ?? 0;
}

/// <summary>
/// Orders passive and post-result operations at lifecycle boundaries.
/// Ordering and representation availability are supplied by the host; operations belong to the core.
/// </summary>
internal sealed class BmsCoreJudgementSchedule(BmsColumnJudgementRuntime column)
{
    private readonly Dictionary<BmsHitObject, Binding> bindings = new(ReferenceEqualityComparer.Instance);
    private readonly object bindingOwner = new();
    private readonly List<BmsScheduledObject> plan = [];
    private long revision;
    private long bindingRevision;
    private long rewindRevision;
    private long nextBindingToken;
    private (long Epoch, long Batch, long Bindings) lastLayout = (-1, -1, -1);
    private (long Epoch, long Batch, long Bindings) lastSubtree = (-1, -1, -1);
    private (long Epoch, long Batch, long Bindings) lastDeferred = (-1, -1, -1);
    private (long Epoch, long Step) lastRewind = (-1, -1);

    internal long Epoch { get; private set; }

    internal long Step { get; private set; }

    internal long Batch { get; private set; }

    internal bool TryGetCurrent(BmsHitObject note, out BmsScheduledObject item)
    {
        if (bindings.TryGetValue(note, out var binding) && !binding.Removing)
        {
            item = new BmsScheduledObject(binding);
            return true;
        }

        item = default;
        return false;
    }

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
        var binding = new Binding(bindingOwner, note, checked(++nextBindingToken));
        if (bindings.TryGetValue(note, out var previous)) previous.Bound = false;
        bindings[note] = binding;
        InvalidateOrder();
        return new BmsScheduledObject(binding);
    }

    internal void Unbind(BmsScheduledObject item)
    {
        if (bound(item))
        {
            item.Binding.Bound = false;
            bindings.Remove(item.Object);
            InvalidateOrder();
        }
    }

    internal void BeginRemoval(BmsScheduledObject item)
    {
        if (bound(item)) item.Binding.Removing = true;
    }

    internal void Rewind()
    {
        revision++;
        bindingRevision++;
        // A generation invalidates all post-result stamps without rewriting each active binding.
        rewindRevision++;
    }

    internal void InvalidateOrder() => bindingRevision++;

    internal bool BeginRewindStep(in BmsFrameContext frame)
    {
        var next = (Epoch, Step);
        if (lastRewind == next) return false;

        lastRewind = next;
        column.RewindLongNotes(frame.SimulationTime);
        column.RewindLandmines(frame.SimulationTime);
        return true;
    }

    internal void RunLayout(IReadOnlyList<BmsScheduledObject> order, in BmsFrameContext frame, double elapsed, bool frozen)
    {
        if (frozen || frame.IsRewinding || elapsed < 0 || !begin(ref lastLayout)) return;

        if (order.Count == 0)
        {
            if (order is BmsScheduledOrder cachedOrder)
                cachedOrder.GetSnapshot();
            // An empty reentrant layout still interrupts the outer cursor, without clearing its shared buffer.
            revision++;
            return;
        }

        var plan = snapshot(order);
        var version = ++revision;
        for (var i = 0; i < plan.Count; i++)
        {
            if (version != revision) return;

            var item = plan[i];
            if (item.Object is BmsLongNote && current(item))
                updateLongNote(item, frame, elapsed, version);
        }
    }

    internal void RunSubtree(IReadOnlyList<BmsScheduledObject> order, in BmsFrameContext frame)
    {
        if (!begin(ref lastSubtree)) return;

        var plan = snapshot(order);
        var version = ++revision;

        for (var i = 0; i < plan.Count; i++)
        {
            if (version != revision) return;

            var item = plan[i];
            if (current(item) && (item.Object is BmsNote || (!frame.IsRewinding && !column.GetNoteResult(item.Object).HasResult)))
                CheckPassive(item.Object, frame);
        }
    }

    internal void RunDeferred(in BmsFrameContext frame, double elapsed)
    {
        if (frame.IsRewinding || elapsed < 0 || !begin(ref lastDeferred)) return;

        plan.Clear();
        var holds = column.ActiveLongNotes;
        for (var i = 0; i < holds.Count; i++)
        {
            var note = holds[i];
            if (column.RequiresActiveLongNoteUpdate(note) && TryGetCurrent(note, out var item))
                plan.Add(item);
        }

        var version = ++revision;
        for (var i = plan.Count - 1; i >= 0; i--)
        {
            if (version != revision) return;

            var item = plan[i];
            if (current(item)) updateLongNote(item, frame, elapsed, version);
        }

        for (var i = holds.Count - 1; i >= 0; i--)
            column.RetireInactiveLongNoteAt(i);
    }

    internal void CompleteRemoval(BmsScheduledObject item, in BmsFrameContext frame)
    {
        if (!bound(item) || !item.Binding.Removing || item.Binding.RemovalChecked)
            return;

        item.Binding.RemovalChecked = true;
        if (item.Object is BmsNote || !column.GetNoteResult(item.Object).HasResult)
            CheckPassive(item.Object, frame);
    }

    private void updateLongNote(BmsScheduledObject item, in BmsFrameContext frame, double elapsed, long version)
    {
        if (!column.GetNoteResult(item.Object).HasResult)
            CheckPassive(item.Object, frame);
        // Synchronous output may remove or rebind this object. Never let the old plan borrow its replacement.
        if (version != revision || !current(item)) return;

        var binding = item.Binding;
        var step = (Epoch, Step, rewindRevision);
        if (binding.LastPostStep == step) return;

        binding.LastPostStep = step;
        column.ObserveSimulationStep(item.Object, frame);
        if (version != revision || !current(item)) return;

        FinishLongNote((BmsLongNote)item.Object, frame, elapsed, column.PressedDirections != 0);
    }

    internal void CheckPassive(BmsHitObject note, in BmsFrameContext frame)
        => column.CheckRegisteredNotePassive(note, frame);

    internal void FinishLongNote(BmsLongNote note, in BmsFrameContext frame, double elapsed, bool holding)
        => column.UpdateRegisteredLongNote(note, frame, elapsed, holding);

    private bool current(BmsScheduledObject item)
        => bound(item) && !item.Binding.Removing;

    private bool bound(BmsScheduledObject item)
        => ReferenceEquals(item.Binding?.Owner, bindingOwner) && item.Binding.Bound;

    private bool begin(ref (long Epoch, long Batch, long Bindings) previous)
    {
        var next = (Epoch, Batch, bindingRevision);
        if (previous == next) return false;

        previous = next;
        return true;
    }

    private List<BmsScheduledObject> snapshot(IReadOnlyList<BmsScheduledObject> order)
    {
        if (order is BmsScheduledOrder cachedOrder)
            return cachedOrder.GetSnapshot();

        // Membership changes invalidate handles, not this cursor; removing the current item cannot skip its successor.
        plan.Clear();
        for (var i = 0; i < order.Count; i++) plan.Add(order[i]);
        return plan;
    }

    // Old snapshots keep this identity; replacement invalidates it instead of lending them the new binding.
    internal sealed class Binding(object owner, BmsHitObject note, long token)
    {
        internal object Owner { get; } = owner;

        internal BmsHitObject Object { get; } = note;

        internal long Token { get; } = token;

        internal bool Bound = true;

        internal bool Removing;

        internal bool RemovalChecked;

        // Inputs retain their own batches; post-result work runs once per simulation step and binding.
        internal (long Epoch, long Step, long Rewind) LastPostStep = (-1, -1, -1);
    }
}
