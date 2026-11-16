using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Result.Statistic;

[TestFixture]
public class BmsHitScatterStatisticTest
{

    private static BmsBeatmap createBeatmap(BmsLayoutVariant variant) => new()
    {
        LayoutVariant = variant,
        TotalColumns = BmsLayout.GetTotalColumns(variant),
    };

    [Test]
    public void TestEmptyPoorIsPinnedToUpperBoundary()
    {
        var data = BmsHitScatterStatistic.CreateData(BmsGameplayProfile.INSTANCE, [
            new HitEvent(0, 1, HitResult.Miss, new HitObject { StartTime = 1000 }, null, null),
        ]);

        Assert.That(data.OffsetRange, Is.EqualTo(150));
        Assert.That(data.Points.Single().DisplayOffset(data.OffsetRange), Is.EqualTo(-150));
    }

    [Test]
    public void TestOffsetRangeHasReadableTicks()
    {
        var data = BmsHitScatterStatistic.CreateData(BmsGameplayProfile.INSTANCE, [
            new HitEvent(-187, 1, HitResult.Good, new BmsNote { StartTime = 1000 }, null, null),
            new HitEvent(12, 1, HitResult.Perfect, new BmsNote { StartTime = 2000 }, null, null),
        ]);

        Assert.That(data.OffsetRange, Is.EqualTo(200));
        Assert.That(data.OffsetTicks, Is.EqualTo([-200, -100, 0, 100, 200]));
    }

    [Test]
    public void TestPointsIncludeEmptyPoorAndBasicBmsHits()
    {
        var data = BmsHitScatterStatistic.CreateData(BmsGameplayProfile.INSTANCE, [
            new HitEvent(-12, 1, HitResult.Perfect, new BmsNote { StartTime = 1000 }, null, null),
            new HitEvent(28, 1, HitResult.Great, new BmsNote { StartTime = 2000 }, null, null),
            new HitEvent(90, 1, HitResult.Miss, new BmsNote { StartTime = 3000 }, null, null),
            new HitEvent(0, 1, HitResult.Miss, new HitObject { StartTime = 3500 }, null, null),
            new HitEvent(0, 1, HitResult.Meh, new BmsLandmine { StartTime = 4000 }, null, null),
            new HitEvent(4, 1, HitResult.Good, new HitObject { StartTime = 4500 }, null, null),
        ]);

        Assert.That(data.Points.Select(p => p.Result), Is.EqualTo(
        [
            HitResult.Perfect,
            HitResult.Great,
            HitResult.Miss,
            HitResult.Miss,
        ]));

        Assert.That(data.Points.Select(p => p.Time), Is.EqualTo([1000, 2000, 3000, 3500]));
        Assert.That(data.Points.Select(p => p.DisplayOffset(data.OffsetRange)), Is.EqualTo([-12, 28, -150, -150]));
        Assert.That(data.Duration, Is.EqualTo(3500));
    }

    [Test]
    public void TestPoorAndEmptyPoorDoNotExpandOffsetRange()
    {
        var data = BmsHitScatterStatistic.CreateData(BmsGameplayProfile.INSTANCE, [
            new HitEvent(-5000, 1, HitResult.Meh, new BmsNote { StartTime = 1000 }, null, null),
            new HitEvent(5000, 1, HitResult.Meh, new BmsNote { StartTime = 2000 }, null, null),
            new HitEvent(0, 1, HitResult.Miss, new HitObject { StartTime = 3000 }, null, null),
            new HitEvent(12, 1, HitResult.Perfect, new BmsNote { StartTime = 4000 }, null, null),
        ]);

        Assert.That(data.OffsetRange, Is.EqualTo(150));
        Assert.That(data.Points.Select(p => p.DisplayOffset(data.OffsetRange)), Is.EqualTo([-150, 150, -150, 12]));
        Assert.That(data.OffsetTicks, Is.EqualTo([-150, -75, 0, 75, 150]));
    }

