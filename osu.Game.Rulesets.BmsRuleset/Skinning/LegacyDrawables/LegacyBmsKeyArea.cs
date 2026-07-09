using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.LegacyDrawables;

internal sealed partial class LegacyBmsKeyArea : CompositeDrawable, IKeyBindingHandler<BmsAction>
{
    private readonly BmsSkinComponentLookup lookup;
    private readonly Drawable? upSprite;
    private readonly Drawable? downSprite;

    public LegacyBmsKeyArea(BmsLegacySkinTransformer transformer, BmsSkinComponentLookup lookup)
    {
        this.lookup = lookup;

        RelativeSizeAxes = Axes.Both;

        upSprite = transformer.GetLegacyAnimation(transformer.GetKeyImageName(lookup, false))?.With(d =>
        {
            d.Anchor = Anchor.BottomCentre;
            d.Origin = Anchor.BottomCentre;
            d.RelativeSizeAxes = Axes.X;
            d.Width = 1;
        });

        downSprite = transformer.GetLegacyAnimation(transformer.GetKeyImageName(lookup, true))?.With(d =>
        {
            d.Anchor = Anchor.BottomCentre;
            d.Origin = Anchor.BottomCentre;
            d.RelativeSizeAxes = Axes.X;
            d.Width = 1;
            d.Alpha = 0;
        });

        InternalChild = new Container
        {
            Anchor = Anchor.BottomCentre,
            Origin = Anchor.BottomCentre,
            RelativeSizeAxes = Axes.Both,
            Children =
            [
                upSprite ?? Empty(),
                downSprite ?? Empty(),
            ],
        };
    }

    /// <summary>
    /// Calculates how much of an image extends past the hit position line.
    /// </summary>
    internal static float CalculateBottomOverflow(float imageHeight, float hitPosition) => System.Math.Max(0, imageHeight - hitPosition);

    /// <summary>
    /// Calculates the scale factor to fit an image width to a target column width.
    /// </summary>
    internal static float CalculateColumnWidthScale(float imageWidth, float columnWidth) => imageWidth > 0 && columnWidth > 0 ? columnWidth / imageWidth : 1;

    public bool OnPressed(KeyBindingPressEvent<BmsAction> e)
    {
        if (lookup.ColumnIndex == null || BmsKeyBindingConfiguration.ActionToColumn(e.Action, lookup.LayoutVariant) != lookup.ColumnIndex)
            return false;

        if (downSprite == null)
            return false;

        upSprite?.FadeTo(0);
        downSprite.FadeTo(1);
        return false;
    }

    public void OnReleased(KeyBindingReleaseEvent<BmsAction> e)
    {
        if (lookup.ColumnIndex == null || BmsKeyBindingConfiguration.ActionToColumn(e.Action, lookup.LayoutVariant) != lookup.ColumnIndex)
            return;

        upSprite?.Delay(BmsLegacySkinTransformer.HIT_EXPLOSION_FADE_IN_DURATION).FadeTo(1);
        downSprite?.Delay(BmsLegacySkinTransformer.HIT_EXPLOSION_FADE_IN_DURATION).FadeTo(0);
    }
}
