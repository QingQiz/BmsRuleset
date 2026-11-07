using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

internal static class BmsTestJudgementSelector
{
    internal static BmsJudgementSelection SelectPress(
        BmsLayoutVariant layout,
        int column,
        IEnumerable<BmsJudgementCandidate> candidates,
        double inputTime,
        BmsJudgementAlgorithm? algorithm = BmsJudgementAlgorithm.Combo)
    {
        // Fixtures may list notes in arbitrary order; gameplay supplies its ordered index.
        var ordered = candidates.OrderBy(candidate => candidate.StartTime).ThenBy(candidate => candidate.Column).ToArray();
        var search = new BmsJudgementSelector.PressSearch(layout, column, inputTime, algorithm);
        for (var i = 0; i < ordered.Length; i++)
        {
            if (!search.Consider(ordered[i], i))
                break;
        }

        return search.Selection;
    }
}
