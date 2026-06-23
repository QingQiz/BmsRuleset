using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsHitWindowsTest
{
    [Test]
    public void TestHitWindowDefaultRankIsNormalSevenKeysBeatoraja()
    {
        var windows = new BmsHitWindows();
        windows.SetDifficulty(5);

        Assert.That(windows.WindowFor(HitResult.Perfect), Is.EqualTo(15).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Great), Is.EqualTo(45).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Good), Is.EqualTo(112.5).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Ok), Is.EqualTo(280).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Meh), Is.EqualTo(280).Within(0.001));
    }

    [Test]
    [TestCase(0, 5, 15, 37.5, 280)]
    [TestCase(1, 10, 30, 75, 280)]
    [TestCase(2, 15, 45, 112.5, 280)]
    [TestCase(3, 20, 60, 150, 280)]
    [TestCase(4, 25, 75, 187.5, 280)]
    public void TestHitWindowRank(int rank, double expectedPerfect, double expectedGreat, double expectedGood, double expectedBad)
    {
        var windows = new BmsHitWindows(rank);
        windows.SetDifficulty(5);

        Assert.That(windows.WindowFor(HitResult.Perfect), Is.EqualTo(expectedPerfect).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Great), Is.EqualTo(expectedGreat).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Good), Is.EqualTo(expectedGood).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Ok), Is.EqualTo(expectedBad).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Meh), Is.EqualTo(expectedBad).Within(0.001));
    }

    [Test]
    public void TestHitWindowUsesLayoutAndColumnProfile()
    {
        var fiveKey = new BmsHitWindows(rank: 3, BmsLayoutVariant.Bms5K, column: 1);
        var scratch = new BmsHitWindows(rank: 3, BmsLayoutVariant.Bme7K, column: 0);

        fiveKey.SetDifficulty(5);
        scratch.SetDifficulty(5);

        Assert.That(fiveKey.WindowFor(HitResult.Good), Is.EqualTo(100).Within(0.001));
        Assert.That(scratch.WindowFor(HitResult.Perfect), Is.EqualTo(30).Within(0.001));
        Assert.That(scratch.WindowFor(HitResult.Great), Is.EqualTo(70).Within(0.001));
        Assert.That(scratch.WindowFor(HitResult.Ok), Is.EqualTo(290).Within(0.001));
    }
}
