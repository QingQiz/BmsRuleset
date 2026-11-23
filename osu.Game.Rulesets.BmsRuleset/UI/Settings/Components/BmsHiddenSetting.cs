using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Overlays.Settings;

namespace osu.Game.Rulesets.BmsRuleset.UI.Settings.Components;

// SettingSource also drives serialisation, including values that should not have an editor control.
internal partial class BmsHiddenSetting<T> : SettingsItem<T>
{
    public BmsHiddenSetting()
    {
        CanBeShown.Value = false;
        MatchingFilter = false;
    }

    protected override Drawable CreateControl() => new ValueControl();

    private partial class ValueControl : CompositeDrawable, IHasCurrentValue<T>
    {
        private readonly BindableWithCurrent<T> current = new();

        public Bindable<T> Current
        {
            get => current.Current;
            set => current.Current = value;
        }
    }
}
