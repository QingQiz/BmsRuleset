using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Scoring;

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
    [TestCase(ScoreRank.X, 1.0)]
    [TestCase(ScoreRank.XH, 1.0)]
    [TestCase(ScoreRank.S, 8.0 / 9.0)]
    [TestCase(ScoreRank.SH, 8.0 / 9.0)]
    [TestCase(ScoreRank.A, 7.0 / 9.0)]
    [TestCase(ScoreRank.B, 6.0 / 9.0)]
    [TestCase(ScoreRank.C, 5.0 / 9.0)]
    [TestCase(ScoreRank.D, 0.0)]
    public void TestAccuracyCutoffFromRankUsesBmsDjLevelThresholds(ScoreRank rank, double expectedAccuracy)
    {
        var processor = new BmsScoreProcessor();

        Assert.That(processor.AccuracyCutoffFromRank(rank), Is.EqualTo(expectedAccuracy).Within(1e-9));
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
    public void TestRegisterEmptyPoorDoesNotBreakCombo()
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
        Assert.That(processor.Combo.Value, Is.EqualTo(1));

        processor.RegisterEmptyPoor();
        Assert.That(processor.Combo.Value, Is.EqualTo(1), "E-POOR must not break combo");
    }

    [Test]
    public void TestRegisterEmptyPoorRecordsHitEventAtPressTime()
    {
        var processor = new BmsScoreProcessor();

        processor.RegisterEmptyPoor(1234);

        Assert.That(processor.HitEvents, Has.Count.EqualTo(1));
        Assert.That(processor.HitEvents.Single().Result, Is.EqualTo(HitResult.Miss));
        Assert.That(processor.HitEvents.Single().HitObject.StartTime, Is.EqualTo(1234));
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

    [Test]
    public void TestRegisterLongNoteEndpointRecordsOnlyStatistics()
    {
        var (processor, source) = createLongNoteProcessor();
        var score = processor.TotalScore.Value;
        var combo = processor.Combo.Value;
        var statistics = processor.Statistics.ToDictionary(pair => pair.Key, pair => pair.Value);

        processor.RegisterLongNoteEndpoint(source, source.StartTime, 1012, 1.5, HitResult.Great);

        var populatedScore = new ScoreInfo();
        processor.PopulateScore(populatedScore);
        var hitEvent = populatedScore.HitEvents.Single();
        Assert.Multiple(() =>
        {
            Assert.That(hitEvent.TimeOffset, Is.EqualTo(12));
            Assert.That(hitEvent.HitObject, Is.InstanceOf<BmsNote>());
            Assert.That(hitEvent.HitObject.StartTime, Is.EqualTo(source.StartTime));
            Assert.That(hitEvent.GameplayRate, Is.EqualTo(1.5));
            Assert.That(processor.HitEvents, Is.Empty);
            Assert.That(processor.TotalScore.Value, Is.EqualTo(score));
            Assert.That(processor.Combo.Value, Is.EqualTo(combo));
            Assert.That(processor.Statistics, Is.EquivalentTo(statistics));
        });
    }

    [Test]
    public void TestLongNoteStatisticsEventDoesNotCorruptRewindStackOrDuplicateOnReplay()
    {
        var (processor, source) = createLongNoteProcessor();
        var first = new BmsNote { StartTime = 900, Column = 1, Beatmap = source.Beatmap };
        var last = new BmsNote { StartTime = 1100, Column = 1, Beatmap = source.Beatmap };
        var firstResult = new JudgementResult(first, first.CreateJudgement()) { Type = HitResult.Great };
        var lastResult = new JudgementResult(last, last.CreateJudgement()) { Type = HitResult.Great };

        processor.ApplyResult(firstResult);
        processor.RegisterLongNoteEndpoint(source, source.StartTime, 1012, 1, HitResult.Great);
        processor.ApplyResult(lastResult);

        Assert.That(processor.HitEvents, Has.Count.EqualTo(2));

        processor.RevertResult(lastResult);
        processor.RevertResult(firstResult);
        processor.RemoveLongNoteEndpoint(source);

        Assert.That(processor.HitEvents, Is.Empty);

        var rewoundScore = new ScoreInfo();
        processor.PopulateScore(rewoundScore);
        Assert.That(rewoundScore.HitEvents, Is.Empty);

        processor.ApplyResult(new JudgementResult(first, first.CreateJudgement()) { Type = HitResult.Great });
        processor.RegisterLongNoteEndpoint(source, source.StartTime, 1015, 1, HitResult.Great);
        processor.ApplyResult(new JudgementResult(last, last.CreateJudgement()) { Type = HitResult.Great });

        var populatedScore = new ScoreInfo();
        processor.PopulateScore(populatedScore);

        Assert.Multiple(() =>
        {
            Assert.That(populatedScore.HitEvents, Has.Count.EqualTo(3));
            Assert.That(populatedScore.HitEvents.Count(e => e.HitObject.StartTime == source.StartTime), Is.EqualTo(1));
            Assert.That(populatedScore.HitEvents.Single(e => e.HitObject.StartTime == source.StartTime).TimeOffset, Is.EqualTo(15));
        });
    }

    [Test]
    public void TestPreparedLongNoteEndpointReplacesAmbiguousFrameworkEvent()
    {
        var (processor, source) = createLongNoteProcessor(BmsLongNoteMode.ChargeNote);
        processor.PrepareLongNoteEndpoint(source, source.StartTime, 988);

        processor.ApplyResult(new JudgementResult(source, source.CreateJudgement()) { Type = HitResult.Great });

        var hitEvent = processor.HitEvents.Single();
        Assert.Multiple(() =>
        {
            Assert.That(hitEvent.TimeOffset, Is.EqualTo(-12));
            Assert.That(hitEvent.HitObject, Is.InstanceOf<BmsNote>());
            Assert.That(hitEvent.HitObject.StartTime, Is.EqualTo(source.StartTime));
            Assert.That(processor.HitEvents.Select(e => e.TimeOffset), Does.Not.Contain(0));
        });
    }

    private static (BmsScoreProcessor processor, BmsLongNote source) createLongNoteProcessor(BmsLongNoteMode mode = BmsLongNoteMode.LongNote)
    {
        var source = new BmsLongNote { StartTime = 1000, Duration = 500, Column = 1 };
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            LockedLongNoteMode = mode,
            HitObjects = { source },
        };
        source.Beatmap = beatmap;
        var processor = new BmsScoreProcessor();
        processor.ApplyBeatmap(beatmap);
        return (processor, source);
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