    [Test]
    public void TestPoorWithinRangeKeepsOffset()
    {
        var data = BmsHitScatterStatistic.CreateData(BmsGameplayProfile.INSTANCE, [
            new HitEvent(-187, 1, HitResult.Good, new BmsNote { StartTime = 1000 }, null, null),
            new HitEvent(175, 1, HitResult.Meh, new BmsNote { StartTime = 2000 }, null, null),
        ]);

        Assert.That(data.OffsetRange, Is.EqualTo(200));
        Assert.That(data.Points[1].Offset, Is.EqualTo(175));
    }

    [Test]
    public void TestStatisticsAreGroupedByKeyLikeHitOffsetStatistic()
    {
        var statistics = BmsHitScatterStatistic.CreateStatistics(BmsGameplayProfile.INSTANCE,
            createBeatmap(BmsLayoutVariant.Bms5K),
            [
                new HitEvent(-12, 1, HitResult.Great, new BmsNote { Column = 0, StartTime = 1000 }, null, null),
                new HitEvent(8, 1, HitResult.Perfect, new BmsNote { Column = 0, StartTime = 1500 }, null, null),
                new HitEvent(30, 1, HitResult.Good, new BmsNote { Column = 1, StartTime = 2000 }, null, null),
                new HitEvent(-20, 1, HitResult.Ok, new BmsNote { Column = 1, StartTime = 2500 }, null, null),
                new HitEvent(100, 1, HitResult.Miss, new HitObject { StartTime = 3000 }, null, null),
                new HitEvent(5, 1, HitResult.Great, new HitObject { StartTime = 3500 }, null, null),
            ]);

        Assert.That(statistics.Overall.Points.Select(p => p.DisplayOffset(statistics.Overall.OffsetRange)), Is.EqualTo([-12, 8, 30, -20, -150]));
        Assert.That(statistics.Keys.Select(k => k.Label), Is.EqualTo(new[] { BmsStrings.Scratch }.Concat(Enumerable.Range(1, 5).Select(BmsStrings.Key))));
        Assert.That(statistics.Keys[0].Data.Points.Select(p => p.Offset), Is.EqualTo([-12, 8]));
        Assert.That(statistics.Keys[1].Data.Points.Select(p => p.Offset), Is.EqualTo([30, -20]));
        Assert.That(statistics.Keys[2].Data.Points, Is.Empty);
    }

    [Test]
    public void TestTimedOffsetRangeIsCapped()
    {
        var data = BmsHitScatterStatistic.CreateData(BmsGameplayProfile.INSTANCE, [
            new HitEvent(-5000, 1, HitResult.Good, new BmsNote { StartTime = 1000 }, null, null),
        ]);

        Assert.That(data.OffsetRange, Is.EqualTo(300));
        Assert.That(data.OffsetTicks, Is.EqualTo([-300, -150, 0, 150, 300]));
    }

    [Test]
    public void TestCombinedScatterUsesOriginalOffsetsAtTheNewRange()
    {
        var beatmap = createBeatmap(BmsLayoutVariant.Bme7K);
        var first = BmsHitScatterStatistic.CreateStatistics(BmsGameplayProfile.INSTANCE, beatmap,
        [
            new HitEvent(210, 1, HitResult.Meh, new BmsNote { StartTime = 1000 }, null, null),
            new HitEvent(-400, 1, HitResult.Miss, new HitObject { StartTime = 1500 }, null, null),
        ]);
        var second = BmsHitScatterStatistic.CreateStatistics(BmsGameplayProfile.INSTANCE, beatmap,
        [
            new HitEvent(187, 1, HitResult.Good, new BmsNote { StartTime = 1000 }, null, null),
        ]);
        var combined = BmsHitScatterStatistic.CombineStatistics([first, second]).Overall;
        Assert.Multiple(() =>
        {
            Assert.That(first.Overall.Points[0].DisplayOffset(first.Overall.OffsetRange), Is.EqualTo(150));
            Assert.That(combined.OffsetRange, Is.EqualTo(200));
            Assert.That(combined.Points.Select(point => point.DisplayOffset(combined.OffsetRange)), Is.EqualTo(new[] { 200, -200, 187 }));
            Assert.That(combined.Points[0].Offset, Is.EqualTo(210));
        });
    }
}
