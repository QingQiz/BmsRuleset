using System;
using HarmonyLib;
using osu.Framework.Graphics;
using osu.Framework.Input.Events;
using osu.Framework.Logging;
using osu.Game.Screens.Ranking.Statistics;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

internal static class BmsCourseResultsPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.BmsRuleset.CourseResults";
    private static bool disabled;

    internal static bool IsInstalled { get; private set; }

    internal static void InstallOnce()
    {
        if (IsInstalled || disabled)
            return;

        try
        {
            var target = AccessTools.Method(typeof(StatisticsPanel), "OnClick", [typeof(ClickEvent)]);
            var prefix = AccessTools.Method(typeof(BmsCourseResultsPatcher), nameof(onClickPrefix));

            if (target == null || prefix == null)
            {
                disable("osu! statistics panel internals no longer match the BMS course-results patch expectations.");
                return;
            }

            new Harmony(harmony_id).Patch(target, prefix: new HarmonyMethod(prefix));
            IsInstalled = true;
        }
        catch (Exception e)
        {
            disable("Failed to disable click-to-close on BMS course results.", e);
        }
    }

    // ReSharper disable InconsistentNaming
    private static bool onClickPrefix(StatisticsPanel __instance, ref bool __result)
    {
        if (__instance.FindClosestParent<BmsCourseResultsScreen>() == null)
            return true;

        // Do not consume the click: stage cards are below the full-screen
        // statistics panel and must receive the same event.
        __result = false;
        return false;
    }
    // ReSharper restore InconsistentNaming

    private static void disable(string message, Exception? exception = null)
    {
        disabled = true;

        if (exception == null)
            BmsLogger.Log(message, LogLevel.Important);
        else
            BmsLogger.Error(exception, message);
    }
}
