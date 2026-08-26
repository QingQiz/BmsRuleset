using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Rulesets.BmsRuleset.Result.Course;
using osu.Game.Screens.Ranking.Expanded;
using osu.Game.Screens.Ranking.Expanded.Statistics;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Result.Statistic;

internal static class BmsResultStatisticsPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.BmsRuleset.ResultStatistics";
    private static bool installed;

    [ThreadStatic]
    private static ScoreInfo? currentScore;

    internal static void InstallOnce()
    {
        if (installed)
            return;

        var target = AccessTools.Method(typeof(ExpandedPanelMiddleContent), "load");
        var transpiler = AccessTools.Method(typeof(BmsResultStatisticsPatcher), nameof(transpile));
        if (target == null || transpiler == null)
        {
            BmsLogger.Log("BMS result statistics patch: ExpandedPanelMiddleContent.load is unavailable.", LogLevel.Error);
            return;
        }

        try
        {
            var prefix = AccessTools.Method(typeof(BmsResultStatisticsPatcher), nameof(loadPrefix));
            var postfix = AccessTools.Method(typeof(BmsResultStatisticsPatcher), nameof(loadPostfix));
            new Harmony(harmony_id).Patch(target, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix), transpiler: new HarmonyMethod(transpiler));
            installed = true;
        }
        catch (Exception exception)
        {
            BmsLogger.Error(exception, "BMS result statistics patch: failed to replace native statistics.");
        }
    }

    // ReSharper disable InconsistentNaming
    private static void loadPrefix(ExpandedPanelMiddleContent __instance)
    {
        if (__instance.FindClosestParent<BmsCourseResultsScreen>() != null
            || __instance.FindClosestParent<BmsCourseStageResultsScreen>() != null)
        {
            currentScore = null;
            return;
        }

        var field = AccessTools.Field(typeof(ExpandedPanelMiddleContent), "score");
        currentScore = field?.GetValue(__instance) as ScoreInfo;
    }
    // ReSharper restore InconsistentNaming

    private static void loadPostfix() => currentScore = null;

    private static StatisticDisplay createAccuracy(double accuracy) => isBms()
        ? new BmsAccuracyStatistic(accuracy)
        : new AccuracyStatistic(accuracy);

    private static StatisticDisplay createExScoreOrCombo(int combo, int? maximumCombo) => isBms()
        ? new BmsExScoreStatistic(currentScore!)
        : new ComboStatistic(combo, maximumCombo);

    private static StatisticDisplay createComboOrPerformance(ScoreInfo score) => isBms()
        ? new BmsComboStatistic(score.MaxCombo, score.GetMaximumAchievableCombo())
        : new PerformanceStatistic(score);

    private static bool isBms() => currentScore?.Ruleset.ShortName == Constant.SHORT_NAME;

    private static IEnumerable<CodeInstruction> transpile(IEnumerable<CodeInstruction> instructions)
    {
        var accuracyConstructor = AccessTools.Constructor(typeof(AccuracyStatistic), [typeof(double)]);
        var comboConstructor = AccessTools.Constructor(typeof(ComboStatistic), [typeof(int), typeof(int?)]);
        var performanceConstructor = AccessTools.Constructor(typeof(PerformanceStatistic), [typeof(ScoreInfo)]);
        var accuracyFactory = AccessTools.Method(typeof(BmsResultStatisticsPatcher), nameof(createAccuracy));
        var comboFactory = AccessTools.Method(typeof(BmsResultStatisticsPatcher), nameof(createExScoreOrCombo));
        var performanceFactory = AccessTools.Method(typeof(BmsResultStatisticsPatcher), nameof(createComboOrPerformance));

        var replacements = new Dictionary<ConstructorInfo, MethodInfo>
        {
            [accuracyConstructor!] = accuracyFactory!,
            [comboConstructor!] = comboFactory!,
            [performanceConstructor!] = performanceFactory!,
        };

        var counts = replacements.Keys.ToDictionary(method => method, _ => 0);
        foreach (var instruction in instructions)
        {
            if (instruction.operand is ConstructorInfo method && replacements.TryGetValue(method, out var replacement))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                counts[method]++;
            }

            yield return instruction;
        }

        if (counts.Any(pair => pair.Value != 1))
            throw new InvalidOperationException("BMS result statistics patch expected one construction of each native statistic.");
    }
}
