using System.Linq;
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
using osu.Game.Overlays;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Screens.Select;
using osuTK;
using osuTK.Graphics;

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
    private OsuSpriteText stageText = null!;

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

        Background = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Colour = colourProvider.Highlight1,
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
                    new Dimension(),
                    new Dimension(GridSizeMode.AutoSize),
                ],
                Content = new[]
                {
                    new Drawable[]
                    {
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            Direction = FillDirection.Vertical,
                            Children =
                            [
                                titleText = new OsuSpriteText
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Font = OsuFont.Style.Heading2.With(typeface: Typeface.TorusAlternate, weight: FontWeight.Bold),
                                },
                                gaugeText = new OsuSpriteText
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Font = OsuFont.Style.Caption1.With(weight: FontWeight.SemiBold),
                                    Colour = colourProvider.Content2,
                                },
                            ],
                        },
                        stageText = new OsuSpriteText
                        {
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.CentreRight,
                            Font = OsuFont.Style.Body.With(weight: FontWeight.SemiBold),
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
        updateLeafPanelOffset(false);
    }

    protected override void PrepareForUse()
    {
        base.PrepareForUse();

        var course = ((BmsGroupedCourse)Item!.Model).Course;
        var missingCount = course.Stages.Count(stage => !stage.IsAvailable);

        titleText.Text = course.Name;
        gaugeText.Text = BmsStrings.CourseGauge(course.Gauge);
        stageText.Text = missingCount == 0
            ? BmsStrings.CourseStageCount(course.Stages.Count)
            : BmsStrings.CourseMissingStageCount(missingCount);
        stageText.Colour = missingCount == 0 ? Color4.White : Color4.OrangeRed;
        updateLeafPanelOffset(false);
    }

    protected override bool OnClick(ClickEvent e)
    {
        if (Item != null)
            CourseCarousel?.Activate(Item);

        return true;
    }

    public override MenuItem[] ContextMenuItems => [];

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
