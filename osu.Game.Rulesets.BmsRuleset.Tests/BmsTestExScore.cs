using System.Collections.Generic;
using System.Linq;
using RhythmEngine.Core;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

// Independent legacy EX projection used by BMS behaviour and snapshot comparisons.
internal static class BmsTestExScore
{
    internal static int[] CreateProgression(IEnumerable<BmsJudgementEvent> events)
    {
        var progression = new List<int> { 0 };
        foreach (var item in events.Where(item => item.Source.IsScoring))
            progression.Add(progression[^1] + (item.Result == HitResult.Perfect ? 2 : item.Result == HitResult.Great ? 1 : 0));
        return progression.ToArray();
    }

    internal static ScoreState[] States(IEnumerable<int> values)
        => values.Select((value, index) => new ScoreState { Score = value, JudgedOpportunities = index }).ToArray();
}
