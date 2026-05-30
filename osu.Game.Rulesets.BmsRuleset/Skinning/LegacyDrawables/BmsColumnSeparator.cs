using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.LegacyDrawables;

/// <summary>
/// Simple vertical lane separator used by legacy column backgrounds.
/// </summary>
internal sealed partial class BmsColumnSeparator : CompositeDrawable
{
    public BmsColumnSeparator(float width, Color4 colour)
    {
        RelativeSizeAxes = Axes.Y;
        Width = width;
        Alpha = width > 0 ? 1 : 0;

        InternalChild = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Colour = colour,
        };
    }
}
