using System;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using osu.Framework.Logging;
using osu.Game.Beatmaps;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps;

public static class BmsWorkingBeatmapPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.BmsRuleset.WorkingBeatmap";

    private static readonly object install_lock = new();
    private static readonly ConditionalWeakTable<WorkingBeatmapCache, BmsWorkingBeatmapCache> wrapper_caches = new();

    /// <summary>
    ///     Install the BMS working-beatmap Harmony patch. Safe to call multiple times (idempotent).
    /// </summary>
    public static bool InstallOnce()
    {
        lock (install_lock)
        {
            if (IsInstalled)
                return true;

            var target = AccessTools.Method(typeof(WorkingBeatmapCache), nameof(WorkingBeatmapCache.GetWorkingBeatmap), [typeof(BeatmapInfo)]);
            var postfixMethod = AccessTools.Method(typeof(BmsWorkingBeatmapPatcher), nameof(postfix));

            var missingMembers = new[]
            {
                (name: "WorkingBeatmapCache.GetWorkingBeatmap", member: target),
            }.Where(m => m.member == null).Select(m => m.name).ToArray();

            if (missingMembers.Length > 0)
            {
                Logger.Log(
                    "BMS WorkingBeatmapPatcher: Cannot install Harmony patch. Missing: "
                    + $"{string.Join(", ", missingMembers)}. BMS preview audio will not be available.",
                    level: LogLevel.Error);
                return false;
            }

            try
            {
                new Harmony(harmony_id).Patch(target, postfix: new HarmonyMethod(postfixMethod));
                IsInstalled = true;
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "BMS WorkingBeatmapPatcher: Failed to install Harmony patch. BMS preview audio will not be available.");
                return false;
            }
        }
    }

    public static bool IsInstalled { get; private set; }

    // ReSharper disable InconsistentNaming
    private static void postfix(WorkingBeatmapCache __instance, BeatmapInfo? beatmapInfo, ref WorkingBeatmap __result)
    {
        _ = beatmapInfo;

        if (__result is BmsWorkingBeatmap)
            return;

        if (__result.BeatmapInfo.Ruleset.ShortName != "bms")
            return;

        try
        {
            __result = wrapper_caches.GetValue(__instance, static cache => new BmsWorkingBeatmapCache(cache)).Wrap(__result);
        }
        catch (Exception e)
        {
            Logger.Error(e, "BMS WorkingBeatmapPatcher: Failed to wrap a BMS working beatmap. Falling back to osu!'s default working beatmap.");
        }
    }
    // ReSharper restore InconsistentNaming
}
