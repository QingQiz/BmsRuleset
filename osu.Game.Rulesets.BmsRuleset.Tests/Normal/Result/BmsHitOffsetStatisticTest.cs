using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Result;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;

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
                new HitEvent(100, 1, HitResult.Miss, new BmsNote { Column = 0, StartTime = 3000 }, null, null),
                new HitEvent(5, 1, HitResult.Great, new HitObject { StartTime = 3500 }, null, null),
            ]);

        Assert.That(statistics.Overall.Count, Is.EqualTo(4));
        Assert.That(statistics.Overall.AverageOffset, Is.EqualTo(1.5).Within(0.001));
        Assert.That(statistics.Overall.EarlyCount, Is.EqualTo(2));
        Assert.That(statistics.Overall.LateCount, Is.EqualTo(2));
        Assert.That(statistics.Overall.BinsByResult.Values.SelectMany(b => b).Sum(), Is.EqualTo(4));

        Assert.That(statistics.Keys.Select(k => k.Label), Is.EqualTo(new[] { "Scratch", "Key 1", "Key 2", "Key 3", "Key 4", "Key 5" }));
        Assert.That(statistics.Keys[0].Summary.Count, Is.EqualTo(2));
        Assert.That(statistics.Keys[0].Summary.AverageOffset, Is.EqualTo(-2).Within(0.001));
        Assert.That(statistics.Keys[1].Summary.Count, Is.EqualTo(2));
        Assert.That(statistics.Keys[1].Summary.AverageOffset, Is.EqualTo(5).Within(0.001));
        Assert.That(statistics.Keys[2].Summary.Count, Is.Zero);
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

    private static BmsBeatmap createBeatmap(BmsLayoutVariant variant) => new()
    {
        LayoutVariant = variant,
        TotalColumns = BmsLayout.GetTotalColumns(variant),
    };
}
