using osu.Game.Configuration;
using osu.Game.Rulesets.Configuration;

namespace osu.Game.Rulesets.BmsRuleset.Configuration;

public class BmsRulesetConfigManager(SettingsStore? settings, RulesetInfo ruleset, int? variant = null)
    : RulesetConfigManager<BmsRulesetSetting>(settings, ruleset, variant)
{

    public const double MAX_SCROLL_SPEED = 50.0;
    public const double DEFAULT_SCROLL_SPEED = 8.0;
    public const double MIN_VISUAL_OFFSET = -500;
    public const double MAX_VISUAL_OFFSET = 500;

    protected override void InitialiseDefaults()
    {
        base.InitialiseDefaults();

        SetDefault(BmsRulesetSetting.LastImportPath, "C:\\");
        SetDefault(BmsRulesetSetting.ScrollSpeed, DEFAULT_SCROLL_SPEED, 1.0, MAX_SCROLL_SPEED, 0.1);
        SetDefault(BmsRulesetSetting.ReferenceBpmMode, BmsReferenceBpmMode.MainBpm);
        SetDefault(BmsRulesetSetting.BgaDim, 0.7, 0, 1, 0.01);
        SetDefault(BmsRulesetSetting.VisualOffset, 0.0, MIN_VISUAL_OFFSET, MAX_VISUAL_OFFSET, 1.0);
        SetDefault(BmsRulesetSetting.AutomaticallyAdjustVisualOffset, false);
        SetDefault(BmsRulesetSetting.UseDedicatedPreviewAudio, true);
        SetDefault(BmsRulesetSetting.UnlockFrameRateLimit, false);

        SetDefault(BmsRulesetSetting.ShowBms5K, true);
        SetDefault(BmsRulesetSetting.ShowBme7K, true);
        SetDefault(BmsRulesetSetting.ShowPms9K, true);
        SetDefault(BmsRulesetSetting.ShowBms5KDouble, true);
        SetDefault(BmsRulesetSetting.ShowBme7KDouble, true);
        SetDefault(BmsRulesetSetting.ShowPms9KDouble, true);
        SetDefault(BmsRulesetSetting.DifficultyTableSources, string.Empty);
        SetDefault(BmsRulesetSetting.DifficultyTableHistory, string.Empty);
        SetDefault(BmsRulesetSetting.DifficultyTableSubdividedTables, string.Empty);
        SetDefault(BmsRulesetSetting.CourseResults, string.Empty);
    }
}

public enum BmsRulesetSetting
{
    LastImportPath,
    ScrollSpeed,
    ReferenceBpmMode,
    BgaDim,
    VisualOffset,
    AutomaticallyAdjustVisualOffset,
    ShowBms5K,
    ShowBme7K,
    ShowPms9K,
    ShowBms5KDouble,
    ShowBme7KDouble,
    ShowPms9KDouble,
    DifficultyTableSources,
    DifficultyTableHistory,
    DifficultyTableSubdividedTables,
    CourseResults,
    UseDedicatedPreviewAudio,
    UnlockFrameRateLimit,
}
