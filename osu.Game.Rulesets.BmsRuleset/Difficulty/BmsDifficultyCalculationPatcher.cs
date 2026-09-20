using System;
using System.Collections.Generic;
using System.Threading;
using HarmonyLib;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.HUD;

namespace osu.Game.Rulesets.BmsRuleset.Difficulty;

internal static class BmsDifficultyCalculationPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.BmsRuleset.DifficultyCalculation";
    private static readonly object install_lock = new();
    private static Func<PerformancePointsCounter, GameplayState?>? getGameplayState;
    private static bool installed;

    internal static bool InstallOnce()
    {
        lock (install_lock)
        {
            if (installed)
                return true;

            var harmony = new Harmony(harmony_id);
            try
            {
                getGameplayState = AccessTools.PropertyGetter(typeof(PerformancePointsCounter), "gameplayState")
                    .CreateDelegate<Func<PerformancePointsCounter, GameplayState?>>();
                harmony.Patch(AccessTools.Method(typeof(PerformancePointsCounter), "load"),
                    prefix: new HarmonyMethod(typeof(BmsDifficultyCalculationPatcher), nameof(shouldLoadCounter)));

                // These upstream entry points are non-virtual and their cancellation checks live in
                // Skill processing, which BMS bypasses. Carry the caller's token to our own algorithm.
                foreach (var name in new[] { nameof(DifficultyCalculator.Calculate), nameof(DifficultyCalculator.CalculateTimed) })
                {
                    harmony.Patch(AccessTools.Method(typeof(DifficultyCalculator), name, [typeof(IEnumerable<Mod>), typeof(CancellationToken)]),
                        prefix: new HarmonyMethod(typeof(BmsDifficultyCalculationPatcher), nameof(beginCalculation)),
                        finalizer: new HarmonyMethod(typeof(BmsDifficultyCalculationPatcher), nameof(endCalculation)));
                }

                installed = true;
                return true;
            }
            catch (Exception exception)
            {
                harmony.UnpatchAll(harmony_id);
                BmsLogger.Error(exception, "BMS difficulty calculation patches could not be installed.");
                return false;
            }
        }
    }

    // ReSharper disable InconsistentNaming
    private static bool shouldLoadCounter(PerformancePointsCounter __instance)
    {
        var ruleset = getGameplayState!(__instance)?.Ruleset;
        // No BMS PP can be displayed without a calculator; computing every exact prefix only
        // monopolises the shared difficulty queue. Keep the public timed API's exact semantics.
        return ruleset is not BmsRuleset || ruleset.CreatePerformanceCalculator() != null;
    }

    private static void beginCalculation(DifficultyCalculator __instance, ref CancellationToken cancellationToken, out CancellationTokenSource? __state)
    {
        __state = null;
        if (__instance is not BmsDifficultyCalculator bms)
            return;

        if (!cancellationToken.CanBeCanceled)
        {
            __state = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            cancellationToken = __state.Token;
        }

        bms.CalculationCancellationToken = cancellationToken;
    }

    private static void endCalculation(DifficultyCalculator __instance, CancellationTokenSource? __state)
    {
        if (__instance is BmsDifficultyCalculator bms)
            bms.CalculationCancellationToken = default;
        __state?.Dispose();
    }
    // ReSharper restore InconsistentNaming
}
