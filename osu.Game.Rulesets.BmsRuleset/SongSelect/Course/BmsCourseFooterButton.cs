using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Screens.Footer;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect.Course;

internal partial class BmsCourseFooterButton : ScreenFooterButton
{
    private readonly BmsCourseSongSelectController controller;

    [Resolved]
    private IBindable<RulesetInfo> ruleset { get; set; } = null!;

    internal BmsCourseFooterButton(BmsCourseSongSelectController controller)
    {
        this.controller = controller;
        Action = controller.ToggleMode;
        Icon = FontAwesome.Solid.Trophy;
    }

    [BackgroundDependencyLoader]
    private void load(OsuColour colour)
    {
        AccentColour = colour.Orange1;
        OverlayState.BindTo(controller.State);
        controller.CourseModeChanged += courseModeChanged;
        ruleset.BindValueChanged(rulesetChanged, true);
    }

    protected override void Dispose(bool isDisposing)
    {
        controller.CourseModeChanged -= courseModeChanged;
        ruleset.ValueChanged -= rulesetChanged;
        OverlayState.UnbindFrom(controller.State);

        base.Dispose(isDisposing);
    }

    private void courseModeChanged(bool courseMode)
    {
        Text = courseMode ? BmsStrings.SongSelect : BmsStrings.Courses;
    }

    private void rulesetChanged(ValueChangedEvent<RulesetInfo> change)
    {
        var isBms = change.NewValue.ShortName == Constant.SHORT_NAME;

        Enabled.Value = isBms;
        this.ResizeWidthTo(isBms ? BUTTON_WIDTH : 0, 180, Easing.OutQuint);
        this.FadeTo(isBms ? 1 : 0, 120, Easing.OutQuint);

        courseModeChanged(controller.IsCourseMode);
    }
}
