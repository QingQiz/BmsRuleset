// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Overlays;

namespace osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Components;

internal sealed partial class BmsWedgeBackground : InputBlockingContainer
{
    private static float startAlpha => 0.9f;

    private static float finalAlpha => 0.6f;

    private static float widthForGradient => 0.3f;

    [BackgroundDependencyLoader]
    private void load(OverlayColourProvider colourProvider)
    {
        RelativeSizeAxes = Axes.Both;

        InternalChildren =
        [
            new Box
            {
                Blending = BlendingParameters.Additive,
                RelativeSizeAxes = Axes.Both,
                Width = 0.6f,
                Alpha = 0.5f,
                Colour = ColourInfo.GradientHorizontal(colourProvider.Background2, colourProvider.Background2.Opacity(0)),
            },
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Width = 1 - widthForGradient,
                Colour = colourProvider.Background5.Opacity(startAlpha),
            },
            new Box
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                RelativeSizeAxes = Axes.Both,
                Width = widthForGradient,
                Colour = ColourInfo.GradientHorizontal(colourProvider.Background5.Opacity(startAlpha), colourProvider.Background5.Opacity(finalAlpha)),
            },
        ];
    }
}
