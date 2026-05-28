using System;
using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual.Gameplay;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
public partial class TestSceneScoring : ScoringTestScene
{
    protected override IBeatmap CreateBeatmap(int maxCombo)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Total = 200,
        };

        for (var i = 0; i < maxCombo; i++)
            beatmap.HitObjects.Add(new BmsHitObject { Column = i % 8 });

        return beatmap;
    }

    protected override IScoringAlgorithm CreateScoreV1(IReadOnlyList<Mod> selectedMods) => new BmsScoreV1(MaxCombo.Value, selectedMods);

    protected override IScoringAlgorithm CreateScoreV2(int maxCombo, IReadOnlyList<Mod> selectedMods) => new BmsScoreV2(maxCombo, selectedMods);

    protected override ProcessorBasedScoringAlgorithm CreateScoreAlgorithm(IBeatmap beatmap, ScoringMode mode, IReadOnlyList<Mod> mods)
        => new BmsProcessorBasedScoringAlgorithm(beatmap, mode, mods);

    [Test]
    public void TestBasicScenarios()
    {
        AddStep("set max combo to 100", () => MaxCombo.Value = 100);
        AddStep("set perfect score", () =>
        {
            NonPerfectLocations.Clear();
            MissLocations.Clear();
        });
        AddStep("set score with POORs", () =>
        {
            NonPerfectLocations.Clear();
            MissLocations.Clear();
            MissLocations.AddRange(new[] { 24d, 49 });
        });
        AddStep("set score with POORs and BADs", () =>
        {
            NonPerfectLocations.Clear();
            MissLocations.Clear();
            NonPerfectLocations.AddRange(new[] { 9d, 19, 29, 39, 59, 69, 79, 89, 99 });
            MissLocations.AddRange(new[] { 24d, 49 });
        });
    }

    private class BmsScoreV1 : IScoringAlgorithm
    {
        private double exScore;

        public BmsScoreV1(int maxCombo, IReadOnlyList<Mod> selectedMods)
        {
            maxExScore = maxCombo * 2.0;
        }

        private double maxExScore { get; }

        public void ApplyHit() => exScore += 2;

        public void ApplyNonPerfect()
        {
        }

        public void ApplyMiss()
        {
        }

        public long TotalScore => (long)Math.Round(1_000_000 * exScore / maxExScore);
    }

    private class BmsScoreV2 : IScoringAlgorithm
    {
        private readonly double maxExScore;
        private double exScore;

        public BmsScoreV2(int maxCombo, IReadOnlyList<Mod> selectedMods)
        {
            maxExScore = maxCombo * 2.0;
        }

        public void ApplyHit() => exScore += 2;

        public void ApplyNonPerfect()
        {
        }

        public void ApplyMiss()
        {
        }

        public long TotalScore => (long)Math.Round(1_000_000 * exScore / maxExScore);
    }

    private class BmsProcessorBasedScoringAlgorithm : ProcessorBasedScoringAlgorithm
    {
        public BmsProcessorBasedScoringAlgorithm(IBeatmap beatmap, ScoringMode mode, IReadOnlyList<Mod> selectedMods)
            : base(beatmap, mode, selectedMods)
        {
        }

        protected override ScoreProcessor CreateScoreProcessor() => new BmsScoreProcessor();

        protected override JudgementResult CreatePerfectJudgementResult() => new(new BmsHitObject(), new BmsJudgement()) { Type = HitResult.Perfect };

        protected override JudgementResult CreateNonPerfectJudgementResult() => new(new BmsHitObject(), new BmsJudgement()) { Type = HitResult.Ok };

        protected override JudgementResult CreateMissJudgementResult() => new(new BmsHitObject(), new BmsJudgement()) { Type = HitResult.Meh };
    }
}
