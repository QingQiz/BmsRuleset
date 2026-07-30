using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

namespace osu.Game.Rulesets.BmsRuleset.Settings.Components;

internal partial class StageWidthScalingCheckbox : SettingsItem<float>
{
    public override Bindable<float> Current
    {
        get => base.Current;
        set
        {
            ((StageWidthCheckboxControl)Control).GetStageWidth = () =>
                SettingSourceObject is BmsStageHud hud ? hud.GetCurrentStageWidth() : 0;
            base.Current = value;
        }
    }

    protected override Drawable CreateControl() => new StageWidthCheckboxControl();

    private partial class StageWidthCheckboxControl : OsuCheckbox, IHasCurrentValue<float>
    {
        private Bindable<float> reference = new BindableFloat();
        private bool updating;

        public System.Func<float>? GetStageWidth { private get; set; }

        Bindable<float> IHasCurrentValue<float>.Current
        {
            get => reference;
            set
            {
                reference.ValueChanged -= onReferenceChanged;
                reference = value;
                reference.ValueChanged += onReferenceChanged;
                updateEnabled(reference.Value > 0);
            }
        }

        public StageWidthCheckboxControl()
        {
            Current.BindValueChanged(onEnabledChanged);
        }

        private void onEnabledChanged(ValueChangedEvent<bool> state)
        {
            if (!updating)
                reference.Value = state.NewValue ? GetStageWidth?.Invoke() ?? 0 : 0;
        }

        private void onReferenceChanged(ValueChangedEvent<float> value) => updateEnabled(value.NewValue > 0);

        private void updateEnabled(bool value)
        {
            updating = true;
            Current.Value = value;
            updating = false;
        }
    }
}
