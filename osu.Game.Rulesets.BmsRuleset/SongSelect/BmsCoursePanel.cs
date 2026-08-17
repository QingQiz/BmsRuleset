using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.Leaderboards;
using osu.Game.Overlays;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Scoring;
using osu.Game.Screens.Select;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

internal partial class BmsCourseTablePanel : PanelGroup
{
    internal BmsCourseCarousel? CourseCarousel { private get; set; }

    protected override bool OnClick(ClickEvent e)
    {
        if (Item != null)
            CourseCarousel?.Activate(Item);

        return true;
    }
}

internal partial class BmsCoursePanel : Panel
{
    private const float leaf_panel_active_x_offset = 25;

    internal BmsCourseCarousel? CourseCarousel { private get; set; }

    private OsuSpriteText titleText = null!;
    private OsuSpriteText gaugeText = null!;
    private UpdateableRank courseRank = null!;
    private BmsLampDisplay courseLamp = null!;
    private BmsCourseDefinition? currentCourse;
    private BmsCourseResultStore? resultStore;

    public BmsCoursePanel()
    {
        PanelXOffset = 20;
    }

    [BackgroundDependencyLoader]
    private void load(OverlayColourProvider colourProvider)
    {
        Height = PanelBeatmapStandalone.HEIGHT;
        AccentColour = colourProvider.Highlight1;

        Icon = new SpriteIcon
        {
            Icon = FontAwesome.Solid.Trophy,
            Size = new Vector2(12),
            Margin = new MarginPadding { Left = 4, Right = 3 },
            Colour = colourProvider.Background5,
        };

        Background = courseLamp = new BmsLampDisplay(BmsLamp.NoPlay)
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
        };

        Content.Children =
        [
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = ColourInfo.GradientHorizontal(colourProvider.Background4, colourProvider.Background5),
            },
            new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Left = 14, Right = 30, Vertical = 8 },
                ColumnDimensions =
                [
                    new Dimension(GridSizeMode.AutoSize),
                    new Dimension(),
                ],
                Content = new[]
                {
                    new Drawable[]
                    {
                        courseRank = new UpdateableRank(animate: false)
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Size = new Vector2(40, 20),
                            Scale = new Vector2(0.8f),
                            Alpha = 0,
                            Margin = new MarginPadding { Right = 5 },
                        },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            Direction = FillDirection.Vertical,
                            Children =
                            [
                                titleText = new OsuSpriteText
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Font = OsuFont.Style.Heading2.With(typeface: Typeface.Torus, weight: FontWeight.Bold, italics: false),
                                },
                                gaugeText = new OsuSpriteText
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Font = OsuFont.Style.Caption1.With(weight: FontWeight.SemiBold, italics: false),
                                    Colour = colourProvider.Content2,
                                },
                            ],
                        },
                    },
                },
            },
        ];
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        // Panel only recognises osu!'s built-in beatmap types as leaf items, so external course panels must restore the same indentation explicitly.
        Expanded.BindValueChanged(_ => updateLeafPanelOffset());
        Selected.BindValueChanged(_ => updateLeafPanelOffset());
        KeyboardSelected.BindValueChanged(_ => updateLeafPanelOffset());
        resultStore = BmsRulesetRuntime.CourseResults;
        if (resultStore != null)
            resultStore.Changed += courseResultChanged;
        updateResult();
        updateLeafPanelOffset(false);
    }

    protected override void PrepareForUse()
    {
        base.PrepareForUse();

        var course = ((BmsGroupedCourse)Item!.Model).Course;
        currentCourse = course;

        titleText.Text = course.Name;
        gaugeText.Text = BmsStrings.CourseGauge(course.Gauge);
        updateResult();
        updateLeafPanelOffset(false);
    }

    protected override void FreeAfterUse()
    {
        currentCourse = null;
        base.FreeAfterUse();
    }

    protected override void Dispose(bool isDisposing)
    {
        if (resultStore != null)
            resultStore.Changed -= courseResultChanged;

        base.Dispose(isDisposing);
    }

    protected override bool OnClick(ClickEvent e)
    {
        if (Item != null)
            CourseCarousel?.Activate(Item);

        return true;
    }

    public override MenuItem[] ContextMenuItems => [];

    private void courseResultChanged(string courseId)
    {
        if (currentCourse?.Id == courseId)
            Scheduler.Add(updateResult);
    }

    private void updateResult()
    {
        if (currentCourse == null)
            return;

        courseLamp.Lamp = resultStore?.GetLamp(currentCourse.Id) ?? BmsLamp.NoPlay;
        ScoreRank? rank = resultStore?.GetRank(currentCourse.Id);
        courseRank.Rank = rank;
        courseRank.Alpha = rank.HasValue ? 1 : 0;
    }

    private void updateLeafPanelOffset(bool animated = true)
    {
        var x = PanelXOffset + CORNER_RADIUS;

        if (!Expanded.Value && !Selected.Value)
            x += leaf_panel_active_x_offset * 2;

        if (!KeyboardSelected.Value)
            x += leaf_panel_active_x_offset;

        TopLevelContent.MoveToX(x, animated ? DURATION : 0, Easing.OutQuint);
    }
}
