#nullable enable

using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.UI;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

[TestFixture]
public class BmsTextEventManagerTest
{
    [TestCase(null)]
    [TestCase("")]
    public void TestMistakeWithoutTextDoesNotTrigger(string? mistakeText)
    {
        var manager = new BmsTextEventManager(new BmsTextEvents(mistakeText, []));
        var received = new List<string>();

        manager.TriggerMistake(received.Add);

        Assert.That(received, Is.Empty);
    }

    [Test]
    public void TestMistakeWithTextTriggers()
    {
        var manager = new BmsTextEventManager(new BmsTextEvents("mistake", []));
        var received = new List<string>();

        manager.TriggerMistake(received.Add);

        Assert.That(received, Is.EqualTo(["mistake"]));
    }
}
