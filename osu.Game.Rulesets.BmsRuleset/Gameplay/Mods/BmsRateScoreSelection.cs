using System.Collections.Generic;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;

internal sealed class BmsRateScoreSelection : IScoreSelectionRule
{
    public static readonly BmsRateScoreSelection INSTANCE = new();

    private BmsRateScoreSelection()
    {
    }

    public bool IsScoreEligible(IReadOnlyList<Mod> scoreMods, IReadOnlyList<Mod> selectedMods) =>
        rateFor(scoreMods) >= rateFor(selectedMods);

    private static double rateFor(IReadOnlyList<Mod> mods)
    {
        var rate = 1.0;
        foreach (var mod in mods)
        {
            if (mod is ModRateAdjust rateAdjust)
                rate *= rateAdjust.SpeedChange.Value;
        }

        return rate;
    }
}
