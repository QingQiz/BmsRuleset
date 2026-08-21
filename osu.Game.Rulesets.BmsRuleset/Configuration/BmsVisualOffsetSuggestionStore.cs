using System;
using osu.Framework.Bindables;

namespace osu.Game.Rulesets.BmsRuleset.Configuration;

internal sealed class BmsVisualOffsetSuggestionStore
{
    private const int maximum_history_count = 50;

    public IBindableList<DataPoint> History => history;

    private readonly BindableList<DataPoint> history = [];

    public double Add(double medianHitError, double visualOffset)
    {
        if (history.Count >= maximum_history_count)
            history.RemoveAt(0);

        var suggestion = Math.Clamp(
            visualOffset + medianHitError,
            BmsRulesetConfigManager.MIN_VISUAL_OFFSET,
            BmsRulesetConfigManager.MAX_VISUAL_OFFSET);

        history.Add(new DataPoint(suggestion));
        return suggestion;
    }

    public void Clear() => history.Clear();

    public readonly record struct DataPoint(double SuggestedVisualOffset);
}
