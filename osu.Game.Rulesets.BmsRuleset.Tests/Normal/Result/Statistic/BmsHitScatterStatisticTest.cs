using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Result.Statistic;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Result.Statistic;

[TestFixture]
public class BmsHitScatterStatisticTest
{
    [Test]
    public void TestStatisticsAreGroupedByKeyLikeHitOffsetStatistic()
    {
        var statistics = BmsHitScatterStatistic.CreateStatistics(
            createBeatmap(BmsLayoutVariant.Bms5K),
            [
                new HitEvent(-12, 1, HitResult.Great, new BmsNote { Column = 0, StartTime = 1000 }, null, null),
                new HitEvent(8, 1, HitResult.Perfect, new BmsNote { Column = 0, StartTime = 1500 }, null, null),
                new HitEvent(30, 1, HitResult.Good, new BmsNote { Column = 1, StartTime = 2000 }, null, null),
                new HitEvent(-20, 1, HitResult.Ok, new BmsNote { Column = 1, StartTime = 2500 }, null, null),
                new HitEvent(100, 1, HitResult.Miss, new HitObject { StartTime = 3000 }, null, null),
                new HitEvent(5, 1, HitResult.Great, new HitObject { StartTime = 3500 }, null, null),
            ]);

        Assert.That(statistics.Overall.Points.Select(p => p.Offset), Is.EqualTo(new[] { -12, 8, 30, -20, -150 }));
        Assert.That(statistics.Keys.Select(k => k.Label), Is.EqualTo(new[] { "Scratch", "Key 1", "Key 2", "Key 3", "Key 4", "Key 5" }));
        Assert.That(statistics.Keys[0].Data.Points.Select(p => p.Offset), Is.EqualTo(new[] { -12, 8 }));
        Assert.That(statistics.Keys[1].Data.Points.Select(p => p.Offset), Is.EqualTo(new[] { 30, -20 }));
        Assert.That(statistics.Keys[2].Data.Points, Is.Empty);
    }

    [Test]
    public void TestPointsIncludeEmptyPoorAndBasicBmsHits()
    {
        var data = BmsHitScatterStatistic.CreateData([
            new HitEvent(-12, 1, HitResult.Perfect, new BmsNote { StartTime = 1000 }, null, null),
            new HitEvent(28, 1, HitResult.Great, new BmsNote { StartTime = 2000 }, null, null),
            new HitEvent(90, 1, HitResult.Miss, new BmsNote { StartTime = 3000 }, null, null),
            new HitEvent(0, 1, HitResult.Miss, new HitObject { StartTime = 3500 }, null, null),
            new HitEvent(0, 1, HitResult.Meh, new BmsLandmine { StartTime = 4000 }, null, null),
            new HitEvent(4, 1, HitResult.Good, new HitObject { StartTime = 4500 }, null, null),
        ]);

        Assert.That(data.Points.Select(p => p.Result), Is.EqualTo(new[]
        {
            HitResult.Perfect,
            HitResult.Great,
            HitResult.Miss,
            HitResult.Miss,
        }));

        Assert.That(data.Points.Select(p => p.Time), Is.EqualTo(new[] { 1000, 2000, 3000, 3500 }));
        Assert.That(data.Points.Select(p => p.Offset), Is.EqualTo(new[] { -12, 28, -150, -150 }));
        Assert.That(data.Duration, Is.EqualTo(3500));
    }

    [Test]
    public void TestEmptyPoorIsPinnedToUpperBoundary()
    {
        var data = BmsHitScatterStatistic.CreateData([
            new HitEvent(0, 1, HitResult.Miss, new HitObject { StartTime = 1000 }, null, null),
        ]);

        Assert.That(data.OffsetRange, Is.EqualTo(150));
        Assert.That(data.Points.Single().Offset, Is.EqualTo(-150));
    }

    [Test]
    public void TestOffsetRangeHasReadableTicks()
    {
        var data = BmsHitScatterStatistic.CreateData([
            new HitEvent(-187, 1, HitResult.Good, new BmsNote { StartTime = 1000 }, null, null),
            new HitEvent(12, 1, HitResult.Perfect, new BmsNote { StartTime = 2000 }, null, null),
        ]);

        Assert.That(data.OffsetRange, Is.EqualTo(200));
        Assert.That(data.OffsetTicks, Is.EqualTo(new[] { -200, -100, 0, 100, 200 }));
    }

    [Test]
    public void TestPoorAndEmptyPoorDoNotExpandOffsetRange()
    {
        var data = BmsHitScatterStatistic.CreateData([
            new HitEvent(-5000, 1, HitResult.Meh, new BmsNote { StartTime = 1000 }, null, null),
            new HitEvent(5000, 1, HitResult.Meh, new BmsNote { StartTime = 2000 }, null, null),
            new HitEvent(0, 1, HitResult.Miss, new HitObject { StartTime = 3000 }, null, null),
            new HitEvent(12, 1, HitResult.Perfect, new BmsNote { StartTime = 4000 }, null, null),
        ]);

        Assert.That(data.OffsetRange, Is.EqualTo(150));
        Assert.That(data.Points.Select(p => p.Offset), Is.EqualTo(new[] { -150, 150, -150, 12 }));
        Assert.That(data.OffsetTicks, Is.EqualTo(new[] { -150, -75, 0, 75, 150 }));
    }

    [Test]
    public void TestPoorWithinRangeKeepsOffset()
    {
        var data = BmsHitScatterStatistic.CreateData([
            new HitEvent(-187, 1, HitResult.Good, new BmsNote { StartTime = 1000 }, null, null),
            new HitEvent(175, 1, HitResult.Meh, new BmsNote { StartTime = 2000 }, null, null),
        ]);

        Assert.That(data.OffsetRange, Is.EqualTo(200));
        Assert.That(data.Points[1].Offset, Is.EqualTo(175));
    }

    [Test]
    public void TestTimedOffsetRangeIsCapped()
    {
        var data = BmsHitScatterStatistic.CreateData([
            new HitEvent(-5000, 1, HitResult.Good, new BmsNote { StartTime = 1000 }, null, null),
        ]);

        Assert.That(data.OffsetRange, Is.EqualTo(300));
        Assert.That(data.OffsetTicks, Is.EqualTo(new[] { -300, -150, 0, 150, 300 }));
    }

    private static BmsBeatmap createBeatmap(BmsLayoutVariant variant) => new()
    {
        LayoutVariant = variant,
        TotalColumns = BmsLayout.GetTotalColumns(variant),
    };
}
