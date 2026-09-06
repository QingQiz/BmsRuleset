using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.Scoring;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal partial class BmsResultFastSlow : CompositeDrawable
{
    internal int FastCount { get; }

    internal int SlowCount { get; }

    internal double FastProportion { get; }

    internal BmsResultFastSlow(IReadOnlyList<HitEvent> hitEvents)
    {
        RelativeSizeAxes = Axes.Both;

        // Match Timeline's timing events, excluding automatic misses and landmines.
        foreach (var hit in hitEvents)
        {
            if (hit.HitObject is not BmsHitObject or BmsLandmine || !hit.Result.IsBasic() || !hit.Result.IsHit())
                continue;

            if (hit.TimeOffset < 0)
                FastCount++;
            else if (hit.TimeOffset > 0)
                SlowCount++;
        }

        var total = FastCount + SlowCount;
        FastProportion = total > 0 ? (double)FastCount / total : 0;

        InternalChild = new GridContainer
        {
            RelativeSizeAxes = Axes.Both,
            RowDimensions = [new Dimension(GridSizeMode.Relative, 0.35f), new Dimension()],
            ColumnDimensions = [new Dimension(GridSizeMode.Absolute, 44), new Dimension(), new Dimension(GridSizeMode.Absolute, 44)],
            Content = new[]
            {
                new Drawable[]
                {
                    new BmsResultFittedText(BmsStrings.Fast, 14, Anchor.CentreLeft, BmsResultColours.FAST, useFullGlyphHeight: false),
                    new Container(),
                    new BmsResultFittedText(BmsStrings.Slow, 14, Anchor.CentreRight, BmsResultColours.SLOW, useFullGlyphHeight: false),
                },
                new Drawable[]
                {
                    new BmsResultFittedText(BmsStrings.ResultNumber(FastCount), 22, Anchor.CentreLeft, BmsResultColours.FAST, useFullGlyphHeight: false)
                    {
                        Name = "Fast count",
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Horizontal = 8 },
                        Children =
                        [
                            new CircularContainer
                            {
                                Name = "Fast slow balance bar",
                                RelativeSizeAxes = Axes.Both,
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Height = 0.5f,
                                Masking = true,
                                Children =
                                [
                                    new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.Gray(0.4f), Alpha = 0.35f },
                                    new Box
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Width = (float)FastProportion,
                                        Colour = BmsResultColours.FAST,
                                    },
                                    new Box
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Anchor = Anchor.TopRight,
                                        Origin = Anchor.TopRight,
                                        Width = total > 0 ? 1 - (float)FastProportion : 0,
                                        Colour = BmsResultColours.SLOW,
                                    },
                                ],
                            },
                            new Box
                            {
                                Name = "Fast slow midpoint",
                                RelativeSizeAxes = Axes.Y,
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Width = 2,
                                Height = 0.85f,
                                Colour = Color4.White,
                            },
                        ],
                    },
                    new BmsResultFittedText(BmsStrings.ResultNumber(SlowCount), 22, Anchor.CentreRight, BmsResultColours.SLOW, useFullGlyphHeight: false)
                    {
                        Name = "Slow count",
                    },
                },
            },
        };
    }

}
