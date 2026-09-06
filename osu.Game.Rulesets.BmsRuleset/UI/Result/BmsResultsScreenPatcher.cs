using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Events;
using osu.Game.Graphics.Containers;
using osu.Game.Input.Bindings;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Course;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Screens.Ranking;
using osu.Game.Screens.Ranking.Statistics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result;

internal static class BmsResultsScreenPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.BmsRuleset.ResultsScreen";
    private static bool disabled;

    internal static bool IsInstalled { get; private set; }

    internal static Container GetBottomPanel(ResultsScreen screen) =>
        (Container)AccessTools.Field(typeof(ResultsScreen), "bottomPanel").GetValue(screen)!;

    internal static void InstallOnce()
    {
        if (IsInstalled || disabled)
            return;

        var harmony = new Harmony(harmony_id);
        try
        {
            harmony.Patch(AccessTools.Method(typeof(ResultsScreen), nameof(ResultsScreen.OnPressed)),
                prefix: new HarmonyMethod(typeof(BmsResultsScreenPatcher), nameof(onSelectPrefix)));
            harmony.Patch(AccessTools.Method(typeof(ResultsScreen), "load"),
                transpiler: new HarmonyMethod(typeof(BmsResultsScreenPatcher), nameof(replaceStatisticsPanel)));
            harmony.Patch(AccessTools.Method(typeof(StatisticsPanel), "populateStatistics"),
                prefix: new HarmonyMethod(typeof(BmsResultsScreenPatcher), nameof(populateStatisticsPrefix)));
            harmony.Patch(AccessTools.Method(typeof(ResultsScreen), "LoadComplete"),
                postfix: new HarmonyMethod(typeof(BmsResultsScreenPatcher), nameof(loadCompletePostfix)));
            harmony.Patch(AccessTools.Method(typeof(ResultsScreen), "onStatisticsStateChanged"),
                prefix: new HarmonyMethod(typeof(BmsResultsScreenPatcher), nameof(statisticsStatePrefix)));
            harmony.Patch(AccessTools.Method(typeof(ResultsScreen), nameof(ResultsScreen.OnBackButton)),
                prefix: new HarmonyMethod(typeof(BmsResultsScreenPatcher), nameof(onBackPrefix)));
            harmony.Patch(AccessTools.Method(AccessTools.Inner(typeof(ResultsScreen), "VerticalScrollContainer"), "Update"),
                postfix: new HarmonyMethod(typeof(BmsResultsScreenPatcher), nameof(scrollUpdatePostfix)));
            IsInstalled = true;
        }
        catch (Exception e)
        {
            harmony.UnpatchAll(harmony_id);
            disabled = true;
            BmsLogger.Error(e, "Failed to install the BMS results screen layout.");
        }
    }

    // ReSharper disable InconsistentNaming
    private static bool onSelectPrefix(ResultsScreen __instance, KeyBindingPressEvent<GlobalAction> e, ref bool __result)
    {
        if (e.Action != GlobalAction.Select || !isBms(__instance))
            return true;

        if (!e.Repeat && __instance is BmsCourseStageResultsScreen stageResults)
            stageResults.AdvanceFromEnter();
        __result = true;
        return false;
    }

    private static bool onBackPrefix(ResultsScreen __instance, ref bool __result)
    {
        if (!isBms(__instance))
            return true;

        __result = false;
        return false;
    }

    private static bool populateStatisticsPrefix(StatisticsPanel __instance) => __instance is not BmsStatisticsPanel;

    private static void loadCompletePostfix(ResultsScreen __instance)
    {
        if (!isBms(__instance))
            return;

        var list = (ScorePanelList)AccessTools.Property(typeof(ResultsScreen), "ScorePanelList").GetValue(__instance)!;
        list.PostExpandAction = null;
        list.HandleInput = false;
        list.Hide();

        if (__instance is BmsCourseResultsScreen)
            return;

        __instance.Delay(0).Schedule(() =>
        {
            if (__instance.SelectedScore.Value != null)
                ((StatisticsPanel)AccessTools.Property(typeof(ResultsScreen), "StatisticsPanel").GetValue(__instance)!).Show();
        });
    }

    // BMS owns the complete result layout, so native card detachment and fading no longer apply.
    private static bool statisticsStatePrefix(ResultsScreen __instance) => !isBms(__instance);

    private static void scrollUpdatePostfix(OsuScrollContainer __instance, Container ___content)
    {
        if (__instance.FindClosestParent<ResultsScreen>() is not { } screen || !isBms(screen))
            return;

        // The native minimum height lets charts spill over the footer at larger UI scales.
        ___content.Height = __instance.DrawHeight;
        __instance.Masking = true;
    }
    // ReSharper restore InconsistentNaming

    private static bool isBms(ResultsScreen screen) => (screen.Score ?? screen.SelectedScore.Value)?.Ruleset.ShortName == Constant.SHORT_NAME;

    private static StatisticsPanel createStatisticsPanel(ResultsScreen screen) => isBms(screen)
        ? new BmsStatisticsPanel(screen is not BmsCourseResultsScreen)
        : new StatisticsPanel();

    private static IEnumerable<CodeInstruction> replaceStatisticsPanel(IEnumerable<CodeInstruction> instructions)
    {
        var constructor = AccessTools.Constructor(typeof(StatisticsPanel));
        var count = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Newobj && Equals(instruction.operand, constructor))
            {
                instruction.opcode = OpCodes.Ldarg_0;
                instruction.operand = null;
                yield return instruction;
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BmsResultsScreenPatcher), nameof(createStatisticsPanel)));

                count++;
            }
            else
                yield return instruction;
        }

        if (count != 1)
            throw new InvalidOperationException("Expected one results statistics panel constructor.");
    }
}
