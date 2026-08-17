using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Gauge;

[TestFixture]
public class BmsGaugeProfileTest
{
    [Test]
    public void TestNormalProfileMatchesBeatorajaBadDelta()
    {
        var profile = BmsGaugeProfileFactory.Create(BmsGaugeType.Normal);

        Assert.That(profile.InitialHealth, Is.EqualTo(0.2).Within(0.0001));
        Assert.That(profile.ClearThreshold, Is.EqualTo(0.8).Within(0.0001));
        Assert.That(profile.BadDelta, Is.EqualTo(-0.03).Within(0.0001));
        Assert.That(profile.PoorDelta, Is.EqualTo(-0.06).Within(0.0001));
        Assert.That(profile.EmptyPoorDelta, Is.EqualTo(-0.02).Within(0.0001));
        Assert.That(profile.Display.ColourMode, Is.EqualTo(BmsGaugeColourMode.GrooveDynamic));
        Assert.That(profile.Display.ShowClearLine, Is.True);
    }

    [Test]
    public void TestHardProfilesUseDistinctFixedColours()
    {
        var hard = BmsGaugeProfileFactory.Create(BmsGaugeType.Hard);
        var exHard = BmsGaugeProfileFactory.Create(BmsGaugeType.ExHard);
        var hazard = BmsGaugeProfileFactory.Create(BmsGaugeType.Hazard);

        Assert.That(hard.Display.ColourMode, Is.EqualTo(BmsGaugeColourMode.Fixed));
        Assert.That(exHard.Display.ColourMode, Is.EqualTo(BmsGaugeColourMode.Fixed));
        Assert.That(hazard.Display.ColourMode, Is.EqualTo(BmsGaugeColourMode.Fixed));

        Assert.That(hard.Display.FillColour, Is.EqualTo(new Color4(220, 55, 50, 255)));
        Assert.That(exHard.Display.FillColour, Is.EqualTo(new Color4(195, 55, 210, 255)));
        Assert.That(hazard.Display.FillColour, Is.EqualTo(new Color4(255, 215, 0, 255)));

        Assert.That(hard.Display.ShowClearLine, Is.False);
        Assert.That(exHard.Display.ShowClearLine, Is.False);
        Assert.That(hazard.Display.ShowClearLine, Is.False);
    }

    [Test]
    public void TestCourseProfilesUseHardGaugeDisplay()
    {
        var hardColour = BmsGaugeProfileFactory.Create(BmsGaugeType.Hard).Display.FillColour;

        BmsGaugeType[] courseGaugeTypes = [BmsGaugeType.Class, BmsGaugeType.ExClass, BmsGaugeType.ExHardClass];

        foreach (var type in courseGaugeTypes)
        {
            var profile = BmsGaugeProfileFactory.Create(type);

            Assert.That(profile.Algorithm, Is.EqualTo(BmsGaugeAlgorithm.Fixed));
            Assert.That(profile.Display.ColourMode, Is.EqualTo(BmsGaugeColourMode.Fixed));
            Assert.That(profile.Display.FillColour, Is.EqualTo(hardColour));
            Assert.That(profile.Display.ShowClearLine, Is.False);
        }
    }

    [Test]
    public void TestHazardDisplayIsGoldAndHasNoClearLine()
    {
        var display = BmsGaugeProfileFactory.Create(BmsGaugeType.Hazard).Display;

        Assert.That(display.ColourMode, Is.EqualTo(BmsGaugeColourMode.Fixed));
        Assert.That(display.FillColour, Is.EqualTo(new Color4(255, 215, 0, 255)));
        Assert.That(display.ClearThreshold, Is.Null);
        Assert.That(display.ShowClearLine, Is.False);
    }
}
