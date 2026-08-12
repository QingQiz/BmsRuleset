#nullable enable

using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

[TestFixture]
public class BmsGameplayTextEventControllerTest
{
    [TestCase(null)]
    [TestCase("")]
    public void TestMistakeWithoutTextDoesNotTrigger(string? mistakeText)
    {
        var controller = new BmsGameplayTextEventController(new BmsTextEvents(mistakeText, []));
        var received = new List<string>();

        controller.TriggerMistake(received.Add);

        Assert.That(received, Is.Empty);
    }

    [Test]
    public void TestMistakeWithTextTriggers()
    {
        var controller = new BmsGameplayTextEventController(new BmsTextEvents("mistake", []));
        var received = new List<string>();

        controller.TriggerMistake(received.Add);

        Assert.That(received, Is.EqualTo(["mistake"]));
    }
}
