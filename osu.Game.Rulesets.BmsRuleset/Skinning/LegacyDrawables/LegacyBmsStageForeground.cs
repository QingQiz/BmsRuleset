using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.LegacyDrawables;

/// <summary>
/// Legacy bottom stage overlay.
/// </summary>
/// <remarks>
/// This is a decorative foreground strip anchored to the bottom of the stage. It intentionally does
/// not participate in input or hit-object layout.
/// </remarks>
internal sealed partial class LegacyBmsStageForeground : CompositeDrawable
{
    public LegacyBmsStageForeground(BmsLegacySkinTransformer transformer)
    {
        RelativeSizeAxes = Axes.Both;

        InternalChild = transformer.GetLegacyAnimation(transformer.GetStageForegroundImageName())?.With(d =>
        {
            d.Anchor = Anchor.BottomCentre;
            d.Origin = Anchor.BottomCentre;
            d.Scale = new Vector2(1.6f);
        }) ?? Empty();
    }
}
