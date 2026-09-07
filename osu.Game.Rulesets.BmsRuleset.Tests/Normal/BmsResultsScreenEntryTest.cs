using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.UI.Result;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Screens.Ranking;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal;

[TestFixture]
public class BmsResultsScreenEntryTest
{
    private ScoreInfo score = null!;

    [SetUp]
    public void SetUp() => score = new ScoreInfo { Ruleset = new BmsRuleset().RulesetInfo };

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void TestSoloResultsPreserveScoreAndCapabilities(bool retry, bool replay)
    {
        using var original = new SoloResultsScreen(score)
        {
            AllowRetry = retry,
            AllowWatchingReplay = replay,
        };
        using var replacement = BmsResultsScreenEntryPatcher.GetReplacement(original);

        Assert.Multiple(() =>
        {
            Assert.That(BmsResultsScreenEntryPatcher.IsInstalled, Is.True);
            Assert.That(replacement, Is.Not.Null);
            Assert.That(replacement!.Score, Is.SameAs(score));
            Assert.That(replacement.SelectedScore.Value, Is.SameAs(score));
            Assert.That(replacement.AllowRetry, Is.EqualTo(retry));
            Assert.That(replacement.AllowWatchingReplay, Is.EqualTo(replay));
        });
    }

    [Test]
    public void TestOtherRulesetKeepsNativeResults()
    {
        score.Ruleset = new RulesetInfo { ShortName = "mania" };
        using var screen = new SoloResultsScreen(score);
        Assert.That(BmsResultsScreenEntryPatcher.GetReplacement(screen), Is.Null);
    }

    [Test]
    public void TestSpecialisedNativeResultsAreNotReplaced()
    {
        using var screen = new SpectatorResultsScreen(score);
        Assert.That(BmsResultsScreenEntryPatcher.GetReplacement(screen), Is.Null);
    }

    [Test]
    public void TestOwnedScreenIsNotReplaced()
    {
        using var screen = new BmsResultsScreen(score);
        Assert.That(BmsResultsScreenEntryPatcher.GetReplacement(screen), Is.Null);
    }

    [Test]
    public void TestGameplayRequestRetainsOwnedScreen()
    {
        using var screen = new BmsResultsScreen(null);
        using var request = new BmsResultsScreenRequest(screen);
        Assert.That(BmsResultsScreenEntryPatcher.GetReplacement(request), Is.SameAs(screen));
    }
}
