using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Judgement;

internal sealed partial class BmsJudgementBridge
{
    private BmsColumnJudgementRuntime context => column.Judgements;

    private BmsColumnHitObjectContainer container => (BmsColumnHitObjectContainer)column.HitObjectContainer;

    internal void ObserveVisualTime(DrawableBmsHitObject drawable, BmsVisualTimeObserver observer)
        => container.ObserveVisualTime(drawable, observer);

    internal void SynchroniseReplayVisuals()
    {
        ((BmsHitExplosionLayer)column.HitExplosionArea).ResetReplayPulses();
        foreach (var drawable in container.AliveEntries.Values)
            if (drawable is DrawableBmsHitObject bms)
                bms.SynchroniseReplayState();
    }

    internal void Register(DrawableBmsHitObject drawable)
    {
        if (drawable.HitObject is BmsNote or BmsLongNote)
            Bind(drawable);
    }

    internal void UnbindRemovedNote(DrawableBmsHitObject drawable) => UnbindNoteRepresentation(drawable, drawable.BindingGeneration);

    internal void UnbindNoteRepresentation(DrawableBmsHitObject drawable, long generation)
        => Unbind(drawable, generation);

    internal void RefreshNoteRepresentation(DrawableBmsHitObject drawable)
    {
        var note = drawable.HitObject;
        if (note is BmsLandmine)
        {
            BindLandmine(drawable);
            return;
        }

        // Reapply has no AddDrawable callback, so it must refresh the representation binding here.
        if (note is BmsNote or BmsLongNote)
        {
            Bind(drawable);
            container.InvalidateCoreOrder();
        }

        context.RefreshNoteConfiguration(note);
    }
}
