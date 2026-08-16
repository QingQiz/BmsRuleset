using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Screens.Select;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

internal enum BmsCourseDetailTab
{
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.CourseDetails))]
    Details,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.CourseHistory))]
    History,
}

internal partial class BmsCourseFilterControl : VisibilityContainer
{
    internal BmsCourseFilterControl(Bindable<string> searchTerm)
    {
        Anchor = Anchor.TopRight;
        Origin = Anchor.TopRight;
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Shear = OsuGame.SHEAR;
        Margin = new MarginPadding { Top = -Panel.CORNER_RADIUS, Right = -40 };

        InternalChildren =
        [
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                CornerRadius = Panel.CORNER_RADIUS,
                Masking = true,
                Child = new BmsCourseWedgeBackground
                {
                    Anchor = Anchor.TopRight,
                    Scale = new Vector2(-1, 1),
                },
            },
            new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Padding = new MarginPadding { Top = Panel.CORNER_RADIUS + 5, Bottom = 7, Right = 40, Left = 2 },
                Child = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Shear = -OsuGame.SHEAR,
                    Child = new ShearedSearchTextBox
                    {
                        RelativeSizeAxes = Axes.X,
                        Current = searchTerm,
                        HoldFocus = true,
                        PlaceholderText = BmsStrings.CourseSearchPlaceholder,
                    },
                },
            },
        ];
    }

    protected override bool StartHidden => true;

    protected override void PopIn()
    {
        this.MoveToX(0, osu.Game.Screens.Select.SongSelect.ENTER_DURATION, Easing.OutQuint)
            .FadeIn(osu.Game.Screens.Select.SongSelect.ENTER_DURATION / 3, Easing.In);
    }

    protected override void PopOut()
    {
        this.MoveToX(150, osu.Game.Screens.Select.SongSelect.ENTER_DURATION, Easing.OutQuint)
            .FadeOut(osu.Game.Screens.Select.SongSelect.ENTER_DURATION / 3, Easing.In);
    }
}

internal partial class BmsCourseTitleWedge : VisibilityContainer
{
    internal float TopPadding { get; init; }

    private readonly IBindable<BmsCourseDefinition?> selectedCourse;
    private OsuSpriteText tableText = null!;
    private OsuSpriteText titleText = null!;
    private OsuSpriteText summaryText = null!;

    internal BmsCourseTitleWedge(IBindable<BmsCourseDefinition?> selectedCourse)
    {
        this.selectedCourse = selectedCourse;
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
    }

    protected override bool StartHidden => true;

    [BackgroundDependencyLoader]
    private void load()
    {
        Masking = true;
        CornerRadius = Panel.CORNER_RADIUS;

        InternalChildren =
        [
            new BmsCourseWedgeBackground(),
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Padding = new MarginPadding
                {
                    Top = osu.Game.Screens.Select.SongSelect.WEDGE_CONTENT_MARGIN + TopPadding,
                    Left = osu.Game.Screens.Select.SongSelect.WEDGE_CONTENT_MARGIN,
                    Right = 36,
                    Bottom = 18,
                },
                Spacing = new Vector2(0, 4),
                Children =
                [
                    unShear(tableText = new OsuSpriteText
                    {
                        Font = OsuFont.Style.Caption1.With(weight: FontWeight.SemiBold),
                    }),
                    unShear(titleText = new OsuSpriteText
                    {
                        RelativeSizeAxes = Axes.X,
                        Font = OsuFont.Style.Title,
                    }),
                    unShear(summaryText = new OsuSpriteText
                    {
                        RelativeSizeAxes = Axes.X,
                        Font = OsuFont.Style.Body.With(weight: FontWeight.SemiBold),
                    }),
                ],
            },
        ];

