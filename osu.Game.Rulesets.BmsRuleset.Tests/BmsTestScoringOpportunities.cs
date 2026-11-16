using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

internal static class BmsTestScoringOpportunities
{
    internal static int CountScoringNotes(this BmsBeatmap beatmap)
        => new BmsEngineChart(beatmap).Chart.ScorePlan.Opportunities.Length;
}
