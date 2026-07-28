using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Result;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Result;

[TestFixture]
public class BmsHitOffsetStatisticTest
{
    [Test]
    public void TestStatisticsAreGroupedByKey()
    {
        var statistics = BmsHitOffsetStatistic.CreateStatistics(
            createBeatmap(BmsLayoutVariant.Bms5K),
            [
                new HitEvent(-12, 1, HitResult.Great, new BmsNote { Column = 0, StartTime = 1000 }, null, null),
                new HitEvent(8, 1, HitResult.Perfect, new BmsNote { Column = 0, StartTime = 1500 }, null, null),
                new HitEvent(30, 1, HitResult.Good, new BmsNote { Column = 1, StartTime = 2000 }, null, null),
                new HitEvent(-20, 1, HitResult.Ok, new BmsNote { Column = 1, StartTime = 2500 }, null, null),
                new HitEvent(500, 1, HitResult.Meh, new BmsNote { Column = 0, StartTime = 2750 }, null, null),
                new HitEvent(100, 1, HitResult.Miss, new BmsNote { Column = 0, StartTime = 3000 }, null, null),
                new HitEvent(0, 1, HitResult.Miss, new HitObject { StartTime = 3250 }, null, null),
                new HitEvent(5, 1, HitResult.Great, new HitObject { StartTime = 3500 }, null, null),
            ]);

        Assert.That(statistics.Overall.Count, Is.EqualTo(7));
        Assert.That(statistics.Overall.AverageOffset, Is.EqualTo(1.5).Within(0.001));
        Assert.That(statistics.Overall.StandardDeviation, Is.EqualTo(19.358).Within(0.001));
        Assert.That(statistics.Overall.FastCount, Is.EqualTo(2));
        Assert.That(statistics.Overall.SlowCount, Is.EqualTo(2));
        Assert.That(statistics.Overall.BinSize, Is.EqualTo(1));
        Assert.That(statistics.Overall.BinsByResult.Values, Has.All.Length.EqualTo(101));
        Assert.That(statistics.Overall.BinsByResult.Values.SelectMany(b => b).Sum(), Is.EqualTo(7));
        Assert.That(statistics.Overall.BinsByResult[HitResult.Meh][^1], Is.EqualTo(1));
        Assert.That(statistics.Overall.BinsByResult[HitResult.Miss][0], Is.EqualTo(2));

        Assert.That(statistics.Keys.Select(k => k.Label), Is.EqualTo(["Scratch", "Key 1", "Key 2", "Key 3", "Key 4", "Key 5"]));
        Assert.That(statistics.Keys[0].Summary.Count, Is.EqualTo(4));
        Assert.That(statistics.Keys[0].Summary.AverageOffset, Is.EqualTo(-2).Within(0.001));
        Assert.That(statistics.Keys[0].Summary.StandardDeviation, Is.EqualTo(10).Within(0.001));
        Assert.That(statistics.Keys[1].Summary.Count, Is.EqualTo(2));
        Assert.That(statistics.Keys[1].Summary.AverageOffset, Is.EqualTo(5).Within(0.001));
        Assert.That(statistics.Keys[1].Summary.StandardDeviation, Is.EqualTo(25).Within(0.001));
        Assert.That(statistics.Keys[2].Summary.Count, Is.Zero);
        Assert.That(statistics.Keys[2].Summary.StandardDeviation, Is.Zero);
    }

    [Test]
    public void TestPoorUsesEdgeMatchingTimingDirection()
    {
        var statistics = BmsHitOffsetStatistic.CreateStatistics(
            createBeatmap(BmsLayoutVariant.Bms5K),
            [
                new HitEvent(-500, 1, HitResult.Meh, new BmsNote { Column = 0, StartTime = 1000 }, null, null),
                new HitEvent(500, 1, HitResult.Meh, new BmsNote { Column = 0, StartTime = 2000 }, null, null),
            ]);

        var poorBins = statistics.Overall.BinsByResult[HitResult.Meh];

        Assert.Multiple(() =>
        {
            Assert.That(poorBins[0], Is.EqualTo(1));
            Assert.That(poorBins[^1], Is.EqualTo(1));
        });
    }

    [Test]
    public void TestPmsStatisticsHaveNoScratch()
    {
        var statistics = BmsHitOffsetStatistic.CreateStatistics(
            createBeatmap(BmsLayoutVariant.Pms9K),
            [
                new HitEvent(12, 1, HitResult.Great, new BmsNote { Column = 0, StartTime = 1000 }, null, null),
                new HitEvent(-8, 1, HitResult.Perfect, new BmsNote { Column = 8, StartTime = 1500 }, null, null),
            ]);

        Assert.That(statistics.Keys.Select(k => k.Label), Is.EqualTo(Enumerable.Range(1, 9).Select(i => $"Key {i}")));
        Assert.That(statistics.Keys[0].Summary.Count, Is.EqualTo(1));
        Assert.That(statistics.Keys[8].Summary.Count, Is.EqualTo(1));
        Assert.That(statistics.Keys.Any(k => k.Label == "Scratch"), Is.False);
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void TestLongNoteEndpointsKeepRealOffsets(BmsLongNoteMode mode)
    {
        var longNote = new BmsLongNote { Column = 1, StartTime = 1000, Duration = 500 };
        var beatmap = createBeatmap(BmsLayoutVariant.Bms5K);
        beatmap.LockedLongNoteMode = mode;
        beatmap.HitObjects.Add(longNote);
        longNote.Beatmap = beatmap;

        var processor = new BmsScoreProcessor();
        processor.ApplyBeatmap(beatmap);

        if (mode == BmsLongNoteMode.LongNote)
        {
            var endpoints = new[]
            {
                new BmsLongNoteEndpointResult(longNote, BmsLongNoteEndpointKind.Head, 987, 1, HitResult.Great),
                new BmsLongNoteEndpointResult(longNote, BmsLongNoteEndpointKind.Tail, 1519, 1, HitResult.Great),
            };
            processor.ApplyResult(new BmsLongNoteJudgementResult(longNote, longNote.CreateJudgement(), endpoints) { Type = HitResult.Great });
        }
        else
        {
            processor.ApplySyntheticLongNoteEndpoint(
                new BmsLongNoteEndpointResult(longNote, BmsLongNoteEndpointKind.Head, 987, 1, HitResult.Great));
            processor.ApplySyntheticLongNoteEndpoint(
                new BmsLongNoteEndpointResult(longNote, BmsLongNoteEndpointKind.Tail, 1519, 1, HitResult.Great));
        }

        var score = new ScoreInfo();
        processor.PopulateScore(score);
        var statistics = BmsHitOffsetStatistic.CreateStatistics(beatmap, score.HitEvents);

        Assert.Multiple(() =>
        {
            Assert.That(score.HitEvents.Select(e => e.TimeOffset), Is.EqualTo(new[] { -13, 19 }));
            Assert.That(score.HitEvents.Select(e => e.HitObject.StartTime), Is.EqualTo(new[] { 1000, 1500 }));
            Assert.That(score.HitEvents.Select(e => e.TimeOffset), Does.Not.Contain(0));
            Assert.That(statistics.Overall.Count, Is.EqualTo(2));
            Assert.That(statistics.Overall.BinsByResult.Values.SelectMany(b => b).Sum(), Is.EqualTo(2));
        });
    }

    private static BmsBeatmap createBeatmap(BmsLayoutVariant variant) => new()
    {
        LayoutVariant = variant,
        TotalColumns = BmsLayout.GetTotalColumns(variant),
    };
}
