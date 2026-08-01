using System;
using System.Linq;
using HarmonyLib;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Conversion;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

public static class BmsConvertedBeatmapFilterPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.BmsRuleset.ConvertedBeatmapFilter";

    private static readonly object install_lock = new();

    public static bool IsInstalled { get; private set; }

    public static void InstallOnce()
    {
        lock (install_lock)
        {
            if (IsInstalled)
                return;

            var target = AccessTools.Method(
                typeof(BeatmapInfoExtensions),
                nameof(BeatmapInfoExtensions.AllowGameplayWithRuleset),
                [typeof(IBeatmapInfo), typeof(RulesetInfo), typeof(bool)]);
            var postfixMethod = AccessTools.Method(typeof(BmsConvertedBeatmapFilterPatcher), nameof(postfix));

            var missingMembers = new[]
            {
                (name: "BeatmapInfoExtensions.AllowGameplayWithRuleset", member: target),
                (name: "BmsConvertedBeatmapFilterPatcher.postfix", member: postfixMethod),
            }.Where(m => m.member == null).Select(m => m.name).ToArray();

            if (missingMembers.Length > 0)
            {
                BmsLogger.Log(
                    $"BMS converted beatmap filter patch cannot be installed. Missing: {string.Join(", ", missingMembers)}.",
                    level: LogLevel.Error);
                return;
            }

            try
            {
                new Harmony(harmony_id).Patch(target, postfix: new HarmonyMethod(postfixMethod));
                IsInstalled = true;
            }
            catch (Exception exception)
            {
                BmsLogger.Error(exception, "Failed to install the BMS converted beatmap filter patch.");
            }
        }
    }

    internal static bool ApplyConversionAllowance(
        bool currentResult,
        IBeatmapInfo beatmap,
        RulesetInfo ruleset,
        bool allowConversion)
    {
        if (currentResult || !allowConversion || ruleset.ShortName != Constant.SHORT_NAME)
            return currentResult;

        return BmsForeignBeatmapConverterRegistry.FindConverter(beatmap) != null;
    }

    // ReSharper disable once InconsistentNaming
    private static void postfix(IBeatmapInfo beatmap, RulesetInfo ruleset, bool allowConversion, ref bool __result)
    {
        try
        {
            __result = ApplyConversionAllowance(__result, beatmap, ruleset, allowConversion);
        }
        catch (Exception exception)
        {
            BmsLogger.Error(exception, $"Failed to evaluate BMS conversion visibility for {beatmap}.");
        }
    }
}
