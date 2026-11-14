using NUnit.Framework;
using RhythmEngine.Core;
using RhythmEngine.Hosting;
using RhythmEngine.Profiles.Common;
using RhythmEngine.Profiles.Malody;
using RhythmEngine.Profiles.Mania;
using RhythmEngine.Profiles.O2Jam;

namespace RhythmEngine.Tests;

[TestFixture]
public class GameFacingContractTest
{
    [Test]
    public void IncrementalFramesContainOnlyNewResults()
    {
        var session = createSession();
        var hit = session.AdvanceTo(1000000, [new InputEvent(1000000, 0, true)]);
        var silence = session.AdvanceTo(3000000);
        Assert.That(hit.Events.Single().Result.Value, Is.EqualTo("perfect"));
        Assert.That(hit.Events.Single().Lane, Is.Zero);
        Assert.That(hit.Events.Single().ExpectedTime, Is.EqualTo(1000000));
        Assert.That(hit.IsSnapshot, Is.False);
        Assert.That(silence.Events, Is.Empty);
        Assert.That(silence.Generation, Is.EqualTo(hit.Generation));
        Assert.That(session.GetNote(10).Completed, Is.True);
        Assert.That(session.ProfileId, Is.EqualTo("osu-lazer-mania-standardised"));
    }

    [Test]
    public void ReconstructionMarksHistoricalResultsAndReplacesPresentationGeneration()
    {
        var session = createSession();
        var missed = session.AdvanceTo(3000000);
        var replayed = session.Reconstruct(2000000, [new InputEvent(1000000, 0, true)]);
        Assert.That(missed.Events.Single().Result.Value, Is.EqualTo("miss"));
        Assert.That(replayed.Events.Single().Result.Value, Is.EqualTo("perfect"));
        Assert.That(replayed.Generation, Is.GreaterThan(missed.Generation));
        Assert.That(replayed.IsSnapshot, Is.True);
        Assert.That(replayed.Score.Score, Is.EqualTo(1000000));
        Assert.That(session.Current.Events, Is.Empty);
    }

    [Test]
    public void RejectedReconstructionPreservesLiveSession()
    {
        var session = createSession();
        var before = session.AdvanceTo(1000000, [new InputEvent(1000000, 0, true)]);
        Assert.Throws<ArgumentException>(() => session.Reconstruct(2000000, [new InputEvent(1000000, 100, true)]));
        Assert.That(session.Current.Score, Is.EqualTo(before.Score));
        Assert.That(session.Current.Generation, Is.EqualTo(before.Generation));
        Assert.That(session.Current.Time, Is.EqualTo(before.Time));
    }

    [Test]
    public void RestartAndCloneDoNotSharePlaybackState()
    {
        var session = createSession();
        var copy = session.Clone();
        session.AdvanceTo(2000000, [new InputEvent(1000000, 0, true)]);
        copy.AdvanceTo(2000000);
        Assert.That(session.Current.Score.Score, Is.EqualTo(1000000));
        Assert.That(copy.Current.Score.Score, Is.Zero);
        var restarted = session.Restart();
        Assert.That(restarted.IsSnapshot, Is.True);
        Assert.That(restarted.Time, Is.Null);
        Assert.That(restarted.Score.Score, Is.Zero);
        Assert.That(copy.Current.Time, Is.EqualTo(2000000));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NativeHoldProfilesCloneAndResetIndependentTailState(bool o2Jam)
    {
        var profile = o2Jam
            ? O2Jam338Profile.Create(1, [new O2JamTickNote(0, 0, 192, 288)],
                new O2JamClock([new O2JamClockPoint(0, 0), new O2JamClockPoint(4000, 384)]))
            : MalodyProfile.Create(new KeyChart(1, [new KeyNote(0, 0, 2000000, 3000000, KeyNoteKind.Hold)]),
                new MalodyOptions(Client: MalodyClient.VPro, JudgeAtEnd: true));
        var session = profile.CreateSession();
        session.AdvanceTo(2500000, [new InputEvent(2000000, 0, true)]);
        var clone = session.Clone();
        var prefix = session.GetEventsSince(0);
        session.AdvanceTo(3500000, [new InputEvent(3000000, 0, false)]);
        clone.AdvanceTo(3500000);
        Assert.That(session.GetEventsSince(0).Single(e => e.Part.Value == "tail").Result.Value, Is.EqualTo(o2Jam ? "cool" : "best"));
        Assert.That(clone.GetEventsSince(0).Single(e => e.Part.Value == "tail").Result.Value, Is.EqualTo("miss"));
        Assert.That(session.Score.Score, Is.GreaterThan(clone.Score.Score));
        Assert.That(prefix, Is.EqualTo(session.GetEventsSince(0).Take(prefix.Length)));
        var events = session.GetEventsSince(0);
        var score = session.Score.Score;
        session.Reset();
        session.AdvanceTo(3500000, [new InputEvent(2000000, 0, true), new InputEvent(3000000, 0, false)]);
        Assert.That(session.GetEventsSince(0), Is.EqualTo(events));
        Assert.That(session.Score.Score, Is.EqualTo(score));
        Assert.That(session.Completed && clone.Completed, Is.True);
    }

    private static IGameplaySession createSession()
    {
        var profile = ManiaProfiles.CreateLazer(new KeyChart(1, [new KeyNote(10, 0, 1000000, 1000000)]));
        return new GameplaySession(profile.Chart, profile.Configuration);
    }
}
