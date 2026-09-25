using System.Collections.Generic;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

internal static class BmsRateScoreSelection
{
    public static bool IsScoreEligible(IReadOnlyList<Mod> scoreMods, IReadOnlyList<Mod> selectedMods) =>
        rateFor(scoreMods) >= rateFor(selectedMods);

    private static double rateFor(IReadOnlyList<Mod> mods)
    {
        var rate = 1.0;
        foreach (var mod in mods)
        {
            rate *= mod switch
            {
                BmsModDoubleTime doubleTime => doubleTime.SpeedChange.Value,
                BmsModHalfTime halfTime => halfTime.SpeedChange.Value,
                _ => 1.0,
            };
        }

        return rate;
    }
}
