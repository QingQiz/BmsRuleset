using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

[TestFixture]
public class BmsGameplayScrollControllerTest
{
    [Test]
    public void TestAdjustScrollSpeedAppliesPresetAndNotifies()
    {
        var controller = new BmsGameplayScrollController(null);
        var notifications = new List<double>();
        controller.ScrollSpeedChanged += notifications.Add;

        controller.AdjustScrollSpeed(1);

        Assert.That(controller.ScrollSpeed, Is.EqualTo(10).Within(0.001));
        Assert.That(controller.ScrollSpeedMultiplier, Is.EqualTo(1.25).Within(0.001));
        Assert.That(notifications, Is.EqualTo([1.25]));
    }

    [Test]
    public void TestConfiguredScrollSpeedUpdatesBaseWithoutNotification()
    {
        var controller = new BmsGameplayScrollController(null);
        var notifications = new List<double>();
        controller.ScrollSpeedChanged += notifications.Add;

        controller.SetConfiguredScrollSpeed(12);

        Assert.That(controller.ScrollSpeed, Is.EqualTo(12).Within(0.001));
        Assert.That(notifications, Is.Empty);

        controller.AdjustScrollSpeed(-1);

        Assert.That(controller.ScrollSpeed, Is.EqualTo(10.8).Within(0.001));
        Assert.That(controller.ScrollSpeedMultiplier, Is.EqualTo(1.35).Within(0.001));
        Assert.That(notifications, Is.EqualTo([0.9]));
    }

    [Test]
    public void TestUpdateFallsBackToCurrentTimeWithoutTimingMap()
    {
        var controller = new BmsGameplayScrollController(null);

        controller.Update(1234);

        Assert.That(controller.CurrentScrollPosition, Is.EqualTo(1234));
        Assert.That(controller.ChartSpeedFactor, Is.EqualTo(1));
    }

    [Test]
    public void TestVisualScrollPositionUsesTimeWithConstantScroll()
    {
        var controller = new BmsGameplayScrollController(null);

        Assert.That(controller.GetVisualScrollPosition(60000, 120000), Is.EqualTo(120000));

        controller.ConstantScrollActive = true;
        controller.Update(59000);

        Assert.That(controller.GetVisualScrollPosition(60000, 120000), Is.EqualTo(60000));
        Assert.That(controller.GetVisualScrollPosition(60000, 120000) - controller.CurrentScrollPosition, Is.EqualTo(1000));
    }

    [Test]
    public void TestLongNoteTailVisualOffsetMovesTailEarlier()
    {
        var controller = new BmsGameplayScrollController(null)
        {
            ConstantScrollActive = true,
        };

        var longNote = new BmsLongNote { StartTime = 1000, Duration = 1000, ScrollPositionAtEndTime = 2000 };
        controller.ApplyLongNoteTailVisualOffset(longNote, 250);

        Assert.That(longNote.VisualScrollPositionAtEndTime, Is.EqualTo(1750));
    }

    [Test]
    public void TestLongNoteTailVisualOffsetCannotMoveTailBeforeHead()
    {
        var controller = new BmsGameplayScrollController(null)
        {
            ConstantScrollActive = true,
        };

        var longNote = new BmsLongNote { StartTime = 1000, Duration = 1000, ScrollPositionAtEndTime = 2000 };
        controller.ApplyLongNoteTailVisualOffset(longNote, 1500);

        Assert.That(longNote.VisualScrollPositionAtEndTime, Is.EqualTo(1000));
    }

    [Test]
    public void TestLongNoteTailVisualOffsetUsesReverseScrollMapping()
    {
        var timingMap = new BmsTimingMap(
            192,
            [],
            [new BmsBpmEvent(0, 120, 0), new BmsBpmEvent(384, -120, 4000)],
            [],
            [],
            []);
        var controller = new BmsGameplayScrollController(timingMap);

        var longNote = new BmsLongNote
        {
            StartTime = 4000,
            Duration = 2000,
            ScrollPositionAtEndTime = timingMap.GetScrollPositionAtTime(6000),
        };
        controller.ApplyLongNoteTailVisualOffset(longNote, 500);

        Assert.That(longNote.VisualScrollPositionAtEndTime, Is.EqualTo(2500).Within(0.001));
    }

    [Test]
    public void TestLongNoteTailVisualOffsetAccountsForPlaybackRate()
    {
        var controller = new BmsGameplayScrollController(null)
        {
            ConstantScrollActive = true,
        };
        controller.SetPlaybackRate(1.5);

        var longNote = new BmsLongNote { StartTime = 1000, Duration = 1000, ScrollPositionAtEndTime = 2000 };
        controller.ApplyLongNoteTailVisualOffset(longNote, 200);

        Assert.That(longNote.VisualScrollPositionAtEndTime, Is.EqualTo(1700));
    }

    [Test]
    public void TestLongNoteTailVisualOffsetCachesMappedPositionOnLongNote()
    {
        var controller = new BmsGameplayScrollController(null)
        {
            ConstantScrollActive = true,
        };
        var longNote = new BmsLongNote { StartTime = 1000, Duration = 1000, ScrollPositionAtEndTime = 2000 };

        controller.ApplyLongNoteTailVisualOffset(longNote, 250);
        Assert.That(longNote.VisualScrollPositionAtEndTime, Is.EqualTo(1750));

        controller.ApplyLongNoteTailVisualOffset(longNote, 100);
        Assert.That(longNote.VisualScrollPositionAtEndTime, Is.EqualTo(1900));
    }

    [Test]
    public void TestCroppedViewportPreservesVisibleTime()
    {
        var controller = new BmsGameplayScrollController(null);
        controller.SetHitTargetPosition(80);
        var visibleProgress = controller.ScrollRange / controller.ScrollSpeedMultiplier;

        Assert.Multiple(() =>
        {
            Assert.That(controller.YForScrollProgress(visibleProgress, 600, 80), Is.EqualTo(0).Within(0.001f));
            Assert.That(controller.YForScrollProgress(visibleProgress, 300, 80), Is.EqualTo(0).Within(0.001f));
        });
    }

    [TestCase(0.75)]
    [TestCase(1.5)]
    public void TestPlaybackRatePreservesTravelDistance(double playbackRate)
    {
        var normal = new BmsGameplayScrollController(null);
        var adjusted = new BmsGameplayScrollController(null);
        adjusted.SetPlaybackRate(playbackRate);

        var normalPosition = normal.YForScrollProgress(1000, 768, 124.8);
        var adjustedPosition = adjusted.YForScrollProgress(1000 * playbackRate, 768, 124.8);

        Assert.That(adjustedPosition, Is.EqualTo(normalPosition).Within(0.001f));
    }
}
