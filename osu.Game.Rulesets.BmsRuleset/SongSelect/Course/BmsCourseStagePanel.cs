using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Containers;
using osu.Game.Overlays;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Screens.Select;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect.Course;

internal partial class BmsCourseStagePanel : BmsBeatmapPanel
{
    private BmsCourseStage? stage;
    private Container backgroundContainer = null!;
    private Box missingBackground = null!;
    private Color4 availableIconColour;
    private Color4 defaultAccentColour;

    internal BeatmapInfo? ResolvedBeatmap { get; private set; }

    public BmsCourseStagePanel()
    {
        PanelXOffset = 40;
    }

    [BackgroundDependencyLoader]
    private void load(OverlayColourProvider colourProvider)
    {
        AccentColour = defaultAccentColour = colourProvider.Highlight1;
        availableIconColour = colourProvider.Background5;

        var difficultyIcon = new ConstrainedIconContainer
        {
            Icon = new SpriteIcon { Icon = FontAwesome.Solid.ExclamationTriangle },
            Size = new Vector2(12),
            Margin = new MarginPadding { Left = 4, Right = 3 },
            Colour = Color4.OrangeRed,
            Alpha = 1,
            AlwaysPresent = true,
        };

        backgroundContainer = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children =
            [
                missingBackground = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = colourProvider.Highlight1,
                    Alpha = 0,
                },
            ],
        };

        InitialisePanel(PanelBeatmapStandalone.HEIGHT, false, colourProvider, difficultyIcon, backgroundContainer);
    }

    protected override void PrepareBeatmap()
    {
        // Carousel reuse can call PrepareForUse directly when replacing the stage item.
        // Cancel the previous background request before publishing the new stage.
        var model = (BmsGroupedCourseStage)Item!.Model;
        stage = model.Stage;
        ResolvedBeatmap = model.Beatmap;
        CurrentBeatmap = ResolvedBeatmap;
        Name = $"Course stage {model.StageIndex + 1} song panel";

        var metadata = ResolvedBeatmap?.BeatmapSet?.Metadata ?? ResolvedBeatmap?.Metadata;
        TitleText.Text = metadata == null
            ? stage.Title
            : new RomanisableString(metadata.TitleUnicode, metadata.Title);
        ArtistText.Text = metadata == null
            ? stage.Artist ?? string.Empty
            : new RomanisableString(metadata.ArtistUnicode, metadata.Artist);
        DifficultyText.Text = ResolvedBeatmap?.DifficultyName ?? (stage.IsAvailable ? stage.Difficulty : BmsStrings.CourseStageMissing);
        DifficultyText.Colour = stage.IsAvailable ? Color4.White : Color4.OrangeRed;

        DifficultyIcon.Icon = ResolvedBeatmap?.Ruleset.CreateInstance().CreateIcon()
                              ?? new SpriteIcon { Icon = FontAwesome.Solid.ExclamationTriangle };
        DifficultyIcon.Colour = ResolvedBeatmap != null ? availableIconColour : Color4.OrangeRed;
        DifficultyIcon.Alpha = ResolvedBeatmap != null ? 0 : 1;

        LocalRank.Beatmap = ResolvedBeatmap;
        if (ResolvedBeatmap != null)
            LocalRank.RefreshBmsScores();
        LocalRank.Alpha = ResolvedBeatmap != null ? 1 : 0;
        LocalRank.AttachLamp(backgroundContainer);
        missingBackground.Alpha = ResolvedBeatmap != null ? 0 : 1;
        StarRatingDisplay.Alpha = ResolvedBeatmap != null ? 1 : 0;
        SpreadDisplayControl.Alpha = ResolvedBeatmap != null ? 1 : 0;
        SpreadDisplayControl.Beatmap.Value = ResolvedBeatmap;

        if (ResolvedBeatmap != null)
            ScheduleBackgroundRetrieval(ResolvedBeatmap);
    }

    protected override void ResetBeatmapState()
    {
        base.ResetBeatmapState();
        missingBackground.Alpha = 0;
        LocalRank.Alpha = 0;
        StarRatingDisplay.Alpha = 1;
        SpreadDisplayControl.Alpha = 1;
        AccentColour = defaultAccentColour;
        ResolvedBeatmap = null;
        stage = null;
    }

    public override MenuItem[] ContextMenuItems => [];

    protected override bool OnClick(ClickEvent e) => true;

    internal static BeatmapInfo? QueryBeatmap(BeatmapManager beatmaps, string hash) => hash.Length switch
    {
        32 => beatmaps.QueryBeatmap(info => info.MD5Hash == hash),
        64 => beatmaps.QueryBeatmap(info => info.Hash == hash),
        _ => null,
    };
}
