using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

[TestFixture]
public class BmsGameplayEventsTest
{

    [Test]
    public void TestRaiseJudgementDisplayedInvokesSubscribers()
    {
        var events = new BmsGameplayEvents();
        HitResult? received = null;
        events.JudgementDisplayed += r => received = r;

        events.RaiseJudgementDisplayed(HitResult.Great);

        Assert.That(received, Is.EqualTo(HitResult.Great));
    }

    [Test]
    public void TestRaiseScrollSpeedChangedInvokesSubscribers()
    {
        var events = new BmsGameplayEvents();
        double? received = null;
        events.ScrollSpeedChanged += m => received = m;

        events.RaiseScrollSpeedChanged(1.5);

        Assert.That(received, Is.EqualTo(1.5));
    }

    [Test]
    public void TestRaiseTextInvokesSubscribers()
    {
        var events = new BmsGameplayEvents();
        string? received = null;
        events.Text += s => received = s;

        events.RaiseText("Game Start");

        Assert.That(received, Is.EqualTo("Game Start"));
    }
}
