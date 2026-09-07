using System;
using HarmonyLib;
using osu.Framework.Screens;
using osu.Game.Screens.Ranking;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result;

internal static class BmsResultsScreenEntryPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.BmsRuleset.ResultsScreenEntry";
    private static bool disabled;

    internal static bool IsInstalled { get; private set; }

    internal static void InstallOnce()
    {
        if (IsInstalled || disabled)
            return;

        var harmony = new Harmony(harmony_id);
        try
        {
            // All solo result entry points converge here before the native screen starts loading.
            var target = AccessTools.Method(typeof(ScreenStack), "Push", [typeof(IScreen), typeof(IScreen)]);
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(BmsResultsScreenEntryPatcher), nameof(pushPrefix)));
            IsInstalled = true;
        }
        catch (Exception exception)
        {
            harmony.UnpatchAll(harmony_id);
            disabled = true;
            BmsLogger.Error(exception, "Failed to install the BMS results screen entry.");
        }
    }

    private static void pushPrefix(ref IScreen newScreen)
    {
        if (GetReplacement(newScreen) is not { } replacement)
            return;

        var original = (ResultsScreen)newScreen;
        newScreen = replacement;
        original.Dispose();
    }

    internal static BmsResultsScreen? GetReplacement(IScreen screen)
    {
        if (screen is BmsResultsScreenRequest request)
            return request.Screen;

        // BMS uses solo results; specialised online screens have separate navigation and score ownership.
        if (screen.GetType() != typeof(SoloResultsScreen)
            || screen is not SoloResultsScreen { Score: { } score } results
            || score.Ruleset.ShortName != Constant.SHORT_NAME)
            return null;

        return new BmsResultsScreen(score)
        {
            AllowRetry = results.AllowRetry,
            AllowWatchingReplay = results.AllowWatchingReplay,
        };
    }
}
