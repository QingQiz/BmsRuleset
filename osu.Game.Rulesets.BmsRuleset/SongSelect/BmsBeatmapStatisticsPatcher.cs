using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using HarmonyLib;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

/// <summary>
/// Prevents stale song-select statistics work from surfacing as an unobserved conversion error.
/// </summary>
internal static class BmsBeatmapStatisticsPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.BmsRuleset.BeatmapStatistics";

    internal static void InstallOnce()
    {
        var target = AccessTools.Method(typeof(WorkingBeatmap), nameof(WorkingBeatmap.GetPlayableBeatmap),
            [typeof(IRulesetInfo), typeof(IReadOnlyList<Mod>), typeof(CancellationToken)]);
        var prefix = AccessTools.Method(typeof(BmsBeatmapStatisticsPatcher), nameof(BmsBeatmapStatisticsPatcher.prefix));

        if (target != null && prefix != null)
            new Harmony(harmony_id).Patch(target, prefix: new HarmonyMethod(prefix));
    }

    // ReSharper disable InconsistentNaming
    private static bool prefix(WorkingBeatmap __instance, IRulesetInfo ruleset, ref IBeatmap __result)
    {
        if (ruleset.ShortName != Constant.SHORT_NAME || !isTitleWedgeCall())
            return true;

        var converter = ruleset.CreateInstance().CreateBeatmapConverter(__instance.Beatmap);
        if (converter.CanConvert())
            return true;

        __result = new BmsBeatmap
        {
            BeatmapInfo = __instance.BeatmapInfo.Clone(),
        };
        return false;
    }
    // ReSharper restore InconsistentNaming

    private static bool isTitleWedgeCall() => new StackTrace().GetFrames().Any(frame =>
        frame.GetMethod()?.DeclaringType?.FullName?.Contains("BeatmapTitleWedge", StringComparison.Ordinal) == true);
}
