using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.Scoring;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal partial class BmsResultJudgementRow : CompositeDrawable
{
    private readonly BmsResultBar bar;

    internal HitResult Result { get; }

    internal int Count { get; }

    internal BmsResultJudgementRow(HitResult result, int count, int total)
    {
        Result = result;
        Count = count;
        RelativeSizeAxes = Axes.Both;
        Padding = new MarginPadding { Vertical = 1 };
        var colour = BmsHitResultColours.ForHitResult(result);
        var labelColour = result == HitResult.Perfect
            ? ColourInfo.GradientVertical(new Color4(102, 255, 204, 255), new Color4(255, 154, 215, 255))
            : ColourInfo.SingleColour(colour);
        InternalChild = new GridContainer
        {
            RelativeSizeAxes = Axes.Both,
            ColumnDimensions = [new Dimension(GridSizeMode.Absolute, 76), new Dimension(), new Dimension(GridSizeMode.Absolute, 48)],
            Content = new[]
            {
                new Drawable[]
                {
                    new BmsResultFittedText(BmsStrings.ResultJudgement(result), 22, colour: labelColour, useFullGlyphHeight: false),
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Right = 14 },
                        Child = bar = new BmsResultBar(total > 0 ? (double)count / total : 0, colour)
                        {
                            RelativeSizeAxes = Axes.X,
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Height = 10,
                        },
                    },
                    new BmsResultFittedText(BmsStrings.ResultNumber(count), 26, Anchor.CentreRight, useFullGlyphHeight: false),
                },
            },
        };
    }

    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();
        bar.Height = Math.Min(Math.Max(0, ChildSize.Y - 2), Math.Clamp(ChildSize.Y * 0.45f, 10, 16));
    }
}
