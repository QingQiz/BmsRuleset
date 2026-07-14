using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Screens.Play.HUD;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.SkinTest;

[TestFixture]
public class BmsDefaultHudTest
{
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
