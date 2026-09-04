using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Backgrounds;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.Leaderboards;
using osu.Game.Scoring;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Ranking;

internal static class BmsRankDisplay
{
    internal static string GetRankLetter(ScoreRank rank) => rank switch
    {
        ScoreRank.X or ScoreRank.XH => "S",
        ScoreRank.S or ScoreRank.SH => "AAA",
        ScoreRank.A => "AA",
        ScoreRank.B => "A",
        ScoreRank.C => "B",
        ScoreRank.D => "C",
        _ => "F",
    };

    // Negative spacing keeps multi-letter DJ LEVEL badges inside osu!'s fixed 64x32 badge.
    internal static float GetLetterSpacing(ScoreRank rank) => GetRankLetter(rank).Length switch
    {
        3 => -9,
        2 => -5,
        _ => -3,
    };

    internal static float GetLargeLetterSpacing(ScoreRank rank) => GetRankLetter(rank).Length switch
    {
        3 => -35,
        2 => -19,
        _ => -15,
    };
}

internal sealed partial class BmsDrawableRank : CompositeDrawable
{
    internal BmsDrawableRank(ScoreRank rank)
    {
        RelativeSizeAxes = Axes.Both;
        FillMode = FillMode.Fit;
        FillAspectRatio = 2;

        var rankColour = OsuColour.ForRank(rank);
        InternalChild = new DrawSizePreservingFillContainer
        {
            TargetDrawSize = new Vector2(64, 32),
            Strategy = DrawSizePreservationStrategy.Minimum,
            Child = new CircularContainer
            {
                Masking = true,
                RelativeSizeAxes = Axes.Both,
                Children =
                [
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = rankColour,
                    },
                    new Triangles
                    {
                        RelativeSizeAxes = Axes.Both,
                        ColourDark = rankColour.Darken(0.1f),
                        ColourLight = rankColour.Lighten(0.1f),
                        Velocity = 0.25f,
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Spacing = new Vector2(BmsRankDisplay.GetLetterSpacing(rank), 0),
                        Padding = new MarginPadding { Top = 5 },
                        Colour = DrawableRank.GetRankLetterColour(rank),
                        Font = OsuFont.Numeric.With(size: 25),
                        Text = BmsRankDisplay.GetRankLetter(rank),
                        ShadowColour = Color4.Black.Opacity(0.3f),
                        ShadowOffset = new Vector2(0, 0.08f),
                        Shadow = true,
                    },
                ],
            },
        };
    }
}

internal sealed partial class BmsUpdateableRank : UpdateableRank
{
    internal BmsUpdateableRank(ScoreRank? rank = null, bool animate = true)
        : base(rank, animate)
    {
    }

    protected override Drawable? CreateDrawable(ScoreRank? rank) => rank.HasValue
        ? new BmsDrawableRank(rank.Value)
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
        }
        : null;
}
