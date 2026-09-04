using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Screens.Select;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Course;

internal partial class BmsCourseDetailsArea : VisibilityContainer
{
    internal float TopPadding { get; init; }

    private readonly IBindable<BmsCourseDefinition?> selectedCourse;
    private OsuSpriteText tableText = null!;
    private OsuSpriteText titleText = null!;
    private OsuSpriteText summaryText = null!;
    private OsuSpriteText constraintText = null!;

    internal BmsCourseDetailsArea(IBindable<BmsCourseDefinition?> selectedCourse)
    {
        this.selectedCourse = selectedCourse;
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        X = -150;
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
                        Font = OsuFont.Style.Caption1.With(weight: FontWeight.SemiBold, italics: false),
                    }),
                    unShear(titleText = new OsuSpriteText
                    {
                        RelativeSizeAxes = Axes.X,
                        Font = OsuFont.Style.Title.With(italics: false),
                    }),
                    unShear(summaryText = new OsuSpriteText
                    {
                        RelativeSizeAxes = Axes.X,
                        Font = OsuFont.Style.Body.With(weight: FontWeight.SemiBold, italics: false),
                    }),
                    unShear(constraintText = new OsuSpriteText
                    {
                        RelativeSizeAxes = Axes.X,
                        Font = OsuFont.Style.Caption1.With(weight: FontWeight.SemiBold, italics: false),
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
            : BmsStrings.CourseTitleSummary(course.Stages.Count);
        constraintText.Text = course == null
            ? string.Empty
            : string.Join(" · ", course.Constraints.Select(formatConstraint));
    }

    private static string formatConstraint(string constraint) => constraint.ToLowerInvariant() switch
    {
        "grade" => "GRADE",
        "grade_mirror" => "GRADE MIRROR",
        "grade_random" => "GRADE RANDOM",
        "no_speed" => "NO SPEED",
        "no_good" => "NO GOOD",
        "no_great" => "NO GREAT",
        "gauge_lr2" => "GAUGE LR2",
        "gauge_5k" => "GAUGE 5K",
        "gauge_7k" => "GAUGE 7K",
        "gauge_9k" => "GAUGE 9K",
        "gauge_24k" => "GAUGE 24K",
        "ln" => "LN",
        "cn" => "CN",
        "hcn" => "HCN",
        _ => constraint,
    };

    private static Drawable unShear(Drawable drawable)
    {
        drawable.Shear = -OsuGame.SHEAR;
        return new ShearAligningWrapper(drawable);
    }
}