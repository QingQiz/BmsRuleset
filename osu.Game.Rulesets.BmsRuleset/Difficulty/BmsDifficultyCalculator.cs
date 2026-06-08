using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Difficulty;

public class BmsDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap) : DifficultyCalculator(ruleset, beatmap)
{
    public BmsStarRatingProcessor StarRatingProcessor { get; } = new();

    protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills, double clockRate)
    {
        var bmsBeatmap = beatmap as BmsBeatmap;

        var totalColumns = bmsBeatmap?.TotalColumns ?? 6;
        var rank = bmsBeatmap?.Rank ?? 2;

        var hitObjects = beatmap.HitObjects.OfType<BmsHitObject>().ToList();

        double sr = 0;

        if (hitObjects.Count > 0)
        {
            var effectiveClockRate = clockRate;

            // When Auto Scratch is active, exclude scratch column notes from difficulty calculation.
            if (bmsBeatmap != null && mods.Any(m => m is BmsModAutoScratch))
                hitObjects = hitObjects.Where(h => !BmsLayout.IsScratchColumn(h.Column, bmsBeatmap.LayoutVariant)).ToList();

            if (hitObjects.Count > 0)
            {
                var result = StarRatingProcessor.Compute(hitObjects, totalColumns, rank, effectiveClockRate);
                sr = result.StarRating;
            }
        }

        return new DifficultyAttributes(mods, sr)
        {
            MaxCombo = beatmap.HitObjects.Count,
        };
    }

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
