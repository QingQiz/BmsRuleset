using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

internal readonly record struct BmsScheduledObject(BmsHitObject Object, long BindingToken);

/// <summary>
/// Orders passive and post-result operations at lifecycle boundaries.
/// Ordering and representation availability are supplied by the host; operations belong to the core.
/// </summary>
internal sealed class BmsCoreJudgementSchedule(BmsColumnJudgementRuntime column)
{
    private readonly Dictionary<BmsHitObject, Binding> bindings = new(ReferenceEqualityComparer.Instance);
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
            item = new BmsScheduledObject(note, binding.Token);
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
        {
            binding.Removing = true;
            bindings[item.Object] = binding;
        }
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
        if (!bindings.TryGetValue(item.Object, out var binding) || binding.Token != item.BindingToken || !binding.Removing || binding.RemovalChecked)
            return;

        binding.RemovalChecked = true;
        bindings[item.Object] = binding;
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
        var step = (Epoch, Step, rewindRevision);
        if (binding.LastPostStep == step) return;

        binding.LastPostStep = step;
        bindings[item.Object] = binding;
        column.ObserveSimulationStep(item.Object, frame);
        if (version != revision || !current(item)) return;

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

    private struct Binding(long token)
    {
        internal long Token { get; } = token;

        internal bool Removing;

        internal bool RemovalChecked;

        // Inputs retain their own batches; post-result work runs once per simulation step and binding.
        internal (long Epoch, long Step, long Rewind) LastPostStep = (-1, -1, -1);
    }
}
