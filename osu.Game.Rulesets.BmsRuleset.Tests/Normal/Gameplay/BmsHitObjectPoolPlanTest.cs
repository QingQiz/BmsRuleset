using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

public class BmsHitObjectPoolPlanTest
{
    [Test]
    public void ShortLongNoteBurstsPreloadHeadHoldAndTailPulses()
    {
        var objects = Enumerable.Range(0, 100).Select(i => new BmsLongNote { Column = 1, StartTime = i * 0.001, Duration = 0.0005 });
        Assert.That(BmsHitObjectPoolPlan.CreateLongNoteHitExplosionSizes(objects, 3), Is.EqualTo(new[] { 3, 300, 3 }));
    }

    [Test]
    public void LongHoldPulsePrewarmingIsBoundedRegardlessOfDuration()
    {
        var objects = Enumerable.Range(0, 16).SelectMany(column =>
            Enumerable.Range(0, 9000).Select(_ => new BmsLongNote { Column = column, Duration = 1e100 }));
        var sizes = BmsHitObjectPoolPlan.CreateLongNoteHitExplosionSizes(objects, 16);
        Assert.That(sizes.Sum(), Is.InRange(8100, 8192));
        Assert.That(BmsHitObjectPoolPlan.CreateLongNoteHitExplosionSizes([new BmsLongNote { Duration = 1000 }], 1)[0], Is.EqualTo(5));
    }

    [Test]
    public void SparseChartsKeepSmallHitExplosionPools()
    {
        var sizes = BmsHitObjectPoolPlan.CreateHitExplosionSizes(Enumerable.Range(0, 100)
            .Select(i => new BmsNote { Column = 1, StartTime = i * 10000 }), 3);
        Assert.That(sizes, Is.EqualTo(new[] { 2, 2, 2 }));
    }

    [TestCase(216, 3)]
    [TestCase(216.001, 2)]
    public void HitExplosionOverlapIncludesOneDeferredFrame(double lastTime, int expected)
    {
        BmsHitObject[] objects =
        [
            new BmsNote { StartTime = lastTime },
            new BmsNote { StartTime = 0 },
            new BmsNote { StartTime = 100 },
        ];
        Assert.That(BmsHitObjectPoolPlan.CreateHitExplosionSizes(objects, 1)[0], Is.EqualTo(expected));
    }

    [Test]
    public void HitExplosionPoolsSeparateColumnsAndIgnoreOtherObjects()
    {
        var objects = Enumerable.Range(0, 8).SelectMany(i => new BmsHitObject[]
        {
            new BmsNote { Column = 1, StartTime = i * 1000 },
            new BmsNote { Column = 2, StartTime = 100 },
            new BmsLongNote { Column = 0, StartTime = 100, Duration = 1000 },
            new BmsLandmine { Column = 0, StartTime = 100 },
            new BmsNote { Column = -1 },
            new BmsNote { Column = 3 },
        }).Reverse();
        Assert.That(BmsHitObjectPoolPlan.CreateHitExplosionSizes(objects, 3), Is.EqualTo(new[] { 2, 2, 8 }));
    }

    [Test]
    public void HitExplosionPreloadingRespectsWholePlayfieldBudget()
    {
        var sizes = BmsHitObjectPoolPlan.CreateHitExplosionSizes(Enumerable.Range(0, 16).SelectMany(column =>
            Enumerable.Range(0, column < 8 ? 9000 : 0).Select(_ => new BmsNote { Column = column })), 16);
        Assert.That(sizes.Sum(), Is.InRange(8100, 8192));
        Assert.That(sizes.Take(8), Is.All.GreaterThan(2));
        Assert.That(sizes.Skip(8), Is.All.EqualTo(2));
    }

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
