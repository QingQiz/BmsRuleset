using osu.Game.Configuration;
using osu.Game.Rulesets.Configuration;

namespace osu.Game.Rulesets.BmsRuleset.Configuration;

public class BmsRulesetConfigManager(SettingsStore? settings, RulesetInfo ruleset, int? variant = null)
    : RulesetConfigManager<BmsRulesetSetting>(settings, ruleset, variant)
{

    public const double MAX_SCROLL_SPEED = 100.0;
    public const double DEFAULT_SCROLL_SPEED = 8.0;

    protected override void InitialiseDefaults()
    {
        base.InitialiseDefaults();

        SetDefault(BmsRulesetSetting.LastImportPath, "C:\\");
        SetDefault(BmsRulesetSetting.ScrollSpeed, DEFAULT_SCROLL_SPEED, 1.0, MAX_SCROLL_SPEED, 0.1);
    }
}

public enum BmsRulesetSetting
{
    LastImportPath,
    ScrollSpeed,
}
