using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Course;

internal partial class BmsCourseNoResultsPlaceholder : VisibilityContainer
{
    internal LocalisableString Message
    {
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