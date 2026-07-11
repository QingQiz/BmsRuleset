using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Overlays.SkinEditor;
using osu.Game.Screens;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

public abstract partial class BmsHudComponent : CompositeDrawable, ISerialisableDrawable
{
    private const float minimum_visible_size = 10;

    public bool UsesFixedAnchor { get; set; }

    [Resolved(CanBeNull = true)]
    protected SkinEditorOverlay? SkinEditor { get; private set; }

    protected override void Update()
    {
        base.Update();

        if (SkinEditor?.State.Value == Visibility.Visible)
            ClampToEditorBounds();
    }

    internal void ClampToEditorBounds()
    {
        if (Parent == null)
            return;

        var boundsDrawable = this.FindClosestParent<OsuScreen>() as Drawable
                             ?? this.FindClosestParent<ISerialisableDrawableContainer>() as Drawable;

        if (boundsDrawable == null)
            return;

        var bounds = boundsDrawable.ScreenSpaceDrawQuad.AABBFloat;
        var componentBounds = ScreenSpaceDrawQuad.AABBFloat;

        if (!isFiniteAndPositive(bounds.Width) || !isFiniteAndPositive(bounds.Height) ||
            !isFiniteAndPositive(componentBounds.Width) || !isFiniteAndPositive(componentBounds.Height))
            return;

        var correction = new Vector2(
            correctionForAxis(componentBounds.Left, componentBounds.Right, bounds.Left, bounds.Right),
            correctionForAxis(componentBounds.Top, componentBounds.Bottom, bounds.Top, bounds.Bottom));

        if (correction == Vector2.Zero)
            return;

        var targetOrigin = Parent.ToLocalSpace(ToScreenSpace(OriginPosition) + correction) - AnchorPosition;
        var relativeAxes = RelativePositionAxes;

        // Skin editor movement is expressed in absolute parent coordinates. Temporarily matching
        // that coordinate space keeps clamping correct for future relative-position HUDs too.
        RelativePositionAxes = Axes.None;
        Position = targetOrigin;
        RelativePositionAxes = relativeAxes;
    }

    private static float correctionForAxis(float start, float end, float boundsStart, float boundsEnd)
    {
        var size = end - start;
        var boundsSize = boundsEnd - boundsStart;
        var requiredVisibleSize = Math.Min(minimum_visible_size, Math.Min(size, boundsSize));

        if (end < boundsStart + requiredVisibleSize)
            return boundsStart + requiredVisibleSize - end;

        return start > boundsEnd - requiredVisibleSize ? boundsEnd - requiredVisibleSize - start : 0;
    }

    private static bool isFiniteAndPositive(float value) => float.IsFinite(value) && value > 0;
}
