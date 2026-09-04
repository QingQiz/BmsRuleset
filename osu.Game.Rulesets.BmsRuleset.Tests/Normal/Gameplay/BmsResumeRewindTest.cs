using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

[TestFixture]
public class BmsResumeRewindTest
{
    [TestCase(0, 5000)]
    [TestCase(100, 2109.375)]
    [TestCase(200, 625)]
    [TestCase(400, 0)]
    [TestCase(800, 0)]
    public void TestVisualOffsetUsesFixedOutCubicAnimation(double elapsed, double expectedOffset)
    {
        Assert.That(BmsPlayfield.ComputeResumeRewindVisualOffset(5000, elapsed), Is.EqualTo(expectedOffset).Within(0.001));
    }

    [TestCase(1000, 20, 1, 0, 1020)]
    [TestCase(1000, -20, 1, 0, 980)]
    [TestCase(1000, 20, 1.5, 0, 1030)]
    [TestCase(1000, 20, 1, 5000, 6020)]
    public void TestDisplayTimeIncludesRateAdjustedVisualOffset(double currentTime, double visualOffset,
                                                                double playbackRate, double resumeRewindOffset, double expected)
    {
        Assert.That(BmsPlayfield.ComputeDisplayTime(currentTime, visualOffset, playbackRate, resumeRewindOffset), Is.EqualTo(expected));
    }
}
