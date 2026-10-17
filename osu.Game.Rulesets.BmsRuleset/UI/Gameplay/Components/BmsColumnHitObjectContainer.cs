using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Judgement;
using System;
using System.Collections.Generic;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

public sealed partial class BmsColumnHitObjectContainer : HitObjectContainer
{
    internal const double MAX_DEFERRED_UPDATE_TIME = 16;
    private readonly BmsGameplayScrollController scrollController;
    private readonly Func<float> getHitTargetPosition;
    private bool frameOpen;
    private bool hasUpdated;
    private bool updatePending;
    private double lastFullUpdateTime;

    private double latestLifetimeStart = double.PositiveInfinity;

    private bool coreOrderDirty = true;
    private readonly Dictionary<DrawableBmsHitObject, Action<ValueChangedEvent<double>>> coreStartTimes = [];

    internal BmsColumnJudgementRuntime Judgements { get; set; } = null!;

    internal BmsJudgementBridge JudgementBridge { get; set; } = null!;

    public override void Add(HitObjectLifetimeEntry entry)
    {
        base.Add(entry);
        latestLifetimeStart = double.PositiveInfinity;
        scrollEntriesDirty = true;
        mineTimesDirty = true;
    }

    public override bool Remove(HitObjectLifetimeEntry entry)
    {
        var removed = base.Remove(entry);
        if (removed && entry is BmsHitObjectLifetimeEntry mineEntry)
            mineEntry.SetBatchedMine(false);
        scrollEntriesDirty |= removed;
        mineTimesDirty |= removed;
        return removed;
    }

    internal BmsColumnHitObjectContainer(
        BmsGameplayScrollController scrollController,
        Func<float> getHitTargetPosition)
    {
        this.scrollController = scrollController;
        this.getHitTargetPosition = getHitTargetPosition;
        RelativeSizeAxes = Axes.Both;
    }

    protected override void AddDrawable(HitObjectLifetimeEntry entry, DrawableHitObject drawable)
    {
        base.AddDrawable(entry, drawable);
        if (drawable is not DrawableBmsHitObject note)
            return;

        JudgementBridge.Register(note);
        InvalidateCoreOrder();
    }

    protected override void RemoveDrawable(HitObjectLifetimeEntry entry, DrawableHitObject drawable)
    {
        InvalidateCoreOrder();
        if (drawable is DrawableBmsHitObject old && coreStartTimes.Remove(old, out var startTimeChanged))
            old.StartTimeBindable.ValueChanged -= startTimeChanged;
        base.RemoveDrawable(entry, drawable);
        if (drawable is DrawableBmsHitObject removed)
            JudgementBridge.UnbindRemovedNote(removed);
    }

    internal void BeginGameplayFrame()
    {
        frameOpen = true;
        updatePending = false;
    }

    internal void EndGameplayFrame(bool completePendingUpdate = true)
    {
        frameOpen = false;
        if (completePendingUpdate && updatePending)
            UpdateSubTree();
    }

    internal bool CanDeferUpdate => frameOpen && hasUpdated && Time.Elapsed >= 0
                                    && (Clock as IGameplayClock)?.IsRewinding != true
                                    && Time.Current >= lastFullUpdateTime
                                    && (Time.Current - lastFullUpdateTime < MAX_DEFERRED_UPDATE_TIME || lastFullUpdateTime >= latestLifetimeStart);

    public override bool UpdateSubTree()
    {
        var now = Time.Current;
        // Judgement and lifetime activation advance independently, so skins can share
        // the final outer frame's traversal. The bridge covers objects judged before activation.
        if (CanDeferUpdate)
        {
            updatePending = true;
            UpdateDeferredFrame();
            return true;
        }

        var updated = base.UpdateSubTree();
        hasUpdated = true;
        updatePending = false;
        lastFullUpdateTime = now;

        return updated;
    }

    internal void UpdateDeferredFrame()
    {
        observeDeferredVisuals(visualFrame());
    }

    /// <summary>
    ///     Re-compute lifetimes for every entry in this container.
    ///     Triggered on load completion and whenever the user adjusts the scroll speed.
    /// </summary>
    public void RefreshAllEntries(double? currentTime = null)
    {
        scrollEntriesDirty = true;
        latestLifetimeStart = double.NegativeInfinity;
        foreach (var entry in Entries)
        {
            if (entry is BmsHitObjectLifetimeEntry bmsEntry)
                bmsEntry.RefreshLifetime(currentTime);
            // Once every candidate has been activated, no future input needs another lifetime
            // pass. Full skin traversal on each catch-up step can otherwise prevent recovery.
            if (entry.HitObject is not (BmsLandmine or BmsInvisibleNote))
                latestLifetimeStart = Math.Max(latestLifetimeStart, entry.LifetimeStart);
        }
    }

