using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Course;
using osu.Game.Screens.Footer;

namespace osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Components;

internal partial class BmsPracticeFooterButton : ScreenFooterButton
{
    private readonly BmsCourseSongSelectController controller;

    [Resolved]
    private IBindable<RulesetInfo> ruleset { get; set; } = null!;

    [Resolved]
    private IBindable<WorkingBeatmap> beatmap { get; set; } = null!;

    internal BmsPracticeFooterButton(BmsCourseSongSelectController controller, Action action)
    {
        this.controller = controller;
        Action = action;
        Text = BmsStrings.Practice;
        Icon = FontAwesome.Solid.Bullseye;
        Width = 0;
        Alpha = 0;
        Enabled.Value = false;
    }

    [BackgroundDependencyLoader]
    private void load(OsuColour colour)
    {
        AccentColour = colour.BlueLight;
        ruleset.BindValueChanged(rulesetChanged, true);
        beatmap.BindValueChanged(beatmapChanged, true);
        controller.CourseModeChanged += courseModeChanged;
    }

    private void rulesetChanged(ValueChangedEvent<RulesetInfo> change) => updateAvailability();

    private void beatmapChanged(ValueChangedEvent<WorkingBeatmap> change) => updateAvailability();

    private void courseModeChanged(bool courseMode) => updateAvailability();

    private void updateAvailability()
    {
        var visible = ruleset.Value.ShortName == Constant.SHORT_NAME && !controller.IsCourseMode
                      && !beatmap.IsDefault && BmsPracticeSession.SupportsBeatmap(beatmap.Value.BeatmapInfo);
        Enabled.Value = visible;
        this.ResizeWidthTo(visible ? BUTTON_WIDTH : 0, 180, Easing.OutQuint);
        this.FadeTo(visible ? 1 : 0, 120, Easing.OutQuint);
    }

    protected override void Dispose(bool isDisposing)
    {
        ruleset.ValueChanged -= rulesetChanged;
        beatmap.ValueChanged -= beatmapChanged;
        controller.CourseModeChanged -= courseModeChanged;
        base.Dispose(isDisposing);
    }
}
