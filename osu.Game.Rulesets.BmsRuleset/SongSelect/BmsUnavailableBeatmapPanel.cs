using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Carousel;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.SongSelect.Course;
using osu.Game.Screens.Select;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

/// <summary>
/// Displays a difficulty-table entry which is known to a table or course but is not present in the local beatmap library.
/// </summary>
internal partial class BmsUnavailableBeatmapPanel : Panel
{
    private TruncatingSpriteText titleText = null!;
    private TruncatingSpriteText artistText = null!;
    private OsuSpriteText difficultyText = null!;
    private OsuSpriteText statusText = null!;
    private ConstrainedIconContainer warningIcon = null!;
    private Box background = null!;

    public BmsUnavailableBeatmapPanel()
    {
        PanelXOffset = 40;
    }

    [BackgroundDependencyLoader]
    private void load(OverlayColourProvider colourProvider)
    {
        Height = CarouselItem.DEFAULT_HEIGHT;

        var contentColour = colourProvider.Content1;
        warningIcon = new ConstrainedIconContainer
        {
            Icon = new SpriteIcon { Icon = FontAwesome.Solid.ExclamationTriangle },
            Size = new Vector2(12),
            Margin = new MarginPadding { Left = 4, Right = 3 },
            Colour = Color4.Red,
            AlwaysPresent = true,
        };

        Icon = warningIcon;
        Background = background = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Colour = ColourInfo.GradientHorizontal(colourProvider.Background4, colourProvider.Background5),
        };

        Content.Children =
        [
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Direction = FillDirection.Vertical,
                Padding = new MarginPadding { Left = 6.5f, Right = 20 },
                Children =
                [
                    titleText = new TruncatingSpriteText
                    {
                        RelativeSizeAxes = Axes.X,
                        Font = OsuFont.Style.Heading2.With(typeface: Typeface.TorusAlternate, weight: FontWeight.Bold),
                    },
                    artistText = new TruncatingSpriteText
                    {
                        RelativeSizeAxes = Axes.X,
                        Colour = colourProvider.Content2,
                        Font = OsuFont.Style.Caption1.With(weight: FontWeight.SemiBold),
                    },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(5),
                        Children =
                        [
                            difficultyText = new OsuSpriteText
                            {
                                Font = OsuFont.Style.Body.With(weight: FontWeight.SemiBold),
                            },
                            statusText = new OsuSpriteText
                            {
                                Colour = contentColour,
                                Font = OsuFont.Style.Caption1.With(weight: FontWeight.SemiBold),
                            },
                        ],
                    },
                ],
            },
        ];
    }

    protected override void PrepareForUse()
    {
        base.PrepareForUse();

        Height = Item?.DrawHeight ?? CarouselItem.DEFAULT_HEIGHT;

        var (title, artist, difficulty, status, unavailable) = Item!.Model switch
        {
            GroupedBeatmap grouped => createContent(grouped.Beatmap),
            BmsGroupedCourseStage { Beatmap: null } grouped => createContent(grouped.Stage),
            _ => throw new InvalidOperationException($"Unsupported unavailable beatmap model {Item.Model.GetType().Name}."),
        };

        titleText.Text = title;
        artistText.Text = Height > CarouselItem.DEFAULT_HEIGHT ? artist : string.Empty;
        difficultyText.Text = difficulty;
        statusText.Text = status;

        var warningColour = unavailable is { } resolved ? GetWarningColour(resolved.Entry) : Color4.Red;
        warningIcon.Colour = warningColour;
        warningIcon.Icon.Colour = warningColour;
        var hasDownload = warningColour == Color4.Orange;
        background.Colour = hasDownload
            ? ColourInfo.GradientHorizontal(Color4.Orange.Opacity(0.25f), Color4.Transparent)
            : ColourInfo.GradientHorizontal(Color4.Red.Opacity(0.25f), Color4.Transparent);
    }

    protected override void FreeAfterUse()
    {
        titleText.Text = string.Empty;
        artistText.Text = string.Empty;
        difficultyText.Text = string.Empty;
        statusText.Text = string.Empty;
        base.FreeAfterUse();
    }

    public override MenuItem[] ContextMenuItems => [];

    protected override bool OnClick(ClickEvent e) => Item?.Model is BmsGroupedCourseStage || base.OnClick(e);

    internal static Color4 GetWarningColour(TableEntry entry) =>
        UnavailableTableBeatmapFactory.GetDownloadUrl(entry) != null ? Color4.Orange : Color4.Red;

    private static UnavailablePanelContent createContent(BeatmapInfo beatmap)
    {
        var metadata = beatmap.Metadata;
        return new UnavailablePanelContent(
            string.IsNullOrWhiteSpace(metadata.Title) ? beatmap.MD5Hash : metadata.Title,
            metadata.Artist,
            beatmap.DifficultyName,
            BmsStrings.DifficultyTableBeatmapNotImported,
            UnavailableTableBeatmapFactory.Resolve(beatmap, BmsRulesetRuntime.DifficultyTableStore));
    }

    private static UnavailablePanelContent createContent(BmsCourseStage stage) => new(
        stage.Title,
        stage.Artist ?? string.Empty,
        stage.Difficulty,
        BmsStrings.CourseStageMissing,
        UnavailableTableBeatmapFactory.Resolve(stage.BeatmapHash ?? string.Empty, BmsRulesetRuntime.DifficultyTableStore));

    private sealed record UnavailablePanelContent(
        string Title,
        string Artist,
        string Difficulty,
        LocalisableString Status,
        UnavailableTableEntry? Unavailable);
}