    internal void ApplyVisualOffsetToAllEntries()
    {
        foreach (var entry in Entries)
        {
            if (entry is BmsHitObjectLifetimeEntry bmsEntry)
                bmsEntry.ApplyVisualOffset();
        }
    }

    protected override bool CheckChildrenLife()
    {
        UpdateScrollEntries();
        return base.CheckChildrenLife();
    }

    protected override void UpdateAfterChildrenLife()
    {
        base.UpdateAfterChildrenLife();
        updateVisuals(visualFrame());
    }

    private void updateVisuals(BmsColumnVisualFrame frame)
    {
        refreshCoreOrder();

        foreach (var entry in AliveEntries)
        {
            if (entry.Value is not DrawableBmsHitObject note)
                continue;

            var hitObject = note.HitObject!;
            var startTime = scrollController.ConstantScrollActive || scrollController.SoftConstantActive ? hitObject.StartTime : 0;
            note.UpdateSoftConstantVisibility(scrollController.SoftConstantActive ? scrollController.GetSoftConstantAlpha(startTime) : 1);
            var startPosition = scrollController.ConstantScrollActive ? startTime : hitObject.ScrollPositionAtStartTime;
            note.UpdateVisualFrame(frame.YAt(startPosition), frame);
        }
    }

    internal void InvalidateCoreOrder()
    {
        coreOrderDirty = true;
        // Until the existing lifetime-order refresh observes all current bindings,
        // keep the original draw path rather than snapshotting a stale static prefix.
        spriteBatchHasMultiplePieces = true;
    }

    private BmsColumnVisualFrame visualFrame()
    {
        var hitTarget = getHitTargetPosition();
        var height = DrawHeight;
        var scale = scrollController.ScrollCoordinateScale(height, hitTarget);
        return new BmsColumnVisualFrame(scrollController.CurrentScrollPosition, scale, hitTarget, height, scrollController.TimelineEndTime);
    }

    private BmsFrameContext coreFrame()
        => new(Time.Current, (Clock as IGameplayClock)?.GetTrueGameplayRate() ?? Clock.Rate, (Clock as IGameplayClock)?.IsRewinding == true);

    private void refreshCoreOrder()
    {
        if (!coreOrderDirty) return;

        spriteBatchHasMultiplePieces = false;
        foreach (var drawable in AliveEntries.Values)
        {
            if (drawable is not DrawableBmsHitObject note)
            {
                spriteBatchHasMultiplePieces = true;
                continue;
            }

            // These representations cannot supply a single Sprite. Reuse this
            // lifecycle traversal so mixed columns never prepare a doomed prefix.
            if (note.HitObject is BmsLongNote)
                spriteBatchHasMultiplePieces = true;
            if (note.HitObject is BmsNote or BmsLongNote && !coreStartTimes.ContainsKey(note))
                observeStartTime(note);
        }

        coreOrderDirty = false;
    }

    private void observeStartTime(DrawableBmsHitObject note)
    {
        // Keep the capture at the binding boundary; rebuilding order must not allocate for existing bindings.
        Action<ValueChangedEvent<double>> changed = _ =>
        {
            Judgements.RefreshNoteTiming(note.HitObject);
            note.Invalidate(Invalidation.DrawNode);
            mineTimesDirty = true;
            InvalidateCoreOrder();
        };
        note.StartTimeBindable.ValueChanged += changed;
        coreStartTimes.Add(note, changed);
    }

    protected override void Dispose(bool isDisposing)
    {
        foreach (var (note, changed) in coreStartTimes) note.StartTimeBindable.ValueChanged -= changed;
        coreStartTimes.Clear();
        foreach (var (drawable, observation) in visualObservations)
            drawable.ClearVisualTimeObserver(observation.Generation);
        visualObservations.Clear();
        base.Dispose(isDisposing);
    }

    protected override void Update()
    {
        base.Update();
        if (Time.Elapsed >= 0)
            return;

        JudgementBridge.RestoreRewoundState(coreFrame());
    }
}
