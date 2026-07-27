using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Gauge;

[TestFixture]
public class BmsGaugeCalculatorTest
{
    [Test]
    public void TestTotalGaugeUsesTotalOverNoteCount()
    {
        var profile = BmsGaugeProfileFactory.Create(BmsGaugeType.Easy);
        var calculator = new BmsGaugeCalculator(profile, total: 80, noteCount: 100);

        Assert.That(calculator.GetDeltaFor(HitResult.Perfect, 0.2), Is.EqualTo(0.008).Within(0.0001));
        Assert.That(calculator.GetDeltaFor(HitResult.Great, 0.2), Is.EqualTo(0.008).Within(0.0001));
        Assert.That(calculator.GetDeltaFor(HitResult.Good, 0.2), Is.EqualTo(0.004).Within(0.0001));
    }

    [Test]
    public void TestDefaultTotalFormulaIsUsedWhenHeaderTotalIsZero()
    {
        var profile = BmsGaugeProfileFactory.Create(BmsGaugeType.Normal);
        var calculator = new BmsGaugeCalculator(profile, total: 0, noteCount: 100);

        Assert.That(calculator.Total, Is.EqualTo(160).Within(0.0001));
        Assert.That(calculator.GetDeltaFor(HitResult.Perfect, 0.2), Is.EqualTo(0.016).Within(0.0001));
    }

    [Test]
    public void TestDefaultTotalFormulaAboveMinimum()
    {
        Assert.That(BmsGaugeCalculator.CalculateDefaultTotal(200), Is.EqualTo(178.94117647058823).Within(0.0000001));
    }

    [Test]
    public void TestLimitIncrementScalesRecovery()
    {
        var profile = BmsGaugeProfileFactory.Create(BmsGaugeType.Hard);
        var calculator = new BmsGaugeCalculator(profile, total: 180, noteCount: 400);

        Assert.That(calculator.GetDeltaFor(HitResult.Perfect, 0.5), Is.EqualTo(0.001).Within(0.0001));
        Assert.That(calculator.GetDeltaFor(HitResult.Great, 0.5), Is.EqualTo(0.0008).Within(0.0001));
        Assert.That(calculator.GetDeltaFor(HitResult.Good, 0.5), Is.EqualTo(0.0002).Within(0.0001));
    }

    [Test]
    public void TestHardGutsReducesDamageAtLowHealth()
    {
        var profile = BmsGaugeProfileFactory.Create(BmsGaugeType.Hard);
        var calculator = new BmsGaugeCalculator(profile, total: 160, noteCount: 1000);

        Assert.That(calculator.GetDeltaFor(HitResult.Ok, 0.51), Is.EqualTo(-0.05).Within(0.0001));
        Assert.That(calculator.GetDeltaFor(HitResult.Ok, 0.50), Is.EqualTo(-0.04).Within(0.0001));
        Assert.That(calculator.GetDeltaFor(HitResult.Meh, 0.10), Is.EqualTo(-0.04).Within(0.0001));
    }

    [Test]
    public void TestHazardBadAndPoorAreFatal()
    {
        var profile = BmsGaugeProfileFactory.Create(BmsGaugeType.Hazard);
        var calculator = new BmsGaugeCalculator(profile, total: 160, noteCount: 1000);

        Assert.That(calculator.GetDeltaFor(HitResult.Ok, 1), Is.EqualTo(-1).Within(0.0001));
        Assert.That(calculator.GetDeltaFor(HitResult.Meh, 1), Is.EqualTo(-1).Within(0.0001));
        Assert.That(calculator.GetDeltaFor(HitResult.Miss, 1), Is.EqualTo(-0.10).Within(0.0001));
    }
}
