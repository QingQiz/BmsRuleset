using System;
using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal sealed partial class BmsDrawableJudgementOutput
{
    private readonly Dictionary<DrawableBmsHitObject, (long Generation, BmsScheduledObject Handle)> scheduledRepresentations = [];
    private readonly List<BmsHitObject> activationOrder = [];
    private readonly List<BmsLandmine> landmineOrder = [];
    private BmsColumnJudgementContext context => column.Judgements;
    private BmsColumnHitObjectContainer container => (BmsColumnHitObjectContainer)column.HitObjectContainer;

    internal DrawableBmsHitObject? FirstPendingHead => context.FirstPendingNote is { } note ? GetRepresentation(note) : null;

    private void bindScheduledRepresentation(DrawableBmsHitObject drawable)
        => scheduledRepresentations[drawable] = (drawable.BindingGeneration, context.Schedule.Bind(drawable.HitObject));

    internal bool TryGetScheduledObject(DrawableBmsHitObject drawable, out BmsScheduledObject item)
    {
        if (scheduledRepresentations.TryGetValue(drawable, out var binding) && binding.Generation == drawable.BindingGeneration
            && ReferenceEquals(binding.Handle.Object, drawable.HitObject))
        {
            item = binding.Handle;
            return true;
        }
        item = default;
        return false;
    }

    internal void CompleteScheduledRemoval(DrawableBmsHitObject drawable, in BmsFrameContext frame)
    {
        if (TryGetScheduledObject(drawable, out var item)) context.Schedule.CompleteRemoval(item, frame);
    }

    internal void Register(DrawableBmsHitObject drawable)
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
        refreshActiveOrder();
    }

    internal void UnbindRemovedNote(DrawableBmsHitObject drawable) => UnbindNoteRepresentation(drawable, drawable.BindingGeneration);

    internal void UnbindNoteRepresentation(DrawableBmsHitObject drawable, long generation)
    {
        // A late free belongs to that representation, even when its HitObject was cleared or replaced.
        if (scheduledRepresentations.TryGetValue(drawable, out var binding) && binding.Generation == generation)
        {
            scheduledRepresentations.Remove(drawable);
            context.Schedule.Unbind(binding.Handle);
        }
        Unbind(drawable, generation);
    }

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
