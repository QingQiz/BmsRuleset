using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
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
    public void TestCourseProfilesUseTieredGaugeDisplay()
    {
        var hardColour = BmsGaugeProfileFactory.Create(BmsGaugeType.Hard).Display.FillColour;
        var exHardColour = BmsGaugeProfileFactory.Create(BmsGaugeType.ExHard).Display.FillColour;
        var hazardColour = BmsGaugeProfileFactory.Create(BmsGaugeType.Hazard).Display.FillColour;

        var expectedColours = new Dictionary<BmsGaugeType, Color4>
        {
            [BmsGaugeType.Class] = hardColour,
            [BmsGaugeType.ExClass] = exHardColour,
            [BmsGaugeType.ExHardClass] = hazardColour,
        };

        foreach (var (type, expectedColour) in expectedColours)
        {
            var profile = BmsGaugeProfileFactory.Create(type);

            Assert.That(profile.Algorithm, Is.EqualTo(BmsGaugeAlgorithm.Fixed));
            Assert.That(profile.Display.ColourMode, Is.EqualTo(BmsGaugeColourMode.Fixed));
            Assert.That(profile.Display.FillColour, Is.EqualTo(expectedColour));
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

    [Test]
    public void TestCourseGaugeFamiliesUseBeatorajaClassValues()
    {
        var fiveKeyClass = BmsGaugeProfileFactory.Create(BmsGaugeType.Class, BmsGaugeProfileFamily.FiveKeys);
        var lr2ExClass = BmsGaugeProfileFactory.Create(BmsGaugeType.ExClass, BmsGaugeProfileFamily.Lr2);

        Assert.Multiple(() =>
        {
            Assert.That(fiveKeyClass.PerfectGain, Is.EqualTo(0.0001).Within(0.000001));
            Assert.That(fiveKeyClass.BadDelta, Is.EqualTo(-0.005).Within(0.000001));
            Assert.That(lr2ExClass.PerfectGain, Is.EqualTo(0.001).Within(0.000001));
            Assert.That(lr2ExClass.PoorDelta, Is.EqualTo(-0.10).Within(0.000001));
            Assert.That(lr2ExClass.GutsRules.Single().HealthThreshold, Is.EqualTo(0.30).Within(0.000001));
        });
    }

    [TestCase(BmsLayoutVariant.Bms5K, BmsGaugeProfileFamily.FiveKeys)]
    [TestCase(BmsLayoutVariant.Bme7K, BmsGaugeProfileFamily.SevenKeys)]
    [TestCase(BmsLayoutVariant.Pms9K, BmsGaugeProfileFamily.Pms)]
    public void TestLayoutSelectsGaugeProfileFamily(BmsLayoutVariant layout, BmsGaugeProfileFamily expected)
    {
        Assert.That(BmsGaugeProfileFamilyProvider.FromLayout(layout), Is.EqualTo(expected));
    }
}
