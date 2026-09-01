using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Screens.Select;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

/// <summary>
/// Shared visual content for a standalone beatmap and a course stage panel.
/// The owning panel remains responsible for model-specific score and background lifecycles.
/// </summary>
internal partial class BmsStandaloneBeatmapContent : CompositeDrawable
{
    private readonly BindableBool selected;
    private readonly bool includeSetMetadata;

    internal PanelSetBackground BeatmapBackground { get; private set; } = null!;

    internal BmsPanelLocalRankDisplay LocalRank { get; private set; } = null!;

    internal OsuSpriteText TitleText { get; private set; } = null!;

    internal OsuSpriteText ArtistText { get; private set; } = null!;

    internal OsuSpriteText KeyCountText { get; private set; } = null!;

    internal OsuSpriteText DifficultyText { get; private set; } = null!;

    internal OsuSpriteText AuthorText { get; private set; } = null!;

    internal StarRatingDisplay StarRatingDisplay { get; private set; } = null!;

    internal BmsPanelBeatmapStandalone.SpreadDisplay SpreadDisplay { get; private set; } = null!;

    internal FillFlowContainer MainFill { get; private set; } = null!;

    internal PanelUpdateBeatmapButton? UpdateButton { get; private set; }

    internal BeatmapSetOnlineStatusPill? StatusPill { get; private set; }

    internal BmsStandaloneBeatmapContent(BindableBool selected, bool includeSetMetadata)
    {
        this.selected = selected;
        this.includeSetMetadata = includeSetMetadata;

        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load(OverlayColourProvider colourProvider)
    {
        BeatmapBackground = new PanelSetBackground();
        LocalRank = new BmsPanelLocalRankDisplay
        {
            Scale = new Vector2(0.8f),
            Origin = Anchor.CentreLeft,
            Anchor = Anchor.CentreLeft,
        };
        SpreadDisplay = new BmsPanelBeatmapStandalone.SpreadDisplay
        {
            Origin = Anchor.CentreLeft,
            Anchor = Anchor.CentreLeft,
            Selected = { BindTarget = selected },
        };

        var details = new FillFlowContainer
        {
            Direction = FillDirection.Horizontal,
            AutoSizeAxes = Axes.Both,
            Padding = new MarginPadding { Top = 2, Bottom = 2 },
        };

        if (includeSetMetadata)
        {
            details.Add(StatusPill = new BeatmapSetOnlineStatusPill
            {
                Animated = false,
                Origin = Anchor.BottomLeft,
                Anchor = Anchor.BottomLeft,
                TextSize = OsuFont.Style.Caption2.Size,
                Margin = new MarginPadding { Right = 4f },
            });
            details.Add(UpdateButton = new PanelUpdateBeatmapButton
            {
                Scale = new Vector2(0.8f),
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Margin = new MarginPadding { Right = 4f, Bottom = -1f },
            });
        }

        details.AddRange(
        [
            KeyCountText = new OsuSpriteText
            {
                Font = OsuFont.Style.Body.With(weight: FontWeight.SemiBold),
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Alpha = 0,
            },
            DifficultyText = new OsuSpriteText
            {
                Font = OsuFont.Style.Body.With(weight: FontWeight.SemiBold),
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Margin = new MarginPadding { Right = 3f },
            },
        ]);

        if (includeSetMetadata)
        {
            details.Add(AuthorText = new OsuSpriteText
            {
                Colour = colourProvider.Content2,
                Font = OsuFont.Style.Caption1.With(weight: FontWeight.SemiBold),
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
            });
        }
        else
            AuthorText = new OsuSpriteText();

        MainFill = new FillFlowContainer
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Direction = FillDirection.Vertical,
            Padding = new MarginPadding { Bottom = 4.8f },
            AutoSizeAxes = Axes.Both,
            Children =
            [
                TitleText = new OsuSpriteText
                {
                    Font = OsuFont.Style.Heading2.With(typeface: Typeface.TorusAlternate, weight: FontWeight.Bold),
                },
                ArtistText = new OsuSpriteText
                {
                    Font = OsuFont.Style.Caption1.With(weight: FontWeight.SemiBold),
                    Padding = new MarginPadding { Top = -2 },
                },
                details,
                new FillFlowContainer
                {
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(3),
                    AutoSizeAxes = Axes.Both,
                    Children =
                    [
                        StarRatingDisplay = new StarRatingDisplay(default, StarRatingDisplaySize.Small, animated: true)
                        {
                            Origin = Anchor.CentreLeft,
                            Anchor = Anchor.CentreLeft,
                            Scale = new Vector2(0.875f),
                        },
                        SpreadDisplay,
                    ],
                },
            ],
        };

        InternalChildren =
        [
            BeatmapBackground,
            new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Spacing = new Vector2(5),
                Margin = new MarginPadding { Left = 6.5f },
                Direction = FillDirection.Horizontal,
                Children =
                [
                    LocalRank,
                    MainFill,
                ],
            },
        ];
    }
}
