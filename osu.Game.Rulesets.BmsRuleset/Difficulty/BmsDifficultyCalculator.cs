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
    public BmsStarRatingProcessorV3 StarRatingProcessor { get; } = new();

    protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills, double clockRate)
    {
        var bmsBeatmap = beatmap as BmsBeatmap;

        // When called from CalculateTimed, beatmap is a ProgressiveCalculationBeatmap wrapper
        // (not a BmsBeatmap), so the cast above returns null. In that case, derive TotalColumns
        // from the difficulty metadata (CircleSize was set to TotalColumns by the converter).
        var totalColumns = bmsBeatmap?.TotalColumns ?? BmsDifficultyInfo.GetKeyCount(beatmap.Difficulty);
        var rank = bmsBeatmap?.Rank ?? 2;

        var noteTimings = beatmap.HitObjects
            .OfType<BmsHitObject>()
            .Where(h => h is not BmsLandmine)
            .Select(h => new BmsNoteTiming(h.Column, h.StartTime, h is BmsLongNote ln ? ln.EndTime : h.StartTime))
            .ToList();

        double sr = 0;

        if (noteTimings.Count > 0)
        {
            // When Auto Scratch is active, exclude scratch column notes from difficulty calculation.
            if (bmsBeatmap != null && mods.Any(m => m is BmsModAutoScratch))
                noteTimings = noteTimings.Where(n => !BmsLayout.IsScratchColumn(n.Column, bmsBeatmap.LayoutVariant)).ToList();

            if (noteTimings.Count > 0)
            {
                var result = StarRatingProcessor.Compute(noteTimings, totalColumns, rank, clockRate);
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
