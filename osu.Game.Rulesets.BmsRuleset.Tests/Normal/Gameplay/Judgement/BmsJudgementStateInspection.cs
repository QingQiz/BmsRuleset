#nullable enable
using System;
using System.Collections;
using System.Reflection;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

// Lifecycle tests verify state identity across pool bindings without exposing mutable state to gameplay callers.
internal static class BmsJudgementStateInspection
{
    private const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static object? GetRuntime(BmsColumnJudgementRuntime column, BmsHitObject note)
    {
        if (note is not BmsLongNote) return null;
        var holds = GetHolds(column);
        var bindings = (Array)typeof(BmsHoldRegistry).GetField("bindings", instance)!.GetValue(holds)!;
        var indices = (int[])typeof(BmsHoldRegistry).GetField("bindingIndices", instance)!.GetValue(holds)!;
        var binding = bindings.GetValue(indices[column.GetNoteSlot(note)])!;
        var operations = binding.GetType().GetProperty("Operations", instance)!.GetValue(binding)!;
        var index = (int)binding.GetType().GetProperty("StateIndex", instance)!.GetValue(binding)!;
        var states = (IList)operations.GetType().GetField("states", instance)!.GetValue(operations)!;
        return states[index];
    }

    internal static BmsHoldRegistry GetHolds(BmsColumnJudgementRuntime column)
        => (BmsHoldRegistry)typeof(BmsColumnJudgementRuntime).GetField("holds", instance)!.GetValue(column)!;
}
