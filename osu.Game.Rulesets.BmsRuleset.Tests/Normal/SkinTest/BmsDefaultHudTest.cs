using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Screens.Play.HUD;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.SkinTest;

[TestFixture]
public class BmsDefaultHudTest
{
    [Test]
    public void TestSongProgressDefaultsToLeftOfPlayfield()
    {
        var hud = BmsDefaultHud.GetDrawableComponent(new GlobalSkinnableContainerLookup(
            GlobalSkinnableContainers.Playfield,
            new BmsRuleset().RulesetInfo));

        Assert.That(hud, Is.Not.Null);

        var progress = hud!.ChildrenOfType<BmsSongProgress>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(progress.Anchor, Is.EqualTo(Anchor.TopLeft));
            Assert.That(progress.Origin, Is.EqualTo(Anchor.TopRight));
            Assert.That(progress.RelativeSizeAxes, Is.EqualTo(Axes.Y));
            Assert.That(progress.Margin.Right, Is.Zero);
            Assert.That(progress.Width, Is.EqualTo(8));
            Assert.That(progress.IndicatorColour.Value, Is.EqualTo(new Colour4(255, 45, 45, 255)));
        });
    }

    [TestCase(0, 0)]
    [TestCase(0.5, 238)]
    [TestCase(1, 476)]
    [TestCase(-1, 0)]
    [TestCase(2, 476)]
    public void TestSongProgressMarkerPosition(double progress, float expected)
    {
        Assert.That(BmsSongProgress.CalculateMarkerPosition(progress, 500 - 24), Is.EqualTo(expected));
    }

    [Test]
    public void TestSongProgressColourSurvivesLayoutRoundTrip()
    {
        var progress = new BmsSongProgress();
        var expected = new Colour4(255, 80, 140, 200);
        progress.IndicatorColour.Value = expected;

        var restored = (BmsSongProgress)progress.CreateSerialisedInfo().CreateInstance();

        Assert.Multiple(() =>
        {
            Assert.That(restored.IndicatorColour.Value, Is.EqualTo(expected));
            Assert.That(progress.ChildrenOfType<Container>().Count(container => container.EdgeEffect.Colour.Equals(expected)), Is.EqualTo(1));
        });
    }

    [Test]
    public void TestSongProgressUsesRoundedVerticalGlowLayers()
    {
        var progress = new BmsSongProgress();
        var blurred = progress.ChildrenOfType<BufferedContainer>().Single();
        var indicatorLayers = progress.ChildrenOfType<Container>()
                                      .Where(container => container.Masking && container.Size == new Vector2(8, 24))
                                      .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(indicatorLayers, Has.Length.EqualTo(2));
            Assert.That(indicatorLayers, Has.All.Property(nameof(Container.CornerRadius)).EqualTo(3));
            Assert.That(blurred.BlurSigma, Is.EqualTo(new Vector2(6)));
            Assert.That(blurred.EffectBlending, Is.EqualTo(BlendingParameters.Additive));
            Assert.That(blurred.DrawOriginal, Is.False);
        });
    }

    [Test]
    public void TestScoreAndAccuracySpacingSurvivesLayoutRoundTrip()
    {
        var hud = BmsDefaultHud.GetDrawableComponent(new GlobalSkinnableContainerLookup(
            GlobalSkinnableContainers.MainHUDComponents,
            new BmsRuleset().RulesetInfo));

        Assert.That(hud, Is.Not.Null);

        var score = hud!.ChildrenOfType<ArgonScoreCounter>().Single();
        var accuracy = hud.ChildrenOfType<ArgonAccuracyCounter>().Single();
        var restoredScore = (ArgonScoreCounter)score.CreateSerialisedInfo().CreateInstance();
        var restoredAccuracy = (ArgonAccuracyCounter)accuracy.CreateSerialisedInfo().CreateInstance();

        Assert.That(restoredAccuracy.Y - restoredScore.Y, Is.EqualTo(60));
    }
}
