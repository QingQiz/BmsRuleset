using System;
using HarmonyLib;
using osu.Framework.Graphics;
using osu.Framework.Input.Events;
using osu.Framework.Logging;
using osu.Game.Input.Bindings;
using osu.Game.Screens.Ranking;
using osu.Game.Screens.Ranking.Statistics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Course;

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
            var selectTarget = AccessTools.Method(typeof(ResultsScreen), nameof(ResultsScreen.OnPressed), [typeof(KeyBindingPressEvent<GlobalAction>)]);
            var selectPrefix = AccessTools.Method(typeof(BmsCourseResultsPatcher), nameof(onSelectPrefix));

            if (target == null || prefix == null || selectTarget == null || selectPrefix == null)
            {
                disable("osu! results screen internals no longer match the BMS course-results patch expectations.");
                return;
            }

            new Harmony(harmony_id).Patch(target, prefix: new HarmonyMethod(prefix));
            new Harmony(harmony_id).Patch(selectTarget, prefix: new HarmonyMethod(selectPrefix));
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
        if (__instance.FindClosestParent<BmsCourseResultsScreen>() == null
            && __instance.FindClosestParent<BmsCourseStageResultsScreen>() == null)
            return true;

        // Do not consume the click so the surrounding result screen can handle it.
        __result = false;
        return false;
    }

    private static bool onSelectPrefix(ResultsScreen __instance, KeyBindingPressEvent<GlobalAction> e, ref bool __result)
    {
        if (e.Action != GlobalAction.Select || e.Repeat || __instance is not BmsCourseStageResultsScreen stageResults)
            return true;

        stageResults.AdvanceFromEnter();
        __result = true;
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
