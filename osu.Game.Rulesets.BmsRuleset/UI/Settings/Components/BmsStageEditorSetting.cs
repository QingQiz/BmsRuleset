using System.Collections.Generic;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Configuration;

namespace osu.Game.Rulesets.BmsRuleset.UI.Settings.Components;

internal partial class BmsStageEditorSetting : SettingsItem<Dictionary<int, BmsStageStyle>>
{
    // SettingSourceExtensions creates custom controls via Activator.CreateInstance.
    public BmsStageEditorSetting()
    {
        ShowsDefaultIndicator = false;
        Padding = default;
    }

    // The toolbox already titles the selected component; an empty SettingsItem label still takes space.
    public override LocalisableString LabelText { get => default; set { } }

    protected override Drawable CreateControl() => new BmsStageEditor();

    protected override void LoadComplete()
    {
        base.LoadComplete();

        // The child settings already reserve space for their own reset buttons.
        ((Container)FlowContent.Parent!).Padding = default;
    }

    public override Bindable<Dictionary<int, BmsStageStyle>> Current
    {
        get => base.Current;
        set
        {
            base.Current = value;
            ((BmsStageEditor)Control).SetStage((BmsStageHud)SettingSourceObject);
        }
    }
}
