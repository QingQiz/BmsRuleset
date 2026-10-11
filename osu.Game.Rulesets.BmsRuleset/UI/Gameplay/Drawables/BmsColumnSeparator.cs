using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;

/// <summary>
/// Simple vertical lane separator used by legacy column backgrounds.
/// </summary>
internal sealed partial class BmsColumnSeparator : Box
{
    private const float legacy_width_scale = 0.740f;

    public BmsColumnSeparator(float width, Color4 colour)
    {
        RelativeSizeAxes = Axes.Y;
        Width = width;
        // Legacy mania applies this correction to unscaled ColumnLineWidth values.
        Scale = new Vector2(legacy_width_scale, 1);
        // The separator itself can draw the rectangle; a wrapping container adds no geometry.
        LegacyColourCompatibility.ApplyWithDoubledAlpha(this, colour);
        if (width <= 0)
            Alpha = 0;
    }
}
