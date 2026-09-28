using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

[TestFixture]
public class BmsSoftConstantTest
{
    [Test]
    public void TestDefaultsAndFadeInRange()
    {
        using var config = new BmsRulesetConfigManager(null, new BmsRuleset().RulesetInfo);
        Assert.That(config.Get<bool>(BmsRulesetSetting.SoftConstant), Is.False);
        Assert.That(config.Get<double>(BmsRulesetSetting.SoftConstantFadeIn), Is.Zero);
        config.SetValue(BmsRulesetSetting.SoftConstantFadeIn, -2000.0);
        Assert.That(config.Get<double>(BmsRulesetSetting.SoftConstantFadeIn), Is.EqualTo(-1000));
        config.SetValue(BmsRulesetSetting.SoftConstantFadeIn, 2000.0);
        Assert.That(config.Get<double>(BmsRulesetSetting.SoftConstantFadeIn), Is.EqualTo(1000));
    }

    [TestCase(0, 501, 0)]
    [TestCase(0, 500, 0)]
    [TestCase(0, 499, 1)]
    [TestCase(100, 600, 0)]
    [TestCase(100, 550, 0.5f)]
    [TestCase(100, 500, 1)]
    [TestCase(-100, 500, 0)]
    [TestCase(-100, 450, 0.5f)]
    [TestCase(-100, 400, 1)]
    [TestCase(-100, -100, 1)]
    public void TestFadeInTiming(double fadeIn, double remaining, float expected)
    {
        var controller = createController();
        controller.SoftConstantFadeIn.Value = fadeIn;
        controller.Update(5000 - remaining);
        Assert.That(controller.GetSoftConstantAlpha(5000), Is.EqualTo(expected).Within(0.0001));
    }

    [TestCase(0.5)]
    [TestCase(1.5)]
    [TestCase(2.0)]
    public void TestFadeUsesRealTimeAtDifferentPlaybackRates(double rate)
    {
        var controller = createController();
        controller.SetPlaybackRate(rate);
        controller.SoftConstantFadeIn.Value = 100;
        controller.Update(5000 - 550 * rate);
        Assert.That(controller.GetSoftConstantAlpha(5000), Is.EqualTo(0.5f).Within(0.0001));
    }

    [Test]
    public void TestSlowNotesAppearHalfwayWithoutChangingScroll()
    {
        var map = new BmsTimingMap(192, [], [new BmsBpmEvent(0, 60, 0)], [], [], [], 120);
        var controller = createController(map);
        controller.Update(4499);
        Assert.That(controller.GetSoftConstantAlpha(5000), Is.Zero);
        controller.Update(4501);
        Assert.That(controller.GetSoftConstantAlpha(5000), Is.EqualTo(1));
        var progress = controller.GetVisualScrollPosition(5000, map.GetScrollPositionAtTime(5000)) - controller.CurrentScrollPosition;
        Assert.That(controller.YForScrollProgress(progress, 768, 124.8), Is.EqualTo(322.2432).Within(0.01));
    }

    [Test]
    public void TestChartSpeedIsPreservedAndDoesNotChangeAppearanceTime()
    {
        var map = new BmsTimingMap(192, [], [new BmsBpmEvent(0, 120, 0)], [], [],
            [new BmsSpeedEvent(0, 0.5, 0)]);
        var controller = createController(map);
        controller.SoftConstantFadeIn.Value = 100;
        controller.Update(4450);
        Assert.That(controller.ChartSpeedFactor, Is.EqualTo(0.5));
        Assert.That(controller.GetSoftConstantAlpha(5000), Is.EqualTo(0.5f).Within(0.0001));
    }

    [Test]
    public void TestSpeedAndStageHeightChangesAdjustWindow()
    {
        var controller = createController();
        controller.Update(4550);
        Assert.That(controller.GetSoftConstantAlpha(5000), Is.EqualTo(1));
        controller.AdjustScrollSpeed(1);
        controller.Update(4550);
        Assert.That(controller.GetSoftConstantAlpha(5000), Is.Zero);
        controller.AdjustScrollSpeed(-1);
        controller.SetHitTargetPosition(446.4f);
        controller.Update(4550);
        Assert.That(controller.GetSoftConstantAlpha(5000), Is.Zero);
        controller.Update(4800);
        Assert.That(controller.GetSoftConstantAlpha(5000), Is.EqualTo(1));
    }

    [Test]
    public void TestDisableAndRewindRestoreVisibility()
    {
        var controller = createController();
        controller.Update(4600);
        Assert.That(controller.GetSoftConstantAlpha(5000), Is.EqualTo(1));
        controller.Update(4400);
        Assert.That(controller.GetSoftConstantAlpha(5000), Is.Zero);
        controller.SoftConstant.Value = false;
        controller.Update(4400);
        Assert.That(controller.GetSoftConstantAlpha(5000), Is.EqualTo(1));
        controller.SoftConstant.Value = true;
        controller.Update(4400);
        Assert.That(controller.GetSoftConstantAlpha(5000), Is.Zero);
        controller.LockScrollSpeedMultiplier();
        Assert.That(controller.GetSoftConstantAlpha(5000), Is.EqualTo(1));
    }

    private static BmsGameplayScrollController createController(BmsTimingMap map = null)
    {
        var controller = new BmsGameplayScrollController(map);
        controller.SetConfiguredScrollSpeed(BmsGameplayScrollController.MAX_TIME_RANGE / 500);
        controller.SoftConstant.Value = true;
        return controller;
    }
}
