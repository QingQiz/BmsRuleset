using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.UI;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

[TestFixture]
public class BmsScrollControllerTest
{
    [Test]
    public void TestAdjustScrollSpeedAppliesPresetAndNotifies()
    {
        var controller = new BmsScrollController(null);
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
        var controller = new BmsScrollController(null);
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
        var controller = new BmsScrollController(null);

        controller.Update(1234);

        Assert.That(controller.CurrentScrollPosition, Is.EqualTo(1234));
        Assert.That(controller.ChartSpeedFactor, Is.EqualTo(1));
    }

    [Test]
    public void TestVisualScrollPositionUsesTimeWithConstantScroll()
    {
        var controller = new BmsScrollController(null);

        Assert.That(controller.GetVisualScrollPosition(60000, 120000), Is.EqualTo(120000));

        controller.ConstantScrollActive = true;
        controller.Update(59000);

        Assert.That(controller.GetVisualScrollPosition(60000, 120000), Is.EqualTo(60000));
        Assert.That(controller.GetVisualScrollPosition(60000, 120000) - controller.CurrentScrollPosition, Is.EqualTo(1000));
    }

    [Test]
    public void TestCroppedViewportPreservesVisibleTime()
    {
        var controller = new BmsScrollController(null);
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
        var normal = new BmsScrollController(null);
        var adjusted = new BmsScrollController(null);
        adjusted.SetPlaybackRate(playbackRate);

        var normalPosition = normal.YForScrollProgress(1000, 768, 124.8);
        var adjustedPosition = adjusted.YForScrollProgress(1000 * playbackRate, 768, 124.8);

        Assert.That(adjustedPosition, Is.EqualTo(normalPosition).Within(0.001f));
    }
}
