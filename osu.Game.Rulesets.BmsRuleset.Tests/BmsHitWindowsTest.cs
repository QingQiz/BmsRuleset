using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
public class BmsHitWindowsTest
{
    [Test]
    public void TestHitWindowDefaultRankIsNormal()
    {
        var windows = new BmsHitWindows();
        windows.SetDifficulty(5);

        Assert.That(windows.WindowFor(HitResult.Perfect), Is.EqualTo(18).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Great), Is.EqualTo(40).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Good), Is.EqualTo(100).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Ok), Is.EqualTo(200).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Meh), Is.EqualTo(200).Within(0.001));
    }

    [Test]
    [TestCase(0, 8, 24, 40)]
    [TestCase(1, 15, 30, 60)]
    [TestCase(2, 18, 40, 100)]
    [TestCase(3, 21, 60, 120)]
    [TestCase(4, 21, 60, 200)]
    public void TestHitWindowRank(int rank, double expectedPerfect, double expectedGreat, double expectedGood)
    {
        var windows = new BmsHitWindows(rank);
        windows.SetDifficulty(5);

        Assert.That(windows.WindowFor(HitResult.Perfect), Is.EqualTo(expectedPerfect).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Great), Is.EqualTo(expectedGreat).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Good), Is.EqualTo(expectedGood).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Ok), Is.EqualTo(200).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Meh), Is.EqualTo(200).Within(0.001));
    }
}
