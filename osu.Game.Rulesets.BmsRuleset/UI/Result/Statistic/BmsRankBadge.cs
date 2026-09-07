// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.
// Adapted from osu.Game.Screens.Ranking.Expanded.Accuracy.RankBadge.
// Based on osu! revision 3c1c96f742e7aae2ff67a7361e058fe91ca3b955.
// BMS owns these components so rank lettering does not depend on native UI patches.

using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Rulesets.BmsRuleset.UI.Ranking;
using osu.Game.Scoring;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal partial class BmsRankBadge : CompositeDrawable
{
    public readonly double Accuracy;

    private readonly double displayPosition;

    public readonly ScoreRank Rank;

    private Drawable rankContainer = null!;
    private Drawable overlay = null!;

    internal BmsRankBadge(double accuracy, double position, ScoreRank rank)
    {
        Accuracy = accuracy;
        displayPosition = position;
        Rank = rank;

        RelativeSizeAxes = Axes.Both;
        Alpha = 0;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChild = rankContainer = new Container
        {
            Origin = Anchor.Centre,
            Size = new Vector2(28, 14),
            Children =
            [
                new BmsDrawableRank(Rank),
                overlay = new CircularContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Blending = BlendingParameters.Additive,
                    Masking = true,
                    EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Glow,
                        Colour = OsuColour.ForRank(Rank).Opacity(0.2f),
                        Radius = 10,
                    },
                    Child = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Alpha = 0,
                        AlwaysPresent = true,
                    }
                }
            ],
        };
    }

    public void Appear()
    {
        this.FadeIn(50);
        overlay.FadeIn().FadeOut(500, Easing.In);
    }

    protected override void Update()
    {
        base.Update();

        // Starts at -90deg (top) and moves counter-clockwise by the accuracy
        rankContainer.Position = circlePosition(-MathF.PI / 2 - (1 - (float)displayPosition) * MathF.PI * 2);
    }

    private Vector2 circlePosition(float t)
        => DrawSize / 2 + new Vector2(MathF.Cos(t), MathF.Sin(t)) * DrawSize / 2;
}
