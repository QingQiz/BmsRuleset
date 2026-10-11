using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Judgement;

internal sealed partial class BmsJudgementBridge
{
    private readonly List<BmsHitObject> activationOrder = [];
    private readonly List<BmsLandmine> landmineOrder = [];

    private BmsColumnJudgementRuntime context => column.Judgements;

    private BmsColumnHitObjectContainer container => (BmsColumnHitObjectContainer)column.HitObjectContainer;

    internal void ObserveVisualTime(DrawableBmsHitObject drawable, BmsVisualTimeObserver observer)
        => container.ObserveVisualTime(drawable, observer);

    private void bindScheduledRepresentation(DrawableBmsHitObject drawable)
        => boundObjects[drawable].ScheduledObject = context.Schedule.Bind(drawable.HitObject);

    internal bool TryGetScheduledObject(DrawableBmsHitObject drawable, out BmsScheduledObject item)
    {
        if (boundObjects.TryGetValue(drawable, out var binding) && isCurrent(binding) && binding.ScheduledObject is { } scheduled)
        {
            item = scheduled;
            return true;
        }

        item = default;
        return false;
    }

    internal void CompleteScheduledRemoval(DrawableBmsHitObject drawable, in BmsFrameContext frame)
    {
        if (TryGetScheduledObject(drawable, out var item)) context.Schedule.CompleteRemoval(item, frame);
    }

    internal void RestoreRewoundState(in BmsFrameContext frame)
    {
        if (!context.Schedule.BeginRewindStep(frame))
            return;

        // Framework results have reverted; rebuild visuals before publishing the restored logical indexes.
        foreach (var drawable in container.AliveEntries.Values)
        {
            if (drawable is DrawableBmsHitObject bms)
                bms.RestoreRewoundVisuals();
        }

        context.RestoreExecutionState(frame);
    }

    internal void Register(DrawableBmsHitObject drawable, bool appended = false)
    {
        if (drawable.HitObject is BmsLandmine mine)
        {
            Bind(drawable);
            context.ActivateLandmine(mine);
        }
        else if (drawable.HitObject is BmsNote or BmsLongNote)
        {
            Bind(drawable);
            context.ActivateNote(drawable.HitObject);
            bindScheduledRepresentation(drawable);
        }
        else
            return;

        if (!appended)
            refreshActiveOrder();
    }

    internal void Unregister(DrawableBmsHitObject drawable)
    {
        if (drawable.HitObject is BmsLandmine mine)
        {
            context.RemoveLandmine(mine);
            return;
        }

        if (drawable.HitObject is not (BmsNote or BmsLongNote)) return;

        context.BeginNoteRemoval(drawable.HitObject);
        if (TryGetScheduledObject(drawable, out var scheduled)) context.Schedule.BeginRemoval(scheduled);
    }

    internal void UnbindRemovedNote(DrawableBmsHitObject drawable) => UnbindNoteRepresentation(drawable, drawable.BindingGeneration);

    internal void UnbindNoteRepresentation(DrawableBmsHitObject drawable, long generation)
        => Unbind(drawable, generation);

    internal void RefreshNoteRepresentation(DrawableBmsHitObject drawable)
    {
        var note = drawable.HitObject;
        if (note is BmsLandmine mine)
        {
            if (context.IsLandmineActive(mine)) Bind(drawable);
            return;
        }

        // Reapply has no AddDrawable callback. Replace only the binding, retaining ordering and histories.
        if (context.IsNoteActive(note))
        {
            Bind(drawable);
            bindScheduledRepresentation(drawable);
            container.InvalidateCoreOrder();
        }

        context.RefreshNoteConfiguration(note, column.PreserveLongNoteHistory);
    }

    private void refreshActiveOrder()
    {
        // Dictionary slot reuse determines equal-candidate winners in the framework's alive map.
        activationOrder.Clear();
        landmineOrder.Clear();
        foreach (var drawable in container.AliveEntries.Values)
        {
            if (drawable.HitObject is BmsLandmine mine && context.IsLandmineActive(mine)) landmineOrder.Add(mine);
            if (drawable.HitObject is BmsHitObject note && context.IsNoteActive(note)) activationOrder.Add(note);
        }

        context.SetNoteActivationOrder(activationOrder);
        context.SetLandmineActivationOrder(landmineOrder);
    }
}
