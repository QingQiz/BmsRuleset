using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

    private static readonly Dictionary<int, BmsLayoutVariant> column_to_variant = new()
    {
        [BmsLayout.BMS5_KEY_COLUMNS] = BmsLayoutVariant.Bms5K,
        [BmsLayout.BME7_KEY_COLUMNS] = BmsLayoutVariant.Bme7K,
        [BmsLayout.PMS_COLUMNS] = BmsLayoutVariant.Pms9K,
        [BmsLayout.BMS5_DOUBLE_PLAY_COLUMNS] = BmsLayoutVariant.Bms5KDouble,
        [BmsLayout.DOUBLE_PLAY_COLUMNS] = BmsLayoutVariant.Bme7KDouble,
        [BmsLayout.PMS_DOUBLE_PLAY_COLUMNS] = BmsLayoutVariant.Pms9KDouble,
    };

    private HashSet<BmsLayoutVariant>? keyRestrictedVariants;
    private string? selectedTableName;
    private string? selectedLevel;

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
        var keyCount = (int)Math.Round(beatmapInfo.Difficulty.CircleSize);
        var variant = variantFromColumns(keyCount);

        if (!enabledVariants.Contains(variant))
            return false;

        if (keyRestrictedVariants != null && !keyRestrictedVariants.Contains(variant))
            return false;

        var store = BmsRuleset.DifficultyTableStore;
        if (store != null && (!string.IsNullOrEmpty(selectedTableName) || !string.IsNullOrEmpty(selectedLevel)))
        {
            var markers = store.GetMarkers(beatmapInfo.MD5Hash);

            if (!string.IsNullOrEmpty(selectedTableName))
            {
                if (!markers.Any(m => m.table.Name.Contains(selectedTableName, StringComparison.OrdinalIgnoreCase)
                                      || m.table.Symbol.Contains(selectedTableName, StringComparison.OrdinalIgnoreCase)))
                    return false;
            }

            if (!string.IsNullOrEmpty(selectedLevel))
            {
                if (!markers.Any(m => fuzzyLevelMatch(selectedLevel, m.table.Symbol, m.entry.Level)))
                    return false;
            }
        }

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

            case "tb":
            case "table":
                selectedTableName = strValues;
                return true;

            case "lv":
            case "level":
                selectedLevel = strValues;
                return true;
        }

        return false;
    }

    public bool FilterMayChangeFromMods(FilterCriteria criteria, ValueChangedEvent<IReadOnlyList<Mod>> mods) => false;

    private static BmsLayoutVariant variantFromColumns(int columns) => column_to_variant.GetValueOrDefault(columns, BmsLayoutVariant.Bms5K);

    /// <summary>
    /// Fuzzy-match a user-typed level filter against a table entry's level.
    /// Tries several strategies in order of precision:
    /// <list type="number">
    ///   <item>Exact match on the combined symbol+level string.</item>
    ///   <item>Exact match on just the level string.</item>
    ///   <item>Containment: the entry level contains the filter text.</item>
    ///   <item>Numeric match: if both are numbers, compare numerically.</item>
    /// </list>
    /// </summary>
    private static bool fuzzyLevelMatch(string filter, string tableSymbol, string entryLevel)
    {
        // 1) Exact match on combined "{symbol}{level}" (e.g. "IT★1")
        if ($"{tableSymbol}{entryLevel}".Equals(filter, StringComparison.OrdinalIgnoreCase))
            return true;

        // 2) Exact match on the level alone
        if (entryLevel.Equals(filter, StringComparison.OrdinalIgnoreCase))
            return true;

        // 3) Contains/substring match (the core "fuzzy" behaviour)
        if (entryLevel.Contains(filter, StringComparison.OrdinalIgnoreCase))
            return true;

        // 4) If the filter is a plain number, try extracting the numeric portion
        //    from the entry level (stripping non-numeric prefix like ★ / ☆ / ◆)
        //    and compare as integers.
        if (int.TryParse(filter, out var filterNum))
        {
            var numericPart = extractNumericPart(entryLevel);
            if (numericPart == filterNum)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Extract the trailing numeric value from a level string.
    /// "★12" → 12, "☆03" → 3, "12" → 12, "★★★" → null.
    /// </summary>
    private static int? extractNumericPart(string level)
    {
        var digits = new StringBuilder();
        foreach (var c in level)
        {
            if (c >= '0' && c <= '9')
                digits.Append(c);
        }

        return digits.Length > 0 && int.TryParse(digits.ToString(), out var result)
            ? result
            : null;
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
}
