using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Rules;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

internal sealed class BmsLandmineRegistry
{
    private readonly Dictionary<BmsLandmine, BmsLandmineRuntime> landmineStates = [];
    private readonly BmsLandmineJudgementRule rule;
    private readonly IBmsJudgementOutput output;

    internal BmsLandmineRegistry(IEnumerable<BmsHitObject> objects, int column, BmsLandmineJudgementRule rule, IBmsJudgementOutput output)
    {
        this.rule = rule;
        this.output = output;
        foreach (var hitObject in objects)
        {
            if (hitObject is BmsLandmine mine && mine.Column == column)
                landmineStates.Add(mine, new BmsLandmineRuntime(mine));
        }
    }

    internal BmsLandmineState GetLandmineState(BmsLandmine note) => landmineStates[note].State;

    internal void UpdateLandmine(BmsLandmine note, in BmsFrameContext frame, bool holding)
        => rule.CheckPassing(landmineStates[note], output, frame, holding);

    internal void UpdateLandmines(in BmsFrameContext frame, bool holding)
    {
        foreach (var state in landmineStates.Values)
            rule.CheckPassing(state, output, frame, holding);
    }

    internal void RewindLandmines(double time)
    {
        foreach (var state in landmineStates.Values)
            rule.Rewind(state, time);
    }
}
