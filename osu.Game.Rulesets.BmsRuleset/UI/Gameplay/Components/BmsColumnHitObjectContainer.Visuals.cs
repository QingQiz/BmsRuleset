using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

internal delegate void BmsVisualTimeObserver(float y, in BmsColumnVisualFrame frame);

public sealed partial class BmsColumnHitObjectContainer
{
    private readonly Dictionary<DrawableBmsHitObject, VisualObservation> visualObservations = [];

    internal void ObserveVisualTime(DrawableBmsHitObject drawable, BmsVisualTimeObserver observer)
    {
        var observation = new VisualObservation(drawable.HitObject, drawable.BindingGeneration, observer);
        visualObservations[drawable] = observation;
        drawable.SetVisualTimeObserver(observation.Observer);
    }

    internal void RemoveVisualObservation(DrawableBmsHitObject drawable, long generation)
    {
        if (visualObservations.TryGetValue(drawable, out var observation) && observation.Generation == generation)
        {
            visualObservations.Remove(drawable);
            drawable.ClearVisualTimeObserver(generation);
        }
    }

    private void observeDeferredVisuals(in BmsColumnVisualFrame frame)
    {
        // Visual lifetime is independent of pending tails and active HCN bodies.
        foreach (var (drawable, observation) in visualObservations)
        {
            if (!observation.IsCurrent(drawable)) continue;
            var note = observation.Note;
            var position = scrollController.GetVisualScrollPosition(note.StartTime, note.ScrollPositionAtStartTime);
            observation.Observer(frame.YAt(position), frame);
        }
    }

    private readonly record struct VisualObservation(BmsHitObject Note, long Generation, BmsVisualTimeObserver Observer)
    {
        internal bool IsCurrent(DrawableBmsHitObject drawable)
            => drawable.BindingGeneration == Generation && ReferenceEquals(drawable.HitObject, Note);
    }
}
