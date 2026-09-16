using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

public class BmsHitObjectPoolPlanTest
{
    [Test]
    public void SparseChartsKeepSmallPools()
    {
        var sizes = BmsHitObjectPoolPlan.Create(Enumerable.Range(0, 100)
            .Select(i => new BmsNote { Column = 1, StartTime = i * 10000 }), 3);
        Assert.That(sizes, Is.All.EqualTo(new BmsHitObjectPoolPlan.ColumnSizes(64, 32, 32)));
    }

    [Test]
    public void SeparatesColumnsAndTypesRegardlessOfInputOrder()
    {
        var objects = Enumerable.Range(0, 300).Select(i => new BmsLandmine { Column = 1, StartTime = i })
            .Cast<BmsHitObject>()
            .Concat(Enumerable.Range(0, 500).Select(_ => new BmsLandmine { Column = 2, StartTime = 100 }))
            .Concat(Enumerable.Range(0, 400).Select(_ => new BmsNote { Column = 1, StartTime = 100 }))
            .Reverse();
        var sizes = BmsHitObjectPoolPlan.Create(objects, 3);
        Assert.That(sizes[1], Is.EqualTo(new BmsHitObjectPoolPlan.ColumnSizes(400, 32, 201)));
        Assert.That(sizes[2], Is.EqualTo(new BmsHitObjectPoolPlan.ColumnSizes(64, 32, 500)));
    }

    [Test]
    public void LongNoteDurationContributesToPeakResidence()
    {
        var objects = Enumerable.Range(0, 100).Select(i => new BmsLongNote { Column = 0, StartTime = i * 2000, Duration = 200000 });
        Assert.That(BmsHitObjectPoolPlan.Create(objects, 1)[0].LongNotes, Is.EqualTo(100));
    }

    [Test]
    public void NonOverlappingBurstsReuseCapacity()
    {
        var objects = Enumerable.Range(0, 500).Select(i => new BmsNote { Column = 0, StartTime = i / 100 * 10000 });
        Assert.That(BmsHitObjectPoolPlan.Create(objects, 1)[0].Notes, Is.EqualTo(100));
    }

    [Test]
    public void CapsIndividualPoolAndWholePlayfield()
    {
        var sizes = BmsHitObjectPoolPlan.Create(Enumerable.Range(0, 16).SelectMany(column =>
            Enumerable.Range(0, 9000).SelectMany(_ => new BmsHitObject[]
            {
                new BmsNote { Column = column }, new BmsLongNote { Column = column, Duration = 1000 }, new BmsLandmine { Column = column },
            })), 16);
        Assert.That(sizes.Sum(s => s.Notes + s.LongNotes * 3 + s.Mines), Is.LessThanOrEqualTo(BmsHitObjectPoolPlan.PREWARM_BUDGET));
        Assert.That(sizes.All(s => s.Notes is >= 64 and <= 8192 && s.LongNotes is >= 32 and <= 8192 && s.Mines is >= 32 and <= 8192), Is.True);
        Assert.That(BmsHitObjectPoolPlan.Create(Enumerable.Range(0, 9000).Select(_ => new BmsLandmine()), 1)[0].Mines, Is.EqualTo(8192));
    }
}
