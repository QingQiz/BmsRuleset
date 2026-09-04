using System;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Course;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Result.Course;

[TestFixture]
public class BmsCourseCountdownTest
{
    [Test]
    public void TestCountdownUsesAbsoluteElapsedTime()
    {
        var clock = new TestClock { Current = DateTimeOffset.UnixEpoch };
        var countdown = new BmsCourseCountdown(() => clock.Current);

        countdown.Start();
        Assert.That(countdown.Update().SecondsRemaining, Is.EqualTo(99));

        clock.Current = clock.Current.AddSeconds(54.25);
        Assert.That(countdown.Update().SecondsRemaining, Is.EqualTo(45));

        clock.Current = clock.Current.AddSeconds(44.75);
        Assert.That(countdown.Update().HasExpired, Is.True);
    }

    [Test]
    public void TestCueSecondsPlayOnlyOnce()
    {
        var clock = new TestClock { Current = DateTimeOffset.UnixEpoch };
        var countdown = new BmsCourseCountdown(() => clock.Current);
        countdown.Start();

        foreach (var cueSecond in BmsCourseCountdown.CUE_SECONDS)
        {
            clock.Current = DateTimeOffset.UnixEpoch.AddSeconds(BmsCourseCountdown.DURATION_SECONDS - cueSecond);

            Assert.That(countdown.Update().PlayCue, Is.True, $"Expected a cue at {cueSecond} seconds.");
            Assert.That(countdown.Update().PlayCue, Is.False, $"Cue at {cueSecond} seconds should only play once.");
        }
    }

    [Test]
    public void TestCountdownCannotUpdateBeforeStart()
    {
        var countdown = new BmsCourseCountdown();
        Assert.Throws<InvalidOperationException>(() => countdown.Update());
    }

    private sealed class TestClock
    {
        internal DateTimeOffset Current { get; set; }
    }
}
