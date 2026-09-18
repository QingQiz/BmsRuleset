using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Localisation;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal partial class BmsResultComparisonRow : GridContainer
{
    internal const float BAR_HEIGHT = 12;

    internal static readonly Colour4 CURRENT_COLOUR = BmsResultColours.ACCENT;
    internal static readonly Colour4 BEST_COLOUR = new(166, 166, 166, 255);

    internal static Dimension[] CreateColumns() =>
    [
        new(GridSizeMode.Absolute, 80),
        new(),
        new(GridSizeMode.Absolute, 72),
        new(GridSizeMode.Absolute, 84),
        new(GridSizeMode.Absolute, 110),
    ];

    internal BmsResultComparisonRow(LocalisableString label, double? current, double? best, bool? higherIsBetter,
                                    Colour4? labelColour = null, bool percentage = false, double? scaleMaximum = null)
    {
        RelativeSizeAxes = Axes.X;
        Height = 36;
        ColumnDimensions = CreateColumns();
        var maximum = scaleMaximum ?? Math.Max(current ?? 0, best ?? 0);
        var currentColour = labelColour ?? CURRENT_COLOUR;
        var difference = current - best;
        var improvement = higherIsBetter.HasValue ? difference * (higherIsBetter.Value ? 1 : -1) : null;
        var differenceColour = improvement > 0 ? new Colour4(110, 225, 165, 255)
            : improvement < 0 ? new Colour4(255, 150, 140, 255) : Colour4.White;
        var currentBar = bar(current, currentColour, "Current comparison bar");
        var bestBar = bar(best, BEST_COLOUR, "Best comparison bar");
        // Drawing the shorter bar last keeps both endpoints visible without changing their thickness or scale.
        Drawable[] bars = current > best ? [currentBar, bestBar] : [bestBar, currentBar];
        Content = new Drawable[][]
        {
            [
                new BmsResultFittedText(label, 17, colour: labelColour ?? Colour4.White),
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Horizontal = 10 },
                    Children =
                    [
                        new CircularContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Height = BAR_HEIGHT,
                            Masking = true,
                            Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.White.Opacity(0.035f) },
                        },
                        .. bars,
                    ],
                },
                new BmsResultFittedText(valueText(current), 21, Anchor.CentreRight, colour: currentColour),
                new BmsResultFittedText(valueText(best), 19, Anchor.CentreRight, colour: BEST_COLOUR),
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Left = 12 },
                    Child = new BmsResultFittedText(difference.HasValue
                        ? percentage
                            ? BmsStrings.ResultComparisonPercentageDifference(difference.Value)
                            : BmsStrings.ResultComparisonDifferenceValue((int)difference.Value)
                        : BmsStrings.LeaderboardUnknown, 18, Anchor.CentreRight, colour: differenceColour),
                },
            ],
        };

        Drawable bar(double? value, Colour4 colour, string name) => value.HasValue
            ? new BmsResultBar(maximum > 0 ? value.Value / maximum : 0, colour, showTrack: false)
            {
                Name = name,
                RelativeSizeAxes = Axes.X,
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Height = BAR_HEIGHT,
            }
            : Empty();

        LocalisableString valueText(double? value) => value.HasValue
            ? percentage ? BmsStrings.ResultPercentage(value.Value) : BmsStrings.ResultNumber((int)value.Value)
            : BmsStrings.LeaderboardUnknown;
    }
}
