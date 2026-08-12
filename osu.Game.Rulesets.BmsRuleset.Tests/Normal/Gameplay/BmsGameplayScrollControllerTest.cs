using System.Collections.Generic;
using NUnit.Framework;
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
