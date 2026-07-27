using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;

namespace osu.Game.Rulesets.BmsRuleset;

internal static class BmsRulesetRuntime
{
    internal static BmsRulesetConfigManager? ConfigManager { get; set; }

    internal static DifficultyTableStore? DifficultyTableStore { get; set; }

    internal static BmsVisualOffsetSuggestionStore VisualOffsetSuggestions { get; } = new();

    internal static BmsReferenceBpmMode CurrentReferenceBpmMode =>
        ConfigManager?.Get<BmsReferenceBpmMode>(BmsRulesetSetting.ReferenceBpmMode) ?? BmsReferenceBpmMode.MainBpm;

    internal static bool UseDedicatedPreviewAudio =>
        ConfigManager?.Get<bool>(BmsRulesetSetting.UseDedicatedPreviewAudio) ?? true;
}
