using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal sealed partial class BmsColumnJudgementContext
{
    private readonly Dictionary<BmsLandmine, BmsLandmineRuntime> landmineStates = [];
    private readonly List<BmsLandmine> activeLandmines = [];

    private void initialiseLandmines(IEnumerable<BmsHitObject> objects)
    {
        foreach (var hitObject in objects)
        {
            if (hitObject is BmsLandmine mine && mine.Column == Index)
                landmineStates.Add(mine, new BmsLandmineRuntime(mine));
        }
    }

    internal BmsLandmineState GetLandmineState(BmsLandmine note) => landmineStates[note].State;

    internal void ActivateLandmine(BmsLandmine note) => activeLandmines.Add(note);

    internal void RemoveLandmine(BmsLandmine note) => activeLandmines.Remove(note);

    internal void UpdateLandmines(in BmsFrameContext frame, bool holding)
    {
        for (var i = 0; i < activeLandmines.Count;)
        {
            var mine = activeLandmines[i];
            Rules.Landmine.CheckPassing(landmineStates[mine], noteOutput, frame, holding);
            if (i < activeLandmines.Count && ReferenceEquals(activeLandmines[i], mine))
                i++;
        }
    }

    internal void RewindLandmines(double time)
    {
        foreach (var state in landmineStates.Values)
            Rules.Landmine.Rewind(state, time);
    }
}