        selectedCourse.BindValueChanged(_ => updateDisplay(), true);
    }

    protected override void PopIn()
    {
        this.MoveToX(0, osu.Game.Screens.Select.SongSelect.ENTER_DURATION, Easing.OutQuint)
            .FadeIn(osu.Game.Screens.Select.SongSelect.ENTER_DURATION / 3, Easing.In);
    }

    protected override void PopOut()
    {
        this.MoveToX(-150, osu.Game.Screens.Select.SongSelect.ENTER_DURATION, Easing.OutQuint)
            .FadeOut(osu.Game.Screens.Select.SongSelect.ENTER_DURATION / 3, Easing.In);
    }

    private void updateDisplay()
    {
        var course = selectedCourse.Value;

        tableText.Text = course?.TableName ?? string.Empty;
        titleText.Text = course?.Name ?? BmsStrings.Courses;
        summaryText.Text = course == null
            ? BmsStrings.SelectCourseForDetails
            : BmsStrings.CourseTitleSummary(course.Stages.Count, course.Gauge);
    }

    private static Drawable unShear(Drawable drawable)
    {
        drawable.Shear = -OsuGame.SHEAR;
        return new ShearAligningWrapper(drawable);
    }
}

internal partial class BmsCourseDetailsArea : VisibilityContainer
{
    private readonly IBindable<BmsCourseDefinition?> selectedCourse;
    private BeatmapDetailsArea.WedgeSelector<BmsCourseDetailTab> tabs = null!;
    private FillFlowContainer content = null!;

    internal BmsCourseDetailsArea(IBindable<BmsCourseDefinition?> selectedCourse)
    {
        this.selectedCourse = selectedCourse;
        RelativeSizeAxes = Axes.X;
    }

    protected override bool StartHidden => true;

    [BackgroundDependencyLoader]
    private void load()
    {
        const float header_height = 35;

        InternalChildren =
        [
            new ShearAligningWrapper(new Container
            {
                Shear = -OsuGame.SHEAR,
                RelativeSizeAxes = Axes.X,
                Height = header_height,
                Padding = new MarginPadding { Left = osu.Game.Screens.Select.SongSelect.WEDGE_CONTENT_MARGIN, Right = 5 },
                Child = tabs = new BeatmapDetailsArea.WedgeSelector<BmsCourseDetailTab>(20)
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Width = 200,
                    Height = 22,
                    Margin = new MarginPadding { Top = 2 },
                    IsSwitchable = true,
                },
            }),
            new ShearAligningWrapper(new Container
            {
                Shear = -OsuGame.SHEAR,
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Top = header_height },
                Child = new OsuScrollContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    ScrollbarVisible = false,
                    Child = content = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Width = 0.9f,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 4),
                        Padding = new MarginPadding { Top = 4, Bottom = 80 },
                    },
                },
            })
            {
                Depth = 1,
            },
        ];

        tabs.Current.BindValueChanged(_ => rebuild());
        selectedCourse.BindValueChanged(_ => rebuild(), true);
    }

    protected override void PopIn()
    {
        this.MoveToX(0, osu.Game.Screens.Select.SongSelect.ENTER_DURATION, Easing.OutQuint)
            .FadeIn(osu.Game.Screens.Select.SongSelect.ENTER_DURATION / 3, Easing.In);
    }

    protected override void PopOut()
    {
        this.MoveToX(-150, osu.Game.Screens.Select.SongSelect.ENTER_DURATION, Easing.OutQuint)
            .FadeOut(osu.Game.Screens.Select.SongSelect.ENTER_DURATION / 3, Easing.In);
    }

    private void rebuild()
    {
        content.Clear();

        if (tabs.Current.Value == BmsCourseDetailTab.History)
        {
            content.Add(createTextCard(BmsStrings.NoCourseHistory, 64, Color4.White.Opacity(0.65f)));
            return;
        }

        var course = selectedCourse.Value;
        if (course == null)
        {
            content.Add(createTextCard(BmsStrings.SelectCourseForDetails, 64, Color4.White.Opacity(0.65f)));
            return;
        }

        content.Add(createRulesCard(course));

        for (var i = 0; i < course.Stages.Count; i++)
            content.Add(createStageCard(i + 1, course.Stages[i]));
    }

    private static Drawable createRulesCard(BmsCourseDefinition course) => createCard(78, new FillFlowContainer
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Direction = FillDirection.Vertical,
        Spacing = new Vector2(0, 4),
        Children =
        [
            new OsuSpriteText
            {
                Font = OsuFont.Style.Heading2.With(weight: FontWeight.SemiBold),
                Text = BmsStrings.CourseRules,
            },
            new OsuSpriteText
            {
                Font = OsuFont.Style.Body,
                Text = BmsStrings.CourseGauge(course.Gauge),
            },
            new OsuSpriteText
            {
                RelativeSizeAxes = Axes.X,
                Font = OsuFont.Style.Body,
                Text = course.Constraints.Count > 0
                    ? BmsStrings.CourseConstraints(string.Join(" · ", course.Constraints))
                    : BmsStrings.CourseConstraintsNone,
            },
        ],
    });

    private static Drawable createStageCard(int index, BmsCourseStage stage)
    {
        return createCard(58, new GridContainer
        {
            RelativeSizeAxes = Axes.X,
            Height = 34,
            ColumnDimensions =
            [
                new Dimension(GridSizeMode.Absolute, 42),
                new Dimension(),
                new Dimension(GridSizeMode.AutoSize),
            ],
            Content = new[]
            {
                new Drawable[]
                {
                    new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Font = OsuFont.Style.Heading2.With(weight: FontWeight.Bold),
                        Text = BmsStrings.CourseStageNumber(index),
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        RelativeSizeAxes = Axes.X,
                        Font = OsuFont.Style.Body,
                        Text = stage.Title,
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Font = OsuFont.Style.Body.With(weight: FontWeight.SemiBold),
                        Colour = stage.IsAvailable ? Color4.White.Opacity(0.75f) : Color4.OrangeRed,
                        Text = stage.IsAvailable ? stage.Difficulty : BmsStrings.CourseStageMissing,
                    },
                },
            },
        });
    }

    private static Drawable createTextCard(LocalisableString text, float height, Color4 colour) => createCard(height, new OsuSpriteText
    {
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        RelativeSizeAxes = Axes.X,
        Font = OsuFont.Style.Body,
        Colour = colour,
        Text = text,
    });

    private static Drawable createCard(float height, Drawable child) => new ShearAligningWrapper(new Container
    {
        RelativeSizeAxes = Axes.X,
        Height = height,
        CornerRadius = Panel.CORNER_RADIUS,
        Masking = true,
        Children =
        [
            new BmsCourseWedgeBackground(),
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Shear = -OsuGame.SHEAR,
                Padding = new MarginPadding
                {
                    Left = osu.Game.Screens.Select.SongSelect.WEDGE_CONTENT_MARGIN,
                    Right = 35,
                    Vertical = 12,
                },
                Child = child,
            },
        ],
    });
}

