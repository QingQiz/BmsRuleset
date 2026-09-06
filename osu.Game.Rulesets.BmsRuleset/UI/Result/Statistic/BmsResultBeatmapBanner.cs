using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal partial class BmsResultBeatmapBanner : Container
{
    internal BmsResultBeatmapBanner(IBeatmapInfo beatmap, bool fitText = false)
    {
        var metadata = beatmap.BeatmapSet?.Metadata ?? beatmap.Metadata;
        Name = "Result beatmap banner";
        RelativeSizeAxes = Axes.Both;
        Masking = true;
        CornerRadius = 8;
        Children =
        [
            new UpdateableBeatmapBackgroundSprite
            {
                RelativeSizeAxes = Axes.Both,
                BackgroundLoadDelay = 0,
                Beatmap = { Value = beatmap },
            },
            new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black, Alpha = 0.35f },
            fitText
                ? new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Horizontal = 6, Vertical = 4 },
                    RowDimensions = [new Dimension(GridSizeMode.Relative, 0.4f), new Dimension(), new Dimension()],
                    Content = new[]
                    {
                        new Drawable[] { new BmsResultFittedText(new RomanisableString(metadata.TitleUnicode, metadata.Title), 22, Anchor.Centre) },
                        new Drawable[] { new BmsResultFittedText(new RomanisableString(metadata.ArtistUnicode, metadata.Artist), 16, Anchor.Centre) },
                        new Drawable[] { new BmsResultFittedText(beatmap.DifficultyName, 16, Anchor.Centre) },
                    },
                }
                : new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Padding = new MarginPadding(6),
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 4),
                    Children =
                    [
                        metadataText(new RomanisableString(metadata.TitleUnicode, metadata.Title), 26),
                        metadataText(new RomanisableString(metadata.ArtistUnicode, metadata.Artist), 20),
                        metadataText(beatmap.DifficultyName, 20),
                    ],
                },
        ];
    }

    private static Container metadataText(LocalisableString text, float size) => new()
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Masking = true,
        Child = new MarqueeContainer
        {
            NonOverflowingContentAnchor = Anchor.TopCentre,
            OverflowSpacing = 32,
            CreateContent = () => new OsuSpriteText
            {
                Text = text,
                Font = OsuFont.GetFont(size: size, weight: FontWeight.SemiBold),
            },
        },
    };
}
