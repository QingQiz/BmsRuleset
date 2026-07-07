using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Result;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Result;

[TestFixture]
public class BmsHitScatterStatisticTest
{
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
        Assert.That(data.Points.Select(p => p.Offset), Is.EqualTo(new[] { -12, 28, 90, 0 }));
        Assert.That(data.Duration, Is.EqualTo(3500));
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
}
