using System;
using System.Collections.Generic;
using osu.Game.Beatmaps;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps.Conversion;

internal static class BmsForeignBeatmapConverterRegistry
{
    private static readonly IBmsForeignBeatmapConverter[] converters =
    [
        new Mania7KBeatmapConverter(),
    ];

    public static IBmsForeignBeatmapConverter? FindConverter(IBeatmap source) =>
        findConverter(converters, converter => converter.CanConvert(source), source.BeatmapInfo.Ruleset.ShortName);

    public static IBmsForeignBeatmapConverter? FindConverter(IBeatmapInfo source) =>
        findConverter(converters, converter => converter.CanConvert(source), source.Ruleset.ShortName);

    public static bool AllowsGameplay(IBeatmapInfo source, RulesetInfo targetRuleset, bool allowConversion)
    {
        if (source.AllowGameplayWithRuleset(targetRuleset, allowConversion))
            return true;

        return allowConversion
               && targetRuleset.ShortName == Constant.SHORT_NAME
               && FindConverter(source) != null;
    }

    internal static IBmsForeignBeatmapConverter? FindConverter(
        IBeatmap source,
        IReadOnlyList<IBmsForeignBeatmapConverter> candidates) =>
        findConverter(candidates, converter => converter.CanConvert(source), source.BeatmapInfo.Ruleset.ShortName);

    private static IBmsForeignBeatmapConverter? findConverter(
        IReadOnlyList<IBmsForeignBeatmapConverter> candidates,
        Func<IBmsForeignBeatmapConverter, bool> canConvert,
        string sourceRuleset)
    {
        IBmsForeignBeatmapConverter? match = null;

        foreach (var converter in candidates)
        {
            if (!canConvert(converter))
                continue;

            if (match != null)
            {
                throw new InvalidOperationException(
                    $"Multiple BMS beatmap converters match {sourceRuleset}: " +
                    $"{match.GetType().Name} and {converter.GetType().Name}.");
            }

            match = converter;
        }

        return match;
    }
}
