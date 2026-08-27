using System;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Objects;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.UI.Components;

public sealed partial class BmsColumnHitObjectContainer : HitObjectContainer
{

    private readonly BmsGameplayScrollController scrollController;
    private readonly Func<float> getHitTargetPosition;
    private readonly Func<bool> isResumeRewinding;
    private readonly Func<double> getResumeRewindEndTime;

    internal BmsColumnHitObjectContainer(
        BmsGameplayScrollController scrollController,
        Func<float> getHitTargetPosition,
        Func<bool> isResumeRewinding,
        Func<double> getResumeRewindEndTime)
    {
        this.scrollController = scrollController;
        this.getHitTargetPosition = getHitTargetPosition;
        this.isResumeRewinding = isResumeRewinding;
        this.getResumeRewindEndTime = getResumeRewindEndTime;
        RelativeSizeAxes = Axes.Both;
    }

    /// <summary>
    ///     Re-compute lifetimes for every entry in this container.
    ///     Triggered on load completion and whenever the user adjusts the scroll speed.
    /// </summary>
    public void RefreshAllEntries(double? currentTime = null)
    {
        foreach (var entry in Entries)
        {
            if (entry is BmsHitObjectLifetimeEntry bmsEntry)
                bmsEntry.RefreshLifetime(currentTime);
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

    protected override void UpdateAfterChildrenLife()
    {
        base.UpdateAfterChildrenLife();

        var currentScrollPos = scrollController.CurrentScrollPosition;
        var hitTarget = getHitTargetPosition();
        var scale = scrollController.ScrollSpeedMultiplier / Math.Max(1.0, scrollController.ScrollRange)
                    * Math.Max(1f, DrawHeight - hitTarget);
        var resumeRewinding = isResumeRewinding();

        // Position all alive entries in this column
        foreach (var entry in AliveEntries)
        {
            if (entry.Value is not DrawableBmsHitObject note)
                continue;

            var hitObject = note.HitObject!;
            var startPosition = scrollController.GetVisualScrollPosition(hitObject.StartTime, hitObject.ScrollPositionAtStartTime);
            var offset = (float)((startPosition - currentScrollPos) * scale);
            var y = -(hitTarget + offset);

            note.Y = y;

            if (note is ILongNoteHolder ln && hitObject is BmsLongNote longNote)
            {
                var endPosition = double.IsNaN(longNote.VisualScrollPositionAtEndTime)
                    ? longNote.ScrollPositionAtEndTime
                    : longNote.VisualScrollPositionAtEndTime;
                var endOffset = (float)((endPosition - currentScrollPos) * scale);
                ln.UpdateBodyGeometry(y, -(hitTarget + endOffset));
            }

            // Keeping judgement controllers frozen avoids replaying misses or resetting an active long note.
            if (!resumeRewinding && note.RequiresColumnFrameUpdate)
                note.UpdateColumnFrame();
        }
    }

    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();

        if (!isResumeRewinding())
            return;

        var rewindEndTime = getResumeRewindEndTime();

        foreach (var note in AliveEntries.Values)
        {
            if (note is not ILongNoteHolder longNoteHolder || note.HitObject is not BmsLongNote longNote)
                continue;

            // Pooled drawables reset their LN controller when they re-enter lifetime, while their
            // framework result remains authoritative. EndTime covers HCN head-POOR, whose endpoints
            // are scored synthetically without applying a result to the parent drawable.
            if (longNote.EndTime <= rewindEndTime || (note.Judged && !longNoteHolder.IsHoldingLongNote))
                note.Alpha = 0;
        }
    }
}
