// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.
// Adapted from osu.Game.Screens.Ranking.Expanded.Accuracy.RankText.
// Based on osu! revision 3c1c96f742e7aae2ff67a7361e058fe91ca3b955.
// BMS owns these components so rank lettering does not depend on native UI patches.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.BmsRuleset.UI.Ranking;
using osu.Game.Scoring;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal partial class BmsRankText : CompositeDrawable
{
    private readonly ScoreRank rank;

    private BufferedContainer flash = null!;
    private BufferedContainer superFlash = null!;
    private GlowingSpriteText rankText = null!;

    internal BmsRankText(ScoreRank rank)
    {
        this.rank = rank;

        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;

        Alpha = 0;
        AutoSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren =
        [
            rankText = new GlowingSpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                GlowColour = OsuColour.ForRank(rank),
                Spacing = new Vector2(BmsRankDisplay.GetLargeLetterSpacing(rank), 0),
                Text = BmsRankDisplay.GetRankLetter(rank),
                Font = OsuFont.Numeric.With(size: 76),
                UseFullGlyphHeight = false
            },
            superFlash = new BufferedContainer(cachedFrameBuffer: true)
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                BlurSigma = new Vector2(85),
                Size = new Vector2(600),
                Blending = BlendingParameters.Additive,
                Alpha = 0,
                Children =
                [
                    new Box
                    {
                        Colour = Color4.White,
                        Size = new Vector2(150),
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                    },
                ],
            },
            flash = new BufferedContainer(cachedFrameBuffer: true)
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                BlurSigma = new Vector2(35),
                BypassAutoSizeAxes = Axes.Both,
                Size = new Vector2(200),
                Blending = BlendingParameters.Additive,
                Alpha = 0,
                Scale = new Vector2(1.8f),
                Children =
                [
                    new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Spacing = new Vector2(BmsRankDisplay.GetLargeLetterSpacing(rank), 0),
                        Text = BmsRankDisplay.GetRankLetter(rank),
                        Font = OsuFont.Numeric.With(size: 76),
                        UseFullGlyphHeight = false,
                        Shadow = false
                    },
                ],
            },
        ];
    }

    public void Appear()
    {
        this.FadeIn();

        if (rank < ScoreRank.A)
        {
            this
                .MoveToOffset(new Vector2(0, -20))
                .MoveToOffset(new Vector2(0, 20), 200, Easing.OutBounce);

            if (rank <= ScoreRank.D)
            {
                this.Delay(700)
                    .RotateTo(5, 150, Easing.In)
                    .MoveToOffset(new Vector2(0, 3), 150, Easing.In);
            }

            this.FadeInFromZero(200, Easing.OutQuint);
            return;
        }

        flash.Colour = OsuColour.ForRank(rank);

        if (rank >= ScoreRank.S)
            rankText.ScaleTo(1.05f).ScaleTo(1, 3000, Easing.OutQuint);

        if (rank >= ScoreRank.X)
        {
            flash.FadeOutFromOne(3000);
            superFlash.FadeOutFromOne(800, Easing.OutQuint);
        }
        else
        {
            flash.FadeOutFromOne(1200, Easing.OutQuint);
        }
    }
}
