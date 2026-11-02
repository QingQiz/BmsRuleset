using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Course;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Practice;

internal abstract partial class BmsPracticeResultScreen : BmsResultsScreen
{
    protected BmsCourseResultButton NextAttemptButton { get; private set; } = null!;

    [Resolved]
    private OsuColour colours { get; set; } = null!;

    protected BmsPracticeResultScreen()
        : base(null)
    {
        AllowWatchingReplay = false;
    }

    protected override Drawable[] CreateResultControls()
    {
        BottomPanel.Name = "Practice result controls";
        return
        [
            new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.Gray(0.2f) },
            new BmsCourseResultButton
            {
                Name = "Return to practice settings button",
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Text = BmsStrings.PracticeReturnToSettings,
                Icon = OsuIcon.LeftCircle,
                BackgroundColour = colours.Pink,
                HoverColour = colours.PinkDark,
                Action = OnReturnToSettings,
            },
            NextAttemptButton = new NextAttemptResultButton
            {
                Name = "Start next practice attempt button",
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                Text = BmsStrings.PracticeNextAttempt,
                Icon = OsuIcon.Play,
                BackgroundColour = colours.Green3,
                HoverColour = colours.Green4,
                Action = OnNextAttempt,
            },
        ];
    }

    protected abstract void OnReturnToSettings();

    protected abstract void OnNextAttempt();

    protected override void OnSelect() => OnNextAttempt();

    private partial class NextAttemptResultButton : BmsCourseResultButton
    {
        protected override void LoadComplete()
        {
            base.LoadComplete();
            Enabled.BindValueChanged(enabled => this.FadeTo(enabled.NewValue ? 1 : 0.4f, 100), true);
        }
    }
}
