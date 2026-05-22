using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Difficulty;

/// <summary>
///     Minimal BMS difficulty calculator.
/// </summary>
/// <remarks>
///     This removes the dependency on osu!mania's calculator. It intentionally reports only basic
///     attributes until native BMS strain, scratch, LN, STOP, and soflan(SV) skills are implemented.
/// </remarks>
public class BmsDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap) : DifficultyCalculator(ruleset, beatmap)
{
    protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills, double clockRate) => new(mods, 0)
    {
        MaxCombo = beatmap.HitObjects.Count,
    };

    protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(IBeatmap beatmap, double clockRate)
    {
        var objects = beatmap.HitObjects.OrderBy(h => h.StartTime).ToList();
        var difficultyObjects = new List<DifficultyHitObject>();

        for (var i = 1; i < objects.Count; i++)
            difficultyObjects.Add(new DifficultyHitObject(objects[i], objects[i - 1], clockRate, difficultyObjects, difficultyObjects.Count));

        return difficultyObjects;
    }

    protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods, double clockRate) => [];
}
