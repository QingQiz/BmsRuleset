using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
public class BmsScoreProcessorTest
{
    [Test]
    public void TestScoreProcessorBaseScoreIsPgreatTwo()
    {
        var processor = new BmsScoreProcessor();

        Assert.That(processor.GetBaseScoreForResult(HitResult.Perfect), Is.EqualTo(2));
        Assert.That(processor.GetBaseScoreForResult(HitResult.Great), Is.EqualTo(1));
        Assert.That(processor.GetBaseScoreForResult(HitResult.Good), Is.EqualTo(0));
        Assert.That(processor.GetBaseScoreForResult(HitResult.Ok), Is.EqualTo(0));
        Assert.That(processor.GetBaseScoreForResult(HitResult.Meh), Is.EqualTo(0));
        Assert.That(processor.GetBaseScoreForResult(HitResult.Miss), Is.EqualTo(0));
    }

    [Test]
    [TestCase(1.0, ScoreRank.X)]
    [TestCase(8.0 / 9.0, ScoreRank.S)]
    [TestCase(7.0 / 9.0, ScoreRank.A)]
    [TestCase(6.0 / 9.0, ScoreRank.B)]
    [TestCase(5.0 / 9.0, ScoreRank.C)]
    [TestCase(4.0 / 9.0, ScoreRank.D)]
    public void TestScoreProcessorRankFromAccuracy(double accuracy, ScoreRank expectedRank)
    {
        var processor = new BmsScoreProcessor();

        var results = new Dictionary<HitResult, int>();
        if (accuracy < 1.0)
            results[HitResult.Meh] = 1;

        var rank = processor.RankFromScore(accuracy, results);

        Assert.That(rank, Is.EqualTo(expectedRank));
    }

    [Test]
    public void TestScoreProcessorRankXRequiresNoNonPgreat()
    {
        var processor = new BmsScoreProcessor();
        var resultsWithGreat = new Dictionary<HitResult, int> { [HitResult.Great] = 1 };

        var rank = processor.RankFromScore(1.0, resultsWithGreat);

        Assert.That(rank, Is.Not.EqualTo(ScoreRank.X));
    }

    [Test]
    public void TestScoreProcessorBadBreaksCombo()
    {
        var processor = new BmsScoreProcessor();
        var beatmap = createThreeNoteBeatmap();
        processor.ApplyBeatmap(beatmap);

        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
            { Type = HitResult.Perfect });
        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[1], beatmap.HitObjects[1].CreateJudgement())
            { Type = HitResult.Perfect });
        Assert.That(processor.Combo.Value, Is.EqualTo(2));

        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[2], beatmap.HitObjects[2].CreateJudgement())
            { Type = HitResult.Ok });
        Assert.That(processor.Combo.Value, Is.EqualTo(0));
    }

    [Test]
    public void TestScoreProcessorPoorBreaksCombo()
    {
        var processor = new BmsScoreProcessor();
        var beatmap = createThreeNoteBeatmap();
        processor.ApplyBeatmap(beatmap);

        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
            { Type = HitResult.Perfect });
        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[1], beatmap.HitObjects[1].CreateJudgement())
            { Type = HitResult.Perfect });
        Assert.That(processor.Combo.Value, Is.EqualTo(2));

        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[2], beatmap.HitObjects[2].CreateJudgement())
            { Type = HitResult.Meh });
        Assert.That(processor.Combo.Value, Is.EqualTo(0));
    }

    [Test]
    public void TestScoreProcessorRankNeverF()
    {
        var processor = new BmsScoreProcessor();
        var results = new Dictionary<HitResult, int> { [HitResult.Meh] = 100 };

        var rank = processor.RankFromScore(0.0, results);

        Assert.That(rank, Is.Not.EqualTo(ScoreRank.F));
        Assert.That(rank, Is.EqualTo(ScoreRank.D));
    }

    [Test]
    public void TestRegisterEmptyPoorIncrementsStatisticsCounter()
    {
        var processor = new BmsScoreProcessor();

        Assert.That(processor.Statistics.GetValueOrDefault(HitResult.Miss), Is.EqualTo(0));

        processor.RegisterEmptyPoor();
        Assert.That(processor.Statistics.GetValueOrDefault(HitResult.Miss), Is.EqualTo(1));

        processor.RegisterEmptyPoor();
        processor.RegisterEmptyPoor();
        Assert.That(processor.Statistics.GetValueOrDefault(HitResult.Miss), Is.EqualTo(3));
    }

    [Test]
    public void TestRegisterEmptyPoorBreaksCombo()
    {
        var processor = new BmsScoreProcessor();
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects = { new BmsHitObject { StartTime = 1000, Column = 1 } },
        };
        processor.ApplyBeatmap(beatmap);

        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
            { Type = HitResult.Perfect });
        Assert.That(processor.Combo.Value, Is.GreaterThan(0));

        processor.RegisterEmptyPoor();
        Assert.That(processor.Combo.Value, Is.EqualTo(0));
    }

    [Test]
    public void TestEmptyPoorDoesNotBlockXRank()
    {
        var processor = new BmsScoreProcessor();
        var results = new Dictionary<HitResult, int>
        {
            [HitResult.Miss] = 5,
        };

        var rank = processor.RankFromScore(1.0, results);

        Assert.That(rank, Is.EqualTo(ScoreRank.X));
    }

    private static BmsBeatmap createThreeNoteBeatmap() => new()
    {
        LayoutVariant = BmsLayoutVariant.Bme7K,
        TotalColumns = 8,
        HitObjects =
        {
            new BmsHitObject { StartTime = 1000, Column = 1 },
            new BmsHitObject { StartTime = 2000, Column = 2 },
            new BmsHitObject { StartTime = 3000, Column = 3 },
        },
    };
}