internal partial class BmsCourseNoResultsPlaceholder : VisibilityContainer
{
    internal LocalisableString Message
    {
        get => message.Text;
        set => message.Text = value;
    }

    private OsuSpriteText message = null!;

    protected override bool StartHidden => true;

    [BackgroundDependencyLoader]
    private void load()
    {
        RelativeSizeAxes = Axes.Both;
        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;

        InternalChild = new FillFlowContainer
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Width = 360,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Children =
            [
                new Container
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Margin = new MarginPadding(10),
                    Size = new Vector2(50),
                    Child = new GhostIcon
                    {
                        RelativeSizeAxes = Axes.Both,
                    },
                },
                message = new OsuSpriteText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    RelativeSizeAxes = Axes.X,
                    Font = OsuFont.Style.Heading1,
                    Text = BmsStrings.NoCoursesAvailable,
                },
            ],
        };
    }

    protected override void PopIn() => this.FadeIn(600, Easing.OutQuint);

    protected override void PopOut() => this.FadeOut(200, Easing.OutQuint);
}

internal sealed partial class BmsCourseWedgeBackground : InputBlockingContainer
{
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
                Width = 0.7f,
                Colour = colourProvider.Background5.Opacity(0.9f),
            },
            new Box
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                RelativeSizeAxes = Axes.Both,
                Width = 0.3f,
                Colour = ColourInfo.GradientHorizontal(colourProvider.Background5.Opacity(0.9f), colourProvider.Background5.Opacity(0.6f)),
            },
        ];
    }
}
