using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.SkinTest;

[TestFixture]
public class BmsScoreGraphTest
{
    [Test]
    public void TestIncludedInDefaultBmsHud()
    {
        var hud = BmsDefaultHud.GetDrawableComponent(new GlobalSkinnableContainerLookup(
            GlobalSkinnableContainers.MainHUDComponents,
            new BmsRuleset().RulesetInfo));

        Assert.That(hud, Is.Not.Null);
        Assert.That(hud!.ChildrenOfType<BmsScoreGraph>().Single(), Is.Not.Null);
    }

    [Test]
    public void TestDefaultsToLeftSideOfHud()
    {
        var graph = new BmsScoreGraph();

        Assert.Multiple(() =>
        {
            Assert.That(graph.Anchor, Is.EqualTo(Anchor.CentreLeft));
            Assert.That(graph.Origin, Is.EqualTo(Anchor.CentreLeft));
            Assert.That(graph.Position, Is.EqualTo(new Vector2(12, 0)));
            Assert.That(graph.Size, Is.EqualTo(new Vector2(196, 480)));
        });
    }

    [Test]
    public void TestColoursSurviveLayoutRoundTrip()
    {
        var graph = new BmsScoreGraph();
        graph.CurrentColour.Value = new Colour4(10, 20, 30, 255);
        graph.PersonalBestColour.Value = new Colour4(40, 50, 60, 255);
        graph.TargetColour.Value = new Colour4(70, 80, 90, 255);
        graph.ShowBars.Value = false;
        graph.ShowJudgementComparison.Value = false;
        graph.Height = 98;

        var restored = (BmsScoreGraph)graph.CreateSerialisedInfo().CreateInstance();

        Assert.Multiple(() =>
        {
            Assert.That(restored.CurrentColour.Value, Is.EqualTo(graph.CurrentColour.Value));
            Assert.That(restored.PersonalBestColour.Value, Is.EqualTo(graph.PersonalBestColour.Value));
            Assert.That(restored.TargetColour.Value, Is.EqualTo(graph.TargetColour.Value));
            Assert.That(restored.ShowBars.Value, Is.False);
            Assert.That(restored.ShowScoreDifference.Value, Is.True);
            Assert.That(restored.ShowJudgementComparison.Value, Is.False);
            Assert.That(restored.Height, Is.EqualTo(98));
        });
    }

    [Test]
    public void TestAtLeastOneSectionRemainsVisible()
    {
        var graph = new BmsScoreGraph();

        graph.ShowBars.Value = false;
        graph.ShowScoreDifference.Value = false;
        graph.ShowJudgementComparison.Value = false;

        Assert.That(graph.ShowJudgementComparison.Value, Is.True);
    }

    [Test]
    public void TestSizeCannotShrinkPastLayoutLimits()
    {
        var graph = new BmsScoreGraph
        {
            Size = new Vector2(1),
        };

        Assert.That(graph.Size, Is.EqualTo(new Vector2(160, 76)));

        graph.Width = 20;
        graph.Height = 30;

        Assert.That(graph.Size, Is.EqualTo(new Vector2(160, 76)));
    }

    [TestCase(true, true, true, 248)]
    [TestCase(false, true, true, 174)]
    [TestCase(true, false, true, 152)]
    [TestCase(true, true, false, 172)]
    [TestCase(true, false, false, 76)]
    [TestCase(false, true, false, 98)]
    [TestCase(false, false, true, 78)]
    public void TestMinimumHeightTracksVisibleSections(bool showBars, bool showScoreDifference, bool showJudgementComparison, float expected)
    {
        Assert.That(
            BmsScoreGraph.CalculateMinimumHeight(showBars, showScoreDifference, showJudgementComparison),
            Is.EqualTo(expected));
    }
}
