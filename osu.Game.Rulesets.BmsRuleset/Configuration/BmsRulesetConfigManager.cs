using osu.Game.Configuration;
using osu.Game.Rulesets.Configuration;

namespace osu.Game.Rulesets.BmsRuleset.Configuration;

public class BmsRulesetConfigManager(SettingsStore? settings, RulesetInfo ruleset, int? variant = null)
    : RulesetConfigManager<BmsRulesetSetting>(settings, ruleset, variant)
{
    protected override void InitialiseDefaults()
    {
        base.InitialiseDefaults();

        SetDefault(BmsRulesetSetting.LastImportPath, "C:\\");
        SetDefault(BmsRulesetSetting.ScrollSpeed, 8.0, 1.0, 60.0, 0.1);
    }
}

public enum BmsRulesetSetting
{
    LastImportPath,
    ScrollSpeed,
}
