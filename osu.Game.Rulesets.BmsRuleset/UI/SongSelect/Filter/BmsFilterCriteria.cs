using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Conversion;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.DifficultyTable;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.Filter;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;

namespace osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Filter;

public class BmsFilterCriteria : IRulesetFilterCriteria
{
    private readonly HashSet<BmsLayoutVariant> enabledVariants;

    private HashSet<BmsLayoutVariant>? keyRestrictedVariants;
    private FilterCriteria.OptionalRange<float> longNotePercentage;
    private FilterCriteria.OptionalRange<float> scratchPercentage;
    private FilterCriteria.OptionalTextFilter source;
    private string? selectedTableName;
    private string? selectedLevel;

    public BmsFilterCriteria(BmsRulesetConfigManager? config)
    {
        enabledVariants = BmsLayoutVisibility.Options.Where(option => config == null || config.Get<bool>(option.Setting))
            .Select(option => option.Layout).ToHashSet();
    }

    public bool Matches(BeatmapInfo beatmapInfo, FilterCriteria criteria)
    {
        var variant = GetVariant(beatmapInfo);
        var layoutKnown = !UnavailableTableBeatmapFactory.IsUnavailable(beatmapInfo);

        var visibilityVariant = variant switch
        {
            BmsLayoutVariant.Bms5K2P => BmsLayoutVariant.Bms5K,
            BmsLayoutVariant.Bme7K2P => BmsLayoutVariant.Bme7K,
            BmsLayoutVariant.Popn9K2P => BmsLayoutVariant.Popn9K,
            _ when BmsLayout.IsGeneric(variant) => BmsLayoutVariant.Generic,
            _ => variant,
        };
        if (layoutKnown && !enabledVariants.Contains(visibilityVariant))
            return false;

        if (layoutKnown && keyRestrictedVariants != null && !keyRestrictedVariants.Contains(variant))
            return false;

        if (source.HasFilter && !source.Matches(beatmapInfo.Metadata.Source))
            return false;

        if (longNotePercentage.HasFilter && !longNotePercentage.IsInRange(calculatePercentage(beatmapInfo.EndTimeObjectCount, beatmapInfo.TotalObjectCount)))
            return false;

        if (scratchPercentage.HasFilter)
        {
            var scratchObjectCount = 0;
            var layoutHasScratch = Enumerable.Range(0, BmsLayout.GetTotalColumns(variant))
                .Any(column => BmsLayout.IsScratchColumn(column, variant));

            if (layoutHasScratch && !BmsBeatmapStatistics.TryGetScratchObjectCount(beatmapInfo.Difficulty, out scratchObjectCount))
                return false;

            if (!scratchPercentage.IsInRange(calculatePercentage(scratchObjectCount, beatmapInfo.TotalObjectCount)))
                return false;
        }

        var store = BmsRulesetRuntime.DifficultyTableStore;
        if (store != null && (!string.IsNullOrEmpty(selectedTableName) || !string.IsNullOrEmpty(selectedLevel)))
        {
            var markers = store.GetMarkers(beatmapInfo.MD5Hash, beatmapInfo.Hash);

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

    internal static BmsLayoutVariant GetVariant(BeatmapInfo beatmapInfo)
    {
        return Mania7KBeatmapConverter.CanConvert(beatmapInfo)
            ? Mania7KBeatmapConverter.GetConvertedDifficultyInfo(beatmapInfo).LayoutVariant
            : BmsDifficultyInfo.GetLayout(beatmapInfo.Difficulty);
    }

    public bool TryParseCustomKeywordCriteria(string key, Operator op, string strValues)
    {
        switch (key)
        {
            case "k":
            case "key":
            case "keys":
                return tryParseKeyCount(op, strValues);

            case "src":
            case "source":
                return FilterQueryParser.TryUpdateCriteriaText(ref source, op, strValues);

            case "ln":
            case "lns":
                return FilterQueryParser.TryUpdateCriteriaRange(ref longNotePercentage, op, strValues);

            case "sc":
            case "scratch":
                return FilterQueryParser.TryUpdateCriteriaRange(ref scratchPercentage, op, strValues);

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

    public bool FilterMayChangeFromMods(FilterCriteria criteria, ValueChangedEvent<IReadOnlyList<Mod>> mods) =>
        BmsBeatmapCarouselDifficultyFilter.UsesDifficulty(criteria)
        && mods.OldValue.OfType<BmsModHideScratch>().Any() != mods.NewValue.OfType<BmsModHideScratch>().Any();

    private static float calculatePercentage(int matchingObjectCount, int totalObjectCount) =>
        matchingObjectCount / (float)Math.Max(1, totalObjectCount) * 100;

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

        var allowedVariants = new HashSet<BmsLayoutVariant>(BmsKeyBindingConfiguration.AvailableVariants.Select(value => (BmsLayoutVariant)value));

        switch (op)
        {
            case Operator.Equal:
                allowedVariants.RemoveWhere(v => !keyCounts.Contains(BmsLayout.GetManiaKeyCount(v)));
                break;

            case Operator.NotEqual:
                allowedVariants.RemoveWhere(v => keyCounts.Contains(BmsLayout.GetManiaKeyCount(v)));
                break;

            case Operator.Less:
                if (singleKeyCount == null) return false;

                allowedVariants.RemoveWhere(v => BmsLayout.GetManiaKeyCount(v) >= singleKeyCount.Value);
                break;

            case Operator.LessOrEqual:
                if (singleKeyCount == null) return false;

                allowedVariants.RemoveWhere(v => BmsLayout.GetManiaKeyCount(v) > singleKeyCount.Value);
                break;

            case Operator.Greater:
                if (singleKeyCount == null) return false;

                allowedVariants.RemoveWhere(v => BmsLayout.GetManiaKeyCount(v) <= singleKeyCount.Value);
                break;

            case Operator.GreaterOrEqual:
                if (singleKeyCount == null) return false;

                allowedVariants.RemoveWhere(v => BmsLayout.GetManiaKeyCount(v) < singleKeyCount.Value);
                break;

            default:
                return false;
        }

        keyRestrictedVariants = allowedVariants;
        return true;
    }
}
