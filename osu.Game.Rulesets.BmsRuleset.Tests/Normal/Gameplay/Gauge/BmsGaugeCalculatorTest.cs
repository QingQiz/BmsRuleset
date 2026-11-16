using NUnit.Framework;
using RhythmEngine.Profiles.Bms;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Gauge;

[TestFixture]
public class BmsGaugeCalculatorTest
{
    [Test]
    public void TestTotalGaugeUsesTotalOverNoteCount()
    {
        var profile = BmsGaugeProfileFactory.Create(BmsGaugeType.Easy);
        var calculator = new BmsGaugeCalculator(profile, total: 80, noteCount: 100);

        Assert.That(calculator.GetDeltaFor(BmsResults.PGREAT, 0.2), Is.EqualTo(0.008).Within(0.0001));
        Assert.That(calculator.GetDeltaFor(BmsResults.GREAT, 0.2), Is.EqualTo(0.008).Within(0.0001));
        Assert.That(calculator.GetDeltaFor(BmsResults.GOOD, 0.2), Is.EqualTo(0.004).Within(0.0001));
    }

    [Test]
    public void TestDefaultTotalFormulaIsUsedWhenTotalIsUnspecified()
    {
        var profile = BmsGaugeProfileFactory.Create(BmsGaugeType.Normal);
        var calculator = new BmsGaugeCalculator(profile, total: null, noteCount: 100);

        Assert.That(calculator.Total, Is.EqualTo(260).Within(0.0001));
        Assert.That(calculator.GetDeltaFor(BmsResults.PGREAT, 0.2), Is.EqualTo(0.026).Within(0.0001));
    }

    [Test]
    public void TestDefaultTotalFormulaUsesLayoutMinimum()
    {
        Assert.That(BmsGaugeCalculator.CalculateDefaultTotal(200), Is.EqualTo(260).Within(0.0000001));
        Assert.That(BmsGaugeCalculator.CalculateDefaultTotal(100, BmsGaugeProfileFamily.Keyboard), Is.EqualTo(300).Within(0.0000001));
    }

    [Test]
    public void TestLimitIncrementScalesRecovery()
    {
        var profile = BmsGaugeProfileFactory.Create(BmsGaugeType.Hard);
        var calculator = new BmsGaugeCalculator(profile, total: 180, noteCount: 400);

        Assert.That(calculator.GetDeltaFor(BmsResults.PGREAT, 0.5), Is.EqualTo(0.001).Within(0.0001));
        Assert.That(calculator.GetDeltaFor(BmsResults.GREAT, 0.5), Is.EqualTo(0.0008).Within(0.0001));
        Assert.That(calculator.GetDeltaFor(BmsResults.GOOD, 0.5), Is.EqualTo(0.0002).Within(0.0001));
    }

    [Test]
    public void TestHardGutsReducesDamageAtLowHealth()
    {
        var profile = BmsGaugeProfileFactory.Create(BmsGaugeType.Hard);
        var calculator = new BmsGaugeCalculator(profile, total: 160, noteCount: 1000);

        Assert.That(calculator.GetDeltaFor(BmsResults.BAD, 0.51), Is.EqualTo(-0.05).Within(0.0001));
        Assert.That(calculator.GetDeltaFor(BmsResults.BAD, 0.50), Is.EqualTo(-0.05).Within(0.0001));
        Assert.That(calculator.GetDeltaFor(BmsResults.BAD, 0.499), Is.EqualTo(-0.04).Within(0.0001));
        Assert.That(calculator.GetDeltaFor(BmsResults.POOR, 0.10), Is.EqualTo(-0.05).Within(0.0001));
    }

    [Test]
    public void TestHazardBadAndPoorAreFatal()
    {
        var profile = BmsGaugeProfileFactory.Create(BmsGaugeType.Hazard);
        var calculator = new BmsGaugeCalculator(profile, total: 160, noteCount: 1000);

        Assert.That(calculator.GetDeltaFor(BmsResults.BAD, 1), Is.EqualTo(-1).Within(0.0001));
        Assert.That(calculator.GetDeltaFor(BmsResults.POOR, 1), Is.EqualTo(-1).Within(0.0001));
        Assert.That(calculator.GetDeltaFor(BmsResults.EMPTY_POOR, 1), Is.EqualTo(-0.10).Within(0.0001));
    }

    [Test]
    public void TestModifyDamageScalesOnlyDamage()
    {
        var profile = BmsGaugeProfileFactory.Create(BmsGaugeType.ExHard, BmsGaugeProfileFamily.FiveKeys);
        var calculator = new BmsGaugeCalculator(profile, total: 160, noteCount: 1000);

        Assert.Multiple(() =>
        {
            Assert.That(calculator.GetDeltaFor(BmsResults.PGREAT, 1), Is.Zero);
            Assert.That(calculator.GetDeltaFor(BmsResults.BAD, 1), Is.EqualTo(-0.20).Within(0.0001));
        });
    }

    [TestCase(130, -0.3333)]
    [TestCase(149, -0.3333)]
    [TestCase(150, -0.25)]
    public void TestModifyDamageTotalScaleBoundaries(double total, double expectedDamage)
    {
        var profile = BmsGaugeProfileFactory.Create(BmsGaugeType.ExHard, BmsGaugeProfileFamily.FiveKeys);
        var calculator = new BmsGaugeCalculator(profile, total, noteCount: 1000);

        Assert.That(calculator.GetDeltaFor(BmsResults.BAD, 1), Is.EqualTo(expectedDamage).Within(0.0001));
    }
}
