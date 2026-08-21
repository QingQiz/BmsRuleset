using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Replays;

internal static class BmsBranchReplayState
{
    public static void EnsureBranchReplayMod(Score score, IReadOnlyList<BmsBranchDecision> branchDecisions)
    {
        if (branchDecisions.Count == 0 || score.ScoreInfo.Mods.OfType<BmsModBranchReplay>().Any())
            return;

        var branchReplayMod = new BmsModBranchReplay
        {
            Decisions = { Value = BmsChartParser.SerialiseBranchDecisions(branchDecisions) },
        };

        score.ScoreInfo.Mods = [.. score.ScoreInfo.Mods, branchReplayMod];
    }
}
