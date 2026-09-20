using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Utils;

namespace osu.Game.Rulesets.BmsRuleset.Difficulty;

public class BmsDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap) : DifficultyCalculator(ruleset, beatmap)
{
    public BmsStarRatingProcessor StarRatingProcessor { get; } = new();

    internal CancellationToken CalculationCancellationToken { get; set; }

    protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills)
    {
        CalculationCancellationToken.ThrowIfCancellationRequested();
        var clockRate = ModUtils.CalculateRateWithMods(mods);
        var bmsBeatmap = beatmap as BmsBeatmap;
        var storedDifficulty = bmsBeatmap == null ? BmsDifficultyInfo.FromOsuDifficulty(beatmap.Difficulty) : default;

        // When called from CalculateTimed, beatmap is a ProgressiveCalculationBeatmap wrapper
        // (not a BmsBeatmap), so the cast above returns null. In that case, derive TotalColumns
        // and judgement difficulty from the difficulty metadata written by the converter.
        var totalColumns = bmsBeatmap?.TotalColumns ?? storedDifficulty.KeyCount;
        var rank = bmsBeatmap?.Rank ?? storedDifficulty.Rank;

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
                var layout = bmsBeatmap?.LayoutVariant ?? BmsLayout.VariantFromTotalColumns(totalColumns);
                var exRank = bmsBeatmap != null ? bmsBeatmap.ExRank : storedDifficulty.ExRank;
                var judgementRate = exRank is { } exRankValue
                    ? BmsJudgementProfileProvider.RateForExRank(layout, exRankValue)
                    : BmsJudgementProfileProvider.RateForRank(layout, rank);
                var result = StarRatingProcessor.Compute(noteTimings, totalColumns, rank, clockRate, layout, judgementRate, CalculationCancellationToken);
                sr = result.StarRating;
            }
        }

        return new DifficultyAttributes(mods, sr)
        {
            MaxCombo = beatmap.HitObjects.Count,
        };
    }

    // Required by the base class; BMS computes attributes without Skills or their object wrappers.
    protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(IBeatmap beatmap, Mod[] mods) => [];

    protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods) => [];
}
