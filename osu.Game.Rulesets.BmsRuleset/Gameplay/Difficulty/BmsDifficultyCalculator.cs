using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Utils;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Difficulty;

public class BmsDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap) : DifficultyCalculator(ruleset, beatmap)
{
    // Increment when model, conversion or mod changes affect calculated star ratings.
    public const int DIFFICULTY_VERSION = 2;

    public override int Version => DIFFICULTY_VERSION;

    public BmsStarRatingProcessor StarRatingProcessor { get; } = new();

    internal CancellationToken CalculationCancellationToken { get; set; }

    private Dictionary<BmsHitObject, int> scoringOpportunities = [];
    private double hitLeniency;

    protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills)
    {
        CalculationCancellationToken.ThrowIfCancellationRequested();
        var clockRate = ModUtils.CalculateRateWithMods(mods);
        // The base calculator retains the full playable chart when passing a progressive wrapper.
        var bmsBeatmap = (BmsBeatmap)Beatmap;

        var noteTimings = beatmap.HitObjects
            .OfType<BmsHitObject>()
            .Where(scoringOpportunities.ContainsKey)
            .Select(h => new BmsNoteTiming(h.Column, h.StartTime, h is BmsLongNote ln ? ln.EndTime : h.StartTime))
            .ToList();

        double sr = 0;

        if (noteTimings.Count > 0)
        {
            var result = StarRatingProcessor.Compute(noteTimings, bmsBeatmap.TotalColumns, bmsBeatmap.Rank, clockRate, bmsBeatmap.LayoutVariant,
                cancellationToken: CalculationCancellationToken, hitLeniency: hitLeniency);
            sr = result.StarRating;
        }

        return new DifficultyAttributes(mods, sr)
        {
            MaxCombo = beatmap.HitObjects.OfType<BmsHitObject>().Sum(note => scoringOpportunities.GetValueOrDefault(note)),
        };
    }

    // Required by the base class; BMS computes attributes without Skills or their object wrappers.
    protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(IBeatmap beatmap, Mod[] mods) => [];

    protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods)
    {
        CalculationCancellationToken.ThrowIfCancellationRequested();
        var bmsBeatmap = (BmsBeatmap)beatmap;
        var profile = GameplayProfile.FromMods(mods);
        var compiled = new BmsEngineChart(bmsBeatmap, profile);
        scoringOpportunities = compiled.Chart.ScorePlan.Opportunities.GroupBy(opportunity => compiled.Objects[opportunity.NoteId])
            .ToDictionary(group => group.Key, group => group.Count());
        hitLeniency = scoringOpportunities.Count == 0 ? 0 : BmsHitLeniency.FromGreatWindow(profile.RatingWindowFor(bmsBeatmap, compiled));
        return [];
    }
}
