using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Primitives;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;

internal partial class BmsPracticePlayer
{
    private (Drawable Drawable, Quad Bounds)[] previousPracticeLayout = [];
    private int stablePracticeLayoutFrames;

    private bool prepareHudLayout()
    {
        var containers = HUDOverlay.Children.OfType<SkinnableContainer>().ToArray();
        if (containers.Any(container => !container.ComponentsLoaded))
        {
            stablePracticeLayoutFrames = 0;
            return false;
        }

        // Skin components load separately from Player. Their autosizing and stage transforms
        // need complete update passes before the previous attempt's presentation can be released.
        HUDOverlay.FinishTransforms(true);
        HUDOverlay.UpdateSubTree();
        var playfield = (BmsPlayfield)PracticeRuleset.Playfield;
        var components = containers.SelectMany(container => container.Components).Cast<Drawable>()
            .Prepend(playfield.Stage).Concat(playfield.Stage.Columns.Select(column => column.KeyArea)).ToArray();
        if (components.Any(component => !component.IsLoaded))
        {
            stablePracticeLayoutFrames = 0;
            return false;
        }

        var layout = components.Select(component => (Drawable: component, Bounds: component.ScreenSpaceDrawQuad)).ToArray();
        var stable = previousPracticeLayout.Length == layout.Length;
        for (var i = 0; stable && i < layout.Length; i++)
        {
            var previous = previousPracticeLayout[i];
            var current = layout[i];
            stable = previous.Drawable == current.Drawable
                     && Vector2.DistanceSquared(previous.Bounds.TopLeft, current.Bounds.TopLeft) < 0.0001f
                     && Vector2.DistanceSquared(previous.Bounds.TopRight, current.Bounds.TopRight) < 0.0001f
                     && Vector2.DistanceSquared(previous.Bounds.BottomLeft, current.Bounds.BottomLeft) < 0.0001f
                     && Vector2.DistanceSquared(previous.Bounds.BottomRight, current.Bounds.BottomRight) < 0.0001f;
        }

        previousPracticeLayout = layout;
        stablePracticeLayoutFrames = stable ? stablePracticeLayoutFrames + 1 : 0;
        return stablePracticeLayoutFrames >= 2;
    }
}
