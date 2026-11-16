using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Graphics.Backgrounds;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Online.Leaderboards;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Scoring;
using osuTK.Graphics;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Ranking;

internal static class BmsRankDisplay
{
    // Negative spacing keeps multi-letter DJ LEVEL badges inside osu!'s fixed 64x32 badge.
    internal static float GetLetterSpacing(LocalisableString label) => label.ToString().Length switch
    {
        3 => -9,
        2 => -5,
        _ => -3,
    };

    internal static float GetLargeLetterSpacing(LocalisableString label) => label.ToString().Length switch
    {
        3 => -35,
        2 => -19,
        _ => -15,
    };
}

internal sealed partial class BmsDrawableRank : CompositeDrawable
{
    internal BmsDrawableRank(ScoreRank rank, GameplayProfile profile)
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
                        Spacing = new Vector2(BmsRankDisplay.GetLetterSpacing(profile.RankLabel(rank)), 0),
                        Padding = new MarginPadding { Top = 5 },
                        Colour = DrawableRank.GetRankLetterColour(rank),
                        Font = OsuFont.Numeric.With(size: 25),
                        Text = profile.RankLabel(rank),
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
    private GameplayProfile profile = BmsGameplayProfile.INSTANCE;

    internal void SetRank(ScoreRank? rank, GameplayProfile selectedProfile)
    {
        if (profile != selectedProfile)
        {
            Rank = null;
            profile = selectedProfile;
        }
        Rank = rank;
    }

    internal BmsUpdateableRank(bool animate = true)
        : base(null, animate)
    {
    }

    protected override Drawable? CreateDrawable(ScoreRank? rank) => rank.HasValue
        ? new BmsDrawableRank(rank.Value, profile)
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
        }
        : null;
}
