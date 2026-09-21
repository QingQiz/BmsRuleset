using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Replays;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.AutoGeneration;

[TestFixture]
public class BmsAutoGeneratorTest
{
    [TestCase(0.0001)]
    [TestCase(1)]
    [TestCase(9)]
    public void TestShortLongNotesReleaseAtTailBeforeNextPress(double duration)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects =
            {
                new BmsLongNote { StartTime = 1000, Column = 1, Duration = duration },
                new BmsLongNote { StartTime = 1000 + duration * 2, Column = 1, Duration = duration },
            },
        };

        var frames = new BmsAutoGenerator(beatmap).Generate().Frames.OfType<BmsReplayFrame>().ToArray();
        Assert.That(frames.Single(f => f.Time == 1000 + duration).Actions, Does.Not.Contain(BmsAction.Key1));
        Assert.That(frames.Single(f => f.Time == 1000 + duration * 2).Actions, Contains.Item(BmsAction.Key1));
        Assert.That(frames.Single(f => f.Time == 1000 + duration * 2 + duration).Actions, Does.Not.Contain(BmsAction.Key1));
    }

    [Test]
    public void TestAutoplayGeneratesPressAndReleaseFrames()
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 1 },
                new BmsHitObject { StartTime = 1200, Column = 1 },
                new BmsLongNote { StartTime = 2000, Column = 2, Duration = 500 },
            },
        };

        var replay = new BmsAutoGenerator(beatmap).Generate();
        var frames = replay.Frames.OfType<BmsReplayFrame>().ToArray();

        Assert.That(frames.Select(f => f.Time), Does.Contain(1000));
        Assert.That(frames.Select(f => f.Time), Does.Contain(1200));
        Assert.That(frames.Select(f => f.Time), Does.Contain(2500));
        Assert.That(frames.Single(f => f.Time == 1000).Actions, Contains.Item(BmsAction.Key1));
        Assert.That(frames.Single(f => f.Time == 2500).Actions, Does.Not.Contain(BmsAction.Key2));
    }

    [Test]
    public void TestAutoplayReleasesBeforePressingSameActionAtSameTime()
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 1 },
                new BmsHitObject { StartTime = 1100, Column = 1 },
            },
        };

        var replay = new BmsAutoGenerator(beatmap).Generate();
        var frames = replay.Frames.OfType<BmsReplayFrame>().ToArray();

        Assert.That(frames.Single(f => f.Time == 1100).Actions, Contains.Item(BmsAction.Key1));
    }
}
