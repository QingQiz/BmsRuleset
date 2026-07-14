using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.UI;

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
}
