using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

[TestFixture]
public class BmsScrollVisibilityTest
{
    [Test]
    public void FoldedNotesWaitForTheFirstScreenExitToClear()
    {
        var beatmap = createBeatmap();
        var controller = createController(beatmap);

        controller.Update(1000);
        Assert.That(controller.TimelineEndTime, Is.EqualTo(4000));
        Assert.That(controller.IsTimelineVisible(4000), Is.True, "The first outside row is still processed.");
        Assert.That(controller.IsTimelineVisible(10000), Is.False);

        controller.Update(9000);
        Assert.That(controller.IsTimelineVisible(10000), Is.True);
        controller.Update(1000);
        Assert.That(controller.IsTimelineVisible(10000), Is.False, "Rewinding must restore the earlier barrier.");
    }

    [Test]
    public void ScreenExitIncludesTimingRowsWithoutNotesAndAllowsExactTopEdge()
    {
        var beatmap = createBeatmap();
        var visibility = new BmsScrollVisibility(beatmap, beatmap.TimingMap!);

        Assert.That(visibility.GetEndTime(0, 0, 1d / 2000), Is.EqualTo(4000));
        Assert.That(visibility.GetEndTime(0, 0, 1d / 4000), Is.EqualTo(14000));
    }

    [Test]
    public void SpeedAndConstantScrollChangesRefreshTheBarrier()
    {
        var controller = createController(createBeatmap());
        controller.Update(1000);
        Assert.That(controller.IsTimelineVisible(10000), Is.False);

        controller.SetConfiguredScrollSpeed(3);
        Assert.That(controller.IsTimelineVisible(10000), Is.True);
        controller.SetConfiguredScrollSpeed(8);
        Assert.That(controller.IsTimelineVisible(10000), Is.False);

        controller.SetPlaybackRate(3);
        Assert.That(controller.IsTimelineVisible(10000), Is.True);
        controller.SetPlaybackRate(1);
        Assert.That(controller.IsTimelineVisible(10000), Is.False);

        controller.ConstantScrollActive = true;
        controller.Update(1000);
        Assert.That(controller.TimelineEndTime, Is.EqualTo(double.PositiveInfinity));
        controller.ConstantScrollActive = false;
        controller.Update(1000);
        Assert.That(controller.IsTimelineVisible(10000), Is.False);
    }

    [Test]
    public void ValidEarlierAppearancesSurviveAReverseExcursion()
    {
        var beatmap = createBeatmap();
        beatmap.TimingMap = new BmsTimingMap(192, [], [new BmsBpmEvent(0, 120, 0)], [],
            [new BmsScrollEvent(96, -1, 0), new BmsScrollEvent(192, 1, 1)], [], 120);
        beatmap.HitObjects = [new BmsNote { StartTime = 2500 }];
        var controller = createController(beatmap);

        foreach (var time in new[] { 0, 1000, 1500, 2000, 2500, 0 })
        {
            controller.Update(time);
            Assert.That(controller.IsTimelineVisible(2500), Is.True, $"time={time}");
        }
    }

    [TestCase(0, double.PositiveInfinity)]
    [TestCase(-0.001, 8000)]
    public void ZeroAndNegativeSpeedUseTheProjectedTopEdge(double scale, double expectedEnd)
    {
        var beatmap = createBeatmap();
        var visibility = new BmsScrollVisibility(beatmap, beatmap.TimingMap!);
        Assert.That(visibility.GetEndTime(5000, 3000, scale), Is.EqualTo(expectedEnd));
    }

    [Test]
    public void StopKeepsTheSameBarrierUntilScrollingResumes()
    {
        var beatmap = createBeatmap();
        beatmap.TimingMap = new BmsTimingMap(192, [], [new BmsBpmEvent(0, 120, 0)],
            [new BmsStopEvent(96, 2000, 192, 120, 0)],
            [new BmsScrollEvent(384, -1, 0), new BmsScrollEvent(768, 1, 1)], [], 120);
        var controller = createController(beatmap);
        controller.Update(1000);
        var end = controller.TimelineEndTime;
        controller.Update(2999);
        Assert.That(controller.TimelineEndTime, Is.EqualTo(end));
    }

    [Test]
    public void PrisonBreakerDoesNotRevealTheLaterReverseSectionAtTheStart()
    {
        var resource = typeof(BmsScrollVisibilityTest).Assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith("99_outlaw_prison_breaker.bms", StringComparison.Ordinal));
        using var stream = typeof(BmsScrollVisibilityTest).Assembly.GetManifestResourceStream(resource)!;
        using var reader = new LineBufferedReader(stream);
        var decoded = new BmsBeatmapDecoder(referenceBpmMode: BmsReferenceBpmMode.StartBpm).Decode(reader);
        var beatmap = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset())
        {
            ReferenceBpmMode = BmsReferenceBpmMode.StartBpm,
        }.Convert();
        var controller = createController(beatmap);
        var folded = beatmap.HitObjects.Single(n => n.SourceChannel == BmsChartParser.Enc("11") && n.StartTime > 9900 && n.StartTime < 10000);

        controller.Update(2000);
        Assert.That(Math.Abs(folded.ScrollPositionAtStartTime - controller.CurrentScrollPosition), Is.LessThan(controller.ScrollRange),
            "The folded note would pass the old coordinate-only visibility check.");
        Assert.That(controller.IsTimelineVisible(folded.StartTime), Is.False);

        controller.Update(folded.StartTime - 1);
        Assert.That(controller.IsTimelineVisible(folded.StartTime), Is.True);
    }

    private static BmsGameplayScrollController createController(BmsBeatmap beatmap)
    {
        var controller = new BmsGameplayScrollController(beatmap.TimingMap);
        controller.InitializeVisibility(beatmap);
        return controller;
    }

    private static BmsBeatmap createBeatmap() => new()
    {
        TimingMap = new BmsTimingMap(192,
            Enumerable.Range(0, 8).Select(i => new BmsMeasureInfo(i, i * 192, 192, 1)),
            [new BmsBpmEvent(0, 120, 0)], [],
            [new BmsScrollEvent(384, -1, 0), new BmsScrollEvent(768, 1, 1)], [], 120),
        HitObjects = [new BmsNote { StartTime = 10000 }],
    };
}
