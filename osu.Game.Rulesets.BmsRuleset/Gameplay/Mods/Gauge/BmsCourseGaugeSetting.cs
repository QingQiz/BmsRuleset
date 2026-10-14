using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Overlays.Settings;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;

// SettingSource is required for score persistence, but course carry-over is not a player-editable setting.
internal partial class BmsCourseGaugeSetting<T> : SettingsItem<T>
{
    public BmsCourseGaugeSetting()
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
