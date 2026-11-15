using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using osu.Framework.Screens;
using osu.Game;
using osu.Game.Screens.OnlinePlay.DailyChallenge;
using osu.Game.Screens.Ranking;
using NativeSongSelect = osu.Game.Screens.Select.SongSelect;

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

            var performFromScreen = AccessTools.Method(typeof(OsuGame), nameof(OsuGame.PerformFromScreen), [typeof(Action<IScreen>), typeof(IEnumerable<Type>)]);
            harmony.Patch(performFromScreen, prefix: new HarmonyMethod(typeof(BmsResultsScreenEntryPatcher), nameof(performFromScreenPrefix)));
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

    // ReSharper disable InconsistentNaming
    private static void performFromScreenPrefix(OsuGame __instance, ref IEnumerable<Type>? validScreens)
    {
        if (ShouldUseResultAsReplayOrigin(__instance.ScreenStack.CurrentScreen, validScreens))
            validScreens = validScreens!.Append(typeof(BmsResultsScreen)).ToArray();
    }
    // ReSharper restore InconsistentNaming

    internal static bool ShouldUseResultAsReplayOrigin(IScreen? currentScreen, IEnumerable<Type>? validScreens)
        => currentScreen is BmsResultsScreen
           && validScreens is not null
           && validScreens.Contains(typeof(NativeSongSelect))
           && validScreens.Contains(typeof(DailyChallenge));

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
