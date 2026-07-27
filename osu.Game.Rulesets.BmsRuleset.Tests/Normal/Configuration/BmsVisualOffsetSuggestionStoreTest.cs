using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Configuration;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Configuration;

[TestFixture]
public class BmsVisualOffsetSuggestionStoreTest
{
    [TestCase(20, 10, 30)]
    [TestCase(-20, 10, -10)]
    [TestCase(100, 450, 500)]
    [TestCase(-100, -450, -500)]
    public void TestSuggestionFollowsVisualOffsetDirection(double medianHitError, double visualOffset, double expected)
    {
        var store = new BmsVisualOffsetSuggestionStore();

        Assert.That(store.Add(medianHitError, visualOffset), Is.EqualTo(expected));
        Assert.That(store.History[^1].SuggestedVisualOffset, Is.EqualTo(expected));
    }

    [Test]
    public void TestClearRemovesHistory()
    {
        var store = new BmsVisualOffsetSuggestionStore();
        store.Add(20, 0);

        store.Clear();

        Assert.That(store.History, Is.Empty);
    }
}
