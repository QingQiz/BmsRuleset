using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal partial class BmsResultBar : CompositeDrawable
{
    private readonly CircularContainer fill;

    internal double Proportion { get; }

    internal BmsResultBar(double proportion, ColourInfo colour)
    {
        Proportion = Math.Clamp(proportion, 0, 1);
        RelativeSizeAxes = Axes.Both;
        InternalChildren =
        [
            new CircularContainer
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                Child = new Box { RelativeSizeAxes = Axes.Both, Colour = colour, Alpha = 0.18f },
            },
            new Container
            {
                Name = "Bar fill reveal",
                RelativeSizeAxes = Axes.Both,
                Width = (float)Proportion,
                Masking = true,
                Alpha = Proportion > 0 ? 1 : 0,
                Child = fill = new CircularContainer
                {
                    Name = "Bar fill shape",
                    RelativeSizeAxes = Axes.Y,
                    Masking = true,
                    Child = new Box { RelativeSizeAxes = Axes.Both, Colour = colour },
                },
            },
        ];
    }

    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();
        // Clip small values out of a full-sized cap so they retain the track's curvature and true proportion.
        fill.Width = Math.Max(DrawHeight, DrawWidth * (float)Proportion);
    }
}
