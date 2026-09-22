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
            Assert.That(progress.Anchor, Is.EqualTo(Anchor.BottomLeft));
            Assert.That(progress.Origin, Is.EqualTo(Anchor.BottomRight));
            Assert.That(progress.RelativeSizeAxes, Is.EqualTo(Axes.Y));
            Assert.That(progress.X, Is.EqualTo(-15));
            Assert.That(progress.Width, Is.EqualTo(4));
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

    [Test]
    public void TestMainHudUsesBmsHitErrorMeter()
    {
        var hud = BmsDefaultHud.GetDrawableComponent(new GlobalSkinnableContainerLookup(
            GlobalSkinnableContainers.MainHUDComponents,
            new BmsRuleset().RulesetInfo));

        Assert.That(hud, Is.Not.Null);
        var meter = hud!.ChildrenOfType<BmsHitErrorMeter>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(meter.AutoSizeAxes, Is.EqualTo(Axes.None));
            Assert.That(meter.Width, Is.Zero);
            Assert.That(meter.Height, Is.EqualTo(12 * LegacySkin.STABLE_MAGIC_SCALE_FACTOR));
            Assert.That(meter.Scale, Is.EqualTo(Vector2.One));
            Assert.That(meter.Rotation, Is.Zero);
            Assert.That(meter.Origin, Is.EqualTo(Anchor.BottomCentre));
            Assert.That(meter.ShowEmptyPoor.Value, Is.True);
            Assert.That(meter.ShowPoor.Value, Is.True);
            Assert.That(meter.JudgementFadeDuration.Value, Is.EqualTo(10));
            Assert.That(meter.JudgementLineThickness.Value, Is.EqualTo(3));
            Assert.That(meter.BackgroundOpacity.Value, Is.EqualTo(0.6f));
        });
    }

    [Test]
    public void TestJudgementDisplayShowEmptyPoorSurvivesLayoutRoundTrip()
    {
        var hud = BmsDefaultHud.GetDrawableComponent(new GlobalSkinnableContainerLookup(
            GlobalSkinnableContainers.Playfield,
            new BmsRuleset().RulesetInfo));

        Assert.That(hud, Is.Not.Null);

        var display = hud!.ChildrenOfType<BmsJudgementDisplay>().Single();
        Assert.That(display.ShowEmptyPoor.Value, Is.True);

        display.ShowEmptyPoor.Value = false;
        var restored = (BmsJudgementDisplay)display.CreateSerialisedInfo().CreateInstance();

        Assert.That(restored.ShowEmptyPoor.Value, Is.False);
    }

    [Test]
    public void TestHitErrorMeterIndependentSizeSurvivesLayoutRoundTrip()
    {
        var meter = new BmsHitErrorMeter
        {
            Width = 320,
            Height = 52,
            Scale = new Vector2(2),
            Position = new Vector2(25, -10),
            Rotation = 90,
            Anchor = Anchor.BottomCentre,
            Origin = Anchor.BottomCentre,
        };
        meter.ShowEmptyPoor.Value = false;
        meter.ShowPoor.Value = false;
        meter.JudgementFadeDuration.Value = 1.5f;
        meter.BackgroundOpacity.Value = 0.25f;

        var saved = meter.CreateSerialisedInfo();
        saved.Settings["judgement_line_thickness"] = 8f;
        saved.Settings["colour_bar_visibility"] = false;
        saved.Settings["show_moving_average"] = false;
        saved.Settings["centre_marker_style"] = 1;
        saved.Settings["label_style"] = 2;
        var restored = (BmsHitErrorMeter)saved.CreateInstance();
        string[] expectedSettings = ["judgement_line_thickness", "background_opacity", "judgement_fade_duration", "show_empty_poor", "show_poor"];

        Assert.Multiple(() =>
        {
            Assert.That(restored.Size, Is.EqualTo(new Vector2(320, 52)));
            Assert.That(restored.Scale, Is.EqualTo(meter.Scale));
            Assert.That(restored.Position, Is.EqualTo(meter.Position));
            Assert.That(restored.Rotation, Is.EqualTo(meter.Rotation));
            Assert.That(restored.Anchor, Is.EqualTo(meter.Anchor));
            Assert.That(restored.Origin, Is.EqualTo(meter.Origin));
            Assert.That(restored.ShowEmptyPoor.Value, Is.False);
            Assert.That(restored.ShowPoor.Value, Is.False);
            Assert.That(restored.JudgementFadeDuration.Value, Is.EqualTo(1.5f));
            Assert.That(restored.JudgementLineThickness.Value, Is.EqualTo(8));
            Assert.That(restored.BackgroundOpacity.Value, Is.EqualTo(0.25f));
            Assert.That(restored.CreateSerialisedInfo().Settings.Keys, Is.EquivalentTo(expectedSettings));
        });
    }
}
