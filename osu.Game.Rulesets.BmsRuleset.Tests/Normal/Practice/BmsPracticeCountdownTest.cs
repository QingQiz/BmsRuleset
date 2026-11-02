using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Practice;

[TestFixture]
public class BmsPracticeCountdownTest
{
    [Test]
    public void PreparationAndSuspensionNeverConsumeCountIn()
    {
        var countdown = new BmsPracticeCountdown();
        countdown.Advance(5000);
        countdown.Skip();
        Assert.That(countdown.HasFinished, Is.False);

        countdown.Ready(3, paused: false);
        countdown.Advance(1250);
        Assert.That(countdown.SecondsRemaining, Is.EqualTo(2));
        countdown.Pause();
        countdown.Advance(10_000);
        countdown.Skip();
        Assert.That(countdown.RemainingMilliseconds, Is.EqualTo(1750));
        Assert.That(countdown.HasFinished, Is.False);

        countdown.Resume();
        countdown.Advance(1750);
        Assert.That(countdown.HasFinished, Is.True);
    }

    [Test]
    public void CancellationDisarmsAutomaticStartAndNextAttemptGetsFullDuration()
    {
        var countdown = new BmsPracticeCountdown();
        countdown.Ready(3, paused: false);
        countdown.Advance(2900);
        countdown.Cancel();
        countdown.Advance(1000);
        Assert.That(countdown.HasFinished, Is.False);

        countdown.Ready(3, paused: true);
        Assert.That(countdown.RemainingMilliseconds, Is.EqualTo(3000));
        Assert.That(countdown.HasFinished, Is.False);
        countdown.Resume();
        countdown.Skip();
        Assert.That(countdown.HasFinished, Is.True);
    }

    [Test]
    public void ZeroCountInStillRequiresPreparationAndExplicitResume()
    {
        var countdown = new BmsPracticeCountdown();
        Assert.That(countdown.HasFinished, Is.False);
        countdown.Ready(0, paused: true);
        Assert.That(countdown.HasFinished, Is.False);
        countdown.Resume();
        Assert.That(countdown.HasFinished, Is.True);
    }
}
