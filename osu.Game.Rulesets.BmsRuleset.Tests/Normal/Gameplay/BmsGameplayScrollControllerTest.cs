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

    [Test]
    public void TestScrollGeometryChangesApplyBeforeNextFrame()
    {
        var controller = new BmsGameplayScrollController(null);
        Assert.That(controller.ScrollRange, Is.EqualTo(1435.625));
        Assert.That(controller.ScrollSpeedMultiplier, Is.EqualTo(1));
        Assert.That(controller.ScrollCoordinateScale(600, 100), Is.EqualTo(500 / 1435.625).Within(1e-12));

        controller.SetPlaybackRate(1.5);
        Assert.That(controller.ScrollRange, Is.EqualTo(2153.4375));
        Assert.That(controller.ScrollCoordinateScale(600, 100), Is.EqualTo(500 / 2153.4375).Within(1e-12));

        controller.SetHitTargetPosition(446.4f);
        Assert.That(controller.ScrollRange, Is.EqualTo(1076.71875));
        Assert.That(controller.ScrollCoordinateScale(600, 100), Is.EqualTo(500 / 1076.71875).Within(1e-12));

        controller.SetConfiguredScrollSpeed(12);
        Assert.That(controller.ScrollSpeedMultiplier, Is.EqualTo(1.5));
        Assert.That(controller.ScrollCoordinateScale(600, 100), Is.EqualTo(1.5 / 1076.71875 * 500));

        controller.SetHitTargetPosition(768);
        Assert.That(controller.ScrollRange, Is.Zero);
        Assert.That(controller.ScrollCoordinateScale(600, 600), Is.EqualTo(1.5));

        controller.SetHitTargetPosition(124.8f);
        controller.SetPlaybackRate(1);
        Assert.That(controller.ScrollRange, Is.EqualTo(1435.625));
        Assert.That(controller.ScrollCoordinateScale(600, 100), Is.EqualTo(1.5 / 1435.625 * 500));
    }

    [Test]
    public void TestScrollSpeedNotificationObservesUpdatedGeometry()
    {
        var controller = new BmsGameplayScrollController(null);
        var multipliers = new List<double>();
        var scales = new List<double>();
        controller.ScrollSpeedChanged += _ =>
        {
            multipliers.Add(controller.ScrollSpeedMultiplier);
            scales.Add(controller.ScrollCoordinateScale(600, 100));
        };

        controller.AdjustScrollSpeed(1);
        controller.LockScrollSpeedMultiplier();
        controller.AdjustScrollSpeed(1);

        Assert.That(multipliers, Is.EqualTo([1.25, 1.0]));
        Assert.That(scales, Is.EqualTo([1.25 / 1435.625 * 500, 500 / 1435.625]).Within(1e-12));
    }

    [TestCase(1)]
    [TestCase(0.5)]
    [TestCase(0)]
    [TestCase(-0.5)]
    public void TestChartSpeedChangesRewindAndConstantScrollUpdateGeometry(double speedFactor)
    {
        var map = new BmsTimingMap(192, [], [new BmsBpmEvent(0, 120, 0)], [], [],
            [new BmsSpeedEvent(0, 1, 0), new BmsSpeedEvent(192, speedFactor, 1)]);
        var controller = new BmsGameplayScrollController(map);
        controller.SetConfiguredScrollSpeed(12);
        var boundary = map.ProjectTickToTime(192);

        controller.Update(boundary - 1);
        Assert.That(controller.ScrollSpeedMultiplier, Is.EqualTo(1.5));
        Assert.That(controller.ScrollCoordinateScale(600, 100), Is.EqualTo(1.5 / 1435.625 * 500));

        controller.Update(boundary);
        Assert.That(controller.ScrollSpeedMultiplier, Is.EqualTo(1.5 * speedFactor));
        Assert.That(controller.ScrollCoordinateScale(600, 100), Is.EqualTo(1.5 * speedFactor / 1435.625 * 500));

        controller.ConstantScrollActive = true;
        controller.Update(boundary);
        Assert.That(controller.ScrollSpeedMultiplier, Is.EqualTo(1.5));
        Assert.That(controller.ScrollCoordinateScale(600, 100), Is.EqualTo(1.5 / 1435.625 * 500));

        controller.ConstantScrollActive = false;
        controller.Update(boundary);
        Assert.That(controller.ScrollSpeedMultiplier, Is.EqualTo(1.5 * speedFactor));
        controller.Update(boundary - 1);
        Assert.That(controller.ScrollSpeedMultiplier, Is.EqualTo(1.5));
        Assert.That(controller.ScrollCoordinateScale(600, 100), Is.EqualTo(1.5 / 1435.625 * 500));
    }

    [TestCase(-1.5, 1.5)]
    [TestCase(0, 1)]
    [TestCase(0.0001, 1)]
    [TestCase(double.NaN, 1)]
    [TestCase(double.PositiveInfinity, 1)]
    public void TestPlaybackRateNormalisationUpdatesGeometry(double rate, double expectedRate)
    {
        var controller = new BmsGameplayScrollController(null);
        controller.SetPlaybackRate(2);
        controller.SetPlaybackRate(rate);

        Assert.That(controller.PlaybackRate, Is.EqualTo(expectedRate));
        Assert.That(controller.ScrollRange, Is.EqualTo(1435.625 * expectedRate));
        Assert.That(controller.ScrollCoordinateScale(600, 100), Is.EqualTo(500 / (1435.625 * expectedRate)).Within(1e-12));
    }
}
