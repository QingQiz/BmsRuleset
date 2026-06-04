using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.Filter;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;

namespace osu.Game.Rulesets.BmsRuleset;

public class BmsFilterCriteria : IRulesetFilterCriteria
{
    private readonly HashSet<BmsLayoutVariant> enabledVariants;
    private HashSet<BmsLayoutVariant>? keyRestrictedVariants;

    private static readonly Dictionary<int, BmsLayoutVariant> column_to_variant = new()
    {
        [BmsLayout.BMS5_KEY_COLUMNS] = BmsLayoutVariant.Bms5K,
        [BmsLayout.BME7_KEY_COLUMNS] = BmsLayoutVariant.Bme7K,
        [BmsLayout.PMS_COLUMNS] = BmsLayoutVariant.Pms9K,
        [BmsLayout.BMS5_DOUBLE_PLAY_COLUMNS] = BmsLayoutVariant.Bms5KDouble,
        [BmsLayout.DOUBLE_PLAY_COLUMNS] = BmsLayoutVariant.Bme7KDouble,
        [BmsLayout.PMS_DOUBLE_PLAY_COLUMNS] = BmsLayoutVariant.Pms9KDouble,
    };

    public BmsFilterCriteria(BmsRulesetConfigManager? config)
    {

        if (config != null)
        {
            enabledVariants = new HashSet<BmsLayoutVariant>(6);

            if (config.Get<bool>(BmsRulesetSetting.ShowBms5K))
                enabledVariants.Add(BmsLayoutVariant.Bms5K);
            if (config.Get<bool>(BmsRulesetSetting.ShowBme7K))
                enabledVariants.Add(BmsLayoutVariant.Bme7K);
            if (config.Get<bool>(BmsRulesetSetting.ShowPms9K))
                enabledVariants.Add(BmsLayoutVariant.Pms9K);
            if (config.Get<bool>(BmsRulesetSetting.ShowBms5KDouble))
                enabledVariants.Add(BmsLayoutVariant.Bms5KDouble);
            if (config.Get<bool>(BmsRulesetSetting.ShowBme7KDouble))
                enabledVariants.Add(BmsLayoutVariant.Bme7KDouble);
            if (config.Get<bool>(BmsRulesetSetting.ShowPms9KDouble))
                enabledVariants.Add(BmsLayoutVariant.Pms9KDouble);
        }
        else
        {
            enabledVariants =
            [
                BmsLayoutVariant.Bms5K, BmsLayoutVariant.Bme7K, BmsLayoutVariant.Pms9K,
                BmsLayoutVariant.Bms5KDouble, BmsLayoutVariant.Bme7KDouble, BmsLayoutVariant.Pms9KDouble,
            ];
        }
    }

    public bool Matches(BeatmapInfo beatmapInfo, FilterCriteria criteria)
    {
        var keyCount = (int)System.Math.Round(beatmapInfo.Difficulty.CircleSize);
        var variant = variantFromColumns(keyCount);

        if (!enabledVariants.Contains(variant))
            return false;

        if (keyRestrictedVariants != null && !keyRestrictedVariants.Contains(variant))
            return false;

        return true;
    }

    public bool TryParseCustomKeywordCriteria(string key, Operator op, string strValues)
    {
        switch (key)
        {
            case "k":
            case "key":
            case "keys":
                return tryParseKeyCount(op, strValues);
        }

        return false;
    }

    private bool tryParseKeyCount(Operator op, string strValues)
    {
        var keyCounts = new HashSet<int>();

        foreach (var strValue in strValues.Split(','))
        {
            if (!int.TryParse(strValue, out var keyCount))
                return false;

            keyCounts.Add(keyCount);
        }

        int? singleKeyCount = keyCounts.Count == 1 ? keyCounts.Single() : null;

        var allowedKeys = new HashSet<int>(column_to_variant.Keys);

        switch (op)
        {
            case Operator.Equal:
                allowedKeys.IntersectWith(keyCounts);
                break;

            case Operator.NotEqual:
                allowedKeys.ExceptWith(keyCounts);
                break;

            case Operator.Less:
                if (singleKeyCount == null) return false;

                allowedKeys.RemoveWhere(k => k >= singleKeyCount.Value);
                break;

            case Operator.LessOrEqual:
                if (singleKeyCount == null) return false;

                allowedKeys.RemoveWhere(k => k > singleKeyCount.Value);
                break;

            case Operator.Greater:
                if (singleKeyCount == null) return false;

                allowedKeys.RemoveWhere(k => k <= singleKeyCount.Value);
                break;

            case Operator.GreaterOrEqual:
                if (singleKeyCount == null) return false;

                allowedKeys.RemoveWhere(k => k < singleKeyCount.Value);
                break;
        }

        keyRestrictedVariants = new HashSet<BmsLayoutVariant>(allowedKeys.Select(k => column_to_variant[k]));
        return true;
    }

    public bool FilterMayChangeFromMods(FilterCriteria criteria, ValueChangedEvent<IReadOnlyList<Mod>> mods) => false;

    private static BmsLayoutVariant variantFromColumns(int columns) => column_to_variant.GetValueOrDefault(columns, BmsLayoutVariant.Bms5K);
}
