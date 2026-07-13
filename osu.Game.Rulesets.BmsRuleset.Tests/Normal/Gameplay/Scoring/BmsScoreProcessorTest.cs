using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
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
    public void TestLongNoteResultProducesBothEndpointEvents()
    {
        var (processor, source) = createLongNoteProcessor();
        var endpoints = new[]
        {
            new BmsLongNoteEndpointResult(source, BmsLongNoteEndpointKind.Head, 1012, 1.5, HitResult.Great),
            new BmsLongNoteEndpointResult(source, BmsLongNoteEndpointKind.Tail, 1490, 1.5, HitResult.Great),
        };
        var result = new BmsLongNoteJudgementResult(source, source.CreateJudgement(), endpoints) { Type = HitResult.Great };

        processor.ApplyResult(result);

        var populatedScore = new ScoreInfo();
        processor.PopulateScore(populatedScore);
        Assert.Multiple(() =>
        {
            Assert.That(processor.HitEvents, Has.Count.EqualTo(1));
            Assert.That(populatedScore.HitEvents.Select(e => e.TimeOffset), Is.EqualTo(new[] { 12, -10 }));
            Assert.That(populatedScore.HitEvents.Select(e => e.GameplayRate), Is.All.EqualTo(1.5));
            Assert.That(processor.Statistics.GetValueOrDefault(HitResult.Great), Is.EqualTo(1));
        });
    }

    [Test]
    public void TestLongNoteEndpointEventsRevertAtomicallyAndDoNotDuplicateOnReplay()
    {
        var (processor, source) = createLongNoteProcessor();
        var result = createLongNoteResult(source, headEventTime: 1012, tailEventTime: 1490);
        processor.ApplyResult(result);

        processor.RevertResult(result);

        Assert.That(processor.HitEvents, Is.Empty);

        var rewoundScore = new ScoreInfo();
        processor.PopulateScore(rewoundScore);
        Assert.That(rewoundScore.HitEvents, Is.Empty);

        processor.ApplyResult(createLongNoteResult(source, headEventTime: 1015, tailEventTime: 1490));

        var populatedScore = new ScoreInfo();
        processor.PopulateScore(populatedScore);

        Assert.Multiple(() =>
        {
            Assert.That(populatedScore.HitEvents, Has.Count.EqualTo(2));
            Assert.That(populatedScore.HitEvents.Select(e => e.TimeOffset), Is.EqualTo(new[] { 15, -10 }));
        });
    }

    [Test]
    public void TestSingleEndpointResultUsesDomainTiming()
    {
        var (processor, source) = createLongNoteProcessor(BmsLongNoteMode.ChargeNote);
        var endpoint = new BmsLongNoteEndpointResult(source, BmsLongNoteEndpointKind.Head, 988, 1, HitResult.Great);

        processor.ApplyResult(new BmsLongNoteJudgementResult(source, source.CreateJudgement(), [endpoint]) { Type = HitResult.Great });

        var hitEvent = processor.HitEvents.Single();
        Assert.Multiple(() =>
        {
            Assert.That(hitEvent.TimeOffset, Is.EqualTo(-12));
            Assert.That(hitEvent.HitObject, Is.InstanceOf<BmsNote>());
            Assert.That(hitEvent.HitObject.StartTime, Is.EqualTo(source.StartTime));
            Assert.That(processor.HitEvents.Select(e => e.TimeOffset), Does.Not.Contain(0));
        });
    }

    [Test]
    public void TestRevertingChargeTailKeepsHeadEndpoint()
    {
        var (processor, source) = createLongNoteProcessor(BmsLongNoteMode.ChargeNote);
        var head = createEndpointResult(source, BmsLongNoteEndpointKind.Head, 1012);
        var tail = createEndpointResult(source, BmsLongNoteEndpointKind.Tail, 1490);

        processor.ApplyResult(head);
        processor.ApplyResult(tail);
        processor.RevertResult(tail);

        var score = new ScoreInfo();
        processor.PopulateScore(score);
        Assert.Multiple(() =>
        {
            Assert.That(score.HitEvents, Has.Count.EqualTo(1));
            Assert.That(score.HitEvents.Single().HitObject.StartTime, Is.EqualTo(source.StartTime));
            Assert.That(score.HitEvents.Single().TimeOffset, Is.EqualTo(12));
        });
    }

    [Test]
    public void TestConcurrentLongNotesKeepEndpointSourcesSeparate()
    {
        var (processor, first) = createLongNoteProcessor();
        var second = new BmsLongNote
        {
            StartTime = 2000,
            Duration = 500,
            Column = 2,
            Beatmap = first.Beatmap,
        };
        processor.ApplyResult(createLongNoteResult(first, headEventTime: 1012, tailEventTime: 1490));
        processor.ApplyResult(createLongNoteResult(second, headEventTime: 2015, tailEventTime: 2492));

        var score = new ScoreInfo();
        processor.PopulateScore(score);
        Assert.Multiple(() =>
        {
            Assert.That(score.HitEvents.Where(e => e.HitObject.StartTime < 2000).Select(e => e.TimeOffset),
                Is.EqualTo(new[] { 12, -10 }));
            Assert.That(score.HitEvents.Where(e => e.HitObject.StartTime >= 2000).Select(e => e.TimeOffset),
                Is.EqualTo(new[] { 15, -8 }));
        });
    }

    private static BmsLongNoteJudgementResult createLongNoteResult(BmsLongNote source, double headEventTime, double tailEventTime)
        => new(source, source.CreateJudgement(),
        [
            new BmsLongNoteEndpointResult(source, BmsLongNoteEndpointKind.Head, headEventTime, 1, HitResult.Great),
            new BmsLongNoteEndpointResult(source, BmsLongNoteEndpointKind.Tail, tailEventTime, 1, HitResult.Great),
        ])
        {
            Type = HitResult.Great,
        };

    private static BmsLongNoteJudgementResult createEndpointResult(
        BmsLongNote source,
        BmsLongNoteEndpointKind kind,
        double eventTime)
    {
        var endpoint = new BmsLongNoteEndpointResult(source, kind, eventTime, 1, HitResult.Great);
        var hitObject = source.CreateSyntheticEndpoint(endpoint.ExpectedTime);
        return new BmsLongNoteJudgementResult(hitObject, hitObject.CreateJudgement(), [endpoint])
        {
            Type = HitResult.Great,
        };
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
