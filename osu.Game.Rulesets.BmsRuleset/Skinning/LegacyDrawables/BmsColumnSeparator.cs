using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.LegacyDrawables;

/// <summary>
/// Simple vertical lane separator used by legacy column backgrounds.
/// </summary>
internal sealed partial class BmsColumnSeparator : CompositeDrawable
{
    private const float legacy_width_scale = 0.740f;

    public BmsColumnSeparator(float width, Color4 colour)
    {
        RelativeSizeAxes = Axes.Y;
        Width = width;
        // Legacy mania applies this correction to unscaled ColumnLineWidth values.
        Scale = new Vector2(legacy_width_scale, 1);
        Alpha = width > 0 ? 1 : 0;

        InternalChild = LegacyColourCompatibility.ApplyWithDoubledAlpha(new Box
        {
            RelativeSizeAxes = Axes.Both,
        }, colour);
    }
}
