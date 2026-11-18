using NUnit.Framework;
using RhythmEngine.Core;
using RhythmEngine.Hosting;
using RhythmEngine.Profiles.Bms;
using RhythmEngine.Profiles.Common;
using RhythmEngine.Profiles.Etterna;
using RhythmEngine.Profiles.Malody;
using RhythmEngine.Profiles.Mania;
using RhythmEngine.Profiles.O2Jam;
using RhythmEngine.Profiles.Patterns;
using RhythmEngine.Tests.Hosting;

namespace RhythmEngine.Tests;

[TestFixture]
public class HostLifecycleTest
{
    [Test]
    public void SilentHeadAndRegrabsReachTheSameHostInSourceOrder()
    {
        var session = createStableHold();
        var host = new RecordingHost();
        host.Present(session.Current);
        var frame = session.AdvanceTo(2_000_000,
        [
            new(1_000_000, 0, true),
            new(1_040_000, 0, false),
            new(1_040_000, 0, true),
            new(2_000_000, 0, false),
        ]);
        host.Present(frame);

        var head = frame.Transitions[0];
        Assert.That(head.Judgements, Is.Empty);
        Assert.That(head.Input, Is.EqualTo(new InputEvent(1_000_000, 0, true)));
        Assert.That(host.Audio, Is.EqualTo(new (long, int, PartId, bool)[]
        {
            (1_000_000, 10, NoteParts.BODY, true), (1_040_000, 10, NoteParts.BODY, false),
            (1_040_000, 10, NoteParts.BODY, true), (2_000_000, 10, NoteParts.BODY, false),
        }));
        Assert.That(host.Notes[10].Completed, Is.True);
        Assert.That(host.Notes[10].Parts.All(p => p.Resolved && !p.Active), Is.True);
        Assert.That(host.Judgements.Select(j => j.Event), Is.EqualTo(frame.Events));
        Assert.That(host.Judgements[^1].Score, Is.EqualTo(frame.Score));
        Assert.That(host.Judgements[^1].Health, Is.EqualTo(frame.Health));
        Assert.That(host.Judgements.Select(j => j.Score.Combo).Distinct().Count(), Is.GreaterThan(1));
        Assert.That(frame.Transitions.Select(t => t.Sequence), Is.EqualTo(Enumerable.Range(0, frame.Transitions.Length).Select(i => (long)i)));
    }

    [Test]
    public void PartitioningDoesNotLoseTransitionsOrReplayAudioOnSeek()
    {
        InputEvent[] inputs = [new(1_000_000, 0, true), new(1_040_000, 0, false), new(1_070_000, 0, true), new(2_000_000, 0, false)];
        var whole = createStableHold();
        var partitioned = createStableHold();
        var host = new RecordingHost();
        var splitHost = new RecordingHost();
        host.Present(whole.Current);
        splitHost.Present(partitioned.Current);
        host.Present(whole.AdvanceTo(2_100_000, inputs));
        foreach (var input in inputs)
            splitHost.Present(partitioned.AdvanceTo(input.Time, [input]));
        splitHost.Present(partitioned.AdvanceTo(2_100_000));
        Assert.That(splitHost.Audio, Is.EqualTo(host.Audio));
        Assert.That(splitHost.Judgements, Has.Count.EqualTo(host.Judgements.Count));
        foreach (var (actual, expected) in splitHost.Judgements.Zip(host.Judgements))
        {
            Assert.That(actual.Event, Is.EqualTo(expected.Event));
            Assert.That(actual.Health, Is.EqualTo(expected.Health));
            Assert.That(actual.Score.Counters, Is.EquivalentTo(expected.Score.Counters));
            Assert.That(actual.Score.ResultCounts, Is.EquivalentTo(expected.Score.ResultCounts));
            Assert.That(actual.Score with { Counters = expected.Score.Counters, ResultCounts = expected.Score.ResultCounts }, Is.EqualTo(expected.Score));
        }

        var audioCount = host.Audio.Count;
        var restored = whole.Reconstruct(1_080_000, inputs.AsSpan(0, 3));
        host.Present(restored);
        Assert.That(restored.Generation, Is.EqualTo(1));
        Assert.That(host.Audio, Has.Count.EqualTo(audioCount));
        Assert.That(host.Judgements, Is.Empty);
        Assert.That(host.Notes[10].Parts.Single(p => p.Part.Value == "body").Active, Is.True);
        Assert.That(host.Score, Is.EqualTo(restored.Score));

        var clone = whole.Clone();
        var historical = restored.RestoredNotes.Single();
        host.Present(whole.AdvanceTo(2_000_000, [inputs[^1]]));
        Assert.That(clone.GetNote(10).Completed, Is.False);
        Assert.That(historical.Parts.Single(p => p.Part.Value == "body").Active, Is.True);
        Assert.That(host.Audio[^1], Is.EqualTo((2_000_000L, 10, NoteParts.BODY, false)));

        host.Present(whole.Restart());
        Assert.That(host.Notes[10].Parts.All(p => !p.Resolved && !p.Active), Is.True);
        Assert.That(whole.Current.Generation, Is.EqualTo(2));
        Assert.That(clone.Current.Generation, Is.EqualTo(1));
    }

    [TestCase("lazer")]
    [TestCase("stable")]
    [TestCase("classic")]
    [TestCase("current")]
    [TestCase("rc")]
    [TestCase("etterna")]
    [TestCase("lr2")]
    [TestCase("o2jam")]
    public void HostConsumesEveryHoldWithoutKnowingItsRuleState(string profileId)
    {
        var source = new KeyChart(1, [new KeyNote(10, 0, 1_000_000, 2_000_000, KeyNoteKind.Hold)]);
        var profile = profileId switch
        {
            "lazer" => ManiaProfiles.CreateLazer(source),
            "stable" => ManiaProfiles.CreateStable(source),
            "classic" => MalodyProfile.Create(source, new MalodyOptions(Client: MalodyClient.ClassicPc)),
            "current" => MalodyProfile.Create(source, new MalodyOptions(Client: MalodyClient.VPro)),
            "rc" => MalodyProfile.Create(source, new MalodyOptions(Client: MalodyClient.VPro, VVersion: MalodyVVersion.Rc20)),
            "etterna" => EtternaProfile.Create(source),
            "lr2" => Lr2Beta3Profile.Create(source),
            _ => O2Jam338Profile.Create(1, [new O2JamTickNote(10, 0, 96, 192)],
                new O2JamClock([new O2JamClockPoint(0, 0), new O2JamClockPoint(4000, 384)])),
        };
        IGameplaySession session = new GameplaySession(profile.Chart, profile.Configuration);
        var host = new RecordingHost();
        host.Present(session.Current);
        host.Present(session.AdvanceTo(1_000_000, [new(1_000_000, 0, true)]));
        Assert.That(host.Audio, Is.EqualTo([(1_000_000L, 10, NoteParts.BODY, true)]));
        host.Present(session.AdvanceTo(2_500_000, [new(2_000_000, 0, false)]));
        Assert.That(host.Audio, Has.Count.EqualTo(2));
        Assert.That(host.Audio[^1].Active, Is.False);
        Assert.That(host.Notes[10].Completed, Is.True);
        Assert.That(host.Notes[10].Parts.All(p => p.Resolved && !p.Active), Is.True);
    }

    [Test]
    public void ResolvedPartAndInputRetirementAreSeparate()
    {
        var rule = new TapRule(new TapConfiguration(new TimingWindows([new(new ResultId("hit"), -50, 50)]), 50, new ResultId("miss"))
        {
            ResolvedWindows = new TimingWindows([new(new ResultId("repeat"), -100, 100)]),
        });
        var session = new GameplaySession(new Chart(1, [new ChartNote(10, 0, 1_000, 1_000, rule)], []), unscoredConfiguration());
        var hit = session.AdvanceTo(1_000, [new(1_000, 0, true)]);
        Assert.That(hit.Transitions.Single().After.Parts.Single().Resolved, Is.True);
        Assert.That(hit.Completed, Is.False);
        var repeat = session.AdvanceTo(1_050, [new(1_020, 0, false), new(1_050, 0, true)]);
        Assert.That(repeat.Events.Single().Result.Value, Is.EqualTo("repeat"));
        var retired = session.AdvanceTo(1_101);
        Assert.That(retired.Transitions.Single().Judgements, Is.Empty);
        Assert.That(retired.Transitions.Single().After.Completed, Is.True);
    }

    [Test]
    public void ConfiguredRecoveryAndScoreOutcomeSurviveOneLargeFrame()
    {
        var source = new KeyChart(1, [new KeyNote(10, 0, 1_000_000, 1_000_000), new KeyNote(11, 0, 2_000_000, 2_000_000)]);
        var profile = ManiaProfiles.CreateLazer(source);
        var configuration = profile.Configuration with
        {
            Score = new ScoreConfiguration(new ScoreState(), (state, context) => state with
            {
                Score = state.Score + (context.Event.Result.Value == "perfect" ? 7 : 0),
                AccuracyPoints = state.AccuracyPoints + (context.Event.Result.Value == "perfect" ? context.Opportunity!.MaximumAccuracy : 0),
            }, _ => 14),
            Health = new HealthConfiguration(new HealthState(2, false), context =>
            {
                var next = context.Health.Value + (context.Event.Result.Value == "perfect" ? 5 : -3);
                return new HealthState(next, next < 0);
            }, context => context.Health.HasFailed ? SessionOutcome.Failed
                : context.Completed && context.Score.AccuracyPoints >= context.Plan.MaximumAccuracy / 2 ? SessionOutcome.Cleared : SessionOutcome.InProgress),
        };
        var session = new GameplaySession(profile.Chart, configuration);
        var host = new RecordingHost();
        host.Present(session.Current);
        var frame = session.AdvanceTo(2_500_000, [new(2_000_000, 0, true)]);
        host.Present(frame);
        Assert.That(host.Outcomes, Is.EqualTo([SessionOutcome.Failed, SessionOutcome.Cleared]));
        Assert.That(host.Judgements.Select(j => j.Health), Is.EqualTo([new HealthState(-1, true), new HealthState(4, false)]));
        Assert.That(host.Score.Score, Is.EqualTo(7));
        Assert.That(frame.Cleared, Is.True);
        var replayed = session.Reconstruct(2_500_000, [new(2_000_000, 0, true)]);
        Assert.That(replayed.Transitions.Select(t => t.Outcome), Is.EqualTo(host.Outcomes));
    }

    [TestCase(-40, "precise", 3)]
    [TestCase(60, "precise", 3)]
    [TestCase(61, "late", 1)]
    public void IndependentConfigurationReplacesWindowsScoresAndLifeWithoutHostChanges(long offset, string expected, int points)
    {
        var precise = new ResultId("precise");
        var late = new ResultId("late");
        var windows = new TimingWindows([new(new ResultId("precise"), -40, 60), new(new ResultId("late"), -100, 100)]);
        var rule = new TapRule(new TapConfiguration(windows, 100, new ResultId("absent")));
        var chart = new Chart(1, [new ChartNote(4, 0, 1_000, 1_000, rule)], [new ScoringOpportunity(4, NoteParts.TAP, 3)]);
        var configuration = new SessionConfiguration("independent-asymmetric", new ScoreConfiguration(new ScoreState(), (state, context) =>
            {
                var value = context.Event.Result == precise ? 3 : context.Event.Result == late ? 1 : 0;
                return state with { Score = state.Score + value, AccuracyMaximum = state.AccuracyMaximum + context.Opportunity!.MaximumAccuracy };
            }, plan => plan.MaximumAccuracy), new HealthConfiguration(new HealthState(10, false),
                context => new HealthState(context.Score.Score + 10, false),
                context => context.Completed ? SessionOutcome.Cleared : SessionOutcome.InProgress),
            context => [.. context.Candidates.OrderBy(c => c.StartTime).ThenBy(c => c.NoteIndex).Take(1)]);
        IGameplaySession session = new GameplaySession(chart, configuration);
        var host = new RecordingHost();
        host.Present(session.Current);
        host.Present(session.AdvanceTo(2_000, [new(1_000 + offset, 0, true)]));
        Assert.That(host.Judgements.Single().Event.Result, Is.EqualTo(new ResultId(expected)));
        Assert.That(host.Judgements.Single().Event.Part, Is.EqualTo(new PartId("tap")));
        Assert.That(host.Score.Score, Is.EqualTo(points));
        Assert.That(host.Score.AccuracyMaximum, Is.EqualTo(3));
        Assert.That(windows.RankOf(precise), Is.Zero);
        Assert.That(windows.RankOf(late), Is.EqualTo(1));
        Assert.That(host.Health.Value, Is.EqualTo(points + 10));
        Assert.That(host.Notes[4].Completed, Is.True);
    }

    [Test]
    public void SustainedBodyContinuesAfterItsTailDecisionAndStopsAtItsTimer()
    {
        var windows = new TimingWindows([new(new ResultId("hit"), -100, 100)]);
        var rule = new HoldRule(new HoldConfiguration(windows, windows, 100, 100, new ResultId("miss"), new ResultId("hit"), new ResultId("drop"),
            HoldEndMode.Release, HoldDropMode.Resolve, e => new NoteEmission(new PartId("tail"), e.TailResult, e.Note.EndTime))
        {
            Balance = new HoldBalance(200, new ResultId("gain"), new ResultId("loss"), []),
        });
        var configuration = new SessionConfiguration("sustained-body", new ScoreConfiguration(new ScoreState(), (state, _) => state, _ => 0),
            HealthRules.LatchedGauge(0, 10, 5, c => c.Event.Result.Value == "gain" ? 1 : c.Event.Result.Value == "loss" ? -1 : 0, _ => false, _ => true),
            context => context.Candidates);
        IGameplaySession session = new GameplaySession(new Chart(1, [new ChartNote(10, 0, 1_000, 2_000, rule)], []), configuration);
        var host = new RecordingHost();
        host.Present(session.Current);
        host.Present(session.AdvanceTo(2_001, [new(1_000, 0, true), new(1_100, 0, false), new(1_500, 0, true)]));
        Assert.That(host.Audio, Is.EqualTo(new (long, int, PartId, bool)[]
        {
            (1_000, 10, NoteParts.BODY, true), (1_100, 10, NoteParts.BODY, false), (1_500, 10, NoteParts.BODY, true), (2_000, 10, NoteParts.BODY, false),
        }));
        Assert.That(host.Judgements.Count(j => j.Event.Part.Value == "tail"), Is.EqualTo(1));
        Assert.That(host.Notes[10].Parts.All(p => p.Resolved && !p.Active), Is.True);
    }

    [Test]
    public void RetiringAnOverlappingHoldPreservesItsPartIdentity()
    {
        var profile = ManiaProfiles.CreateLazer(new KeyChart(1,
            [new KeyNote(10, 0, 1_000_000, 1_500_000, KeyNoteKind.Hold), new KeyNote(11, 0, 1_550_000, 1_550_000)]));
        IGameplaySession session = new GameplaySession(profile.Chart, profile.Configuration);
        var host = new RecordingHost();
        host.Present(session.Current);
        var retired = session.AdvanceTo(1_550_000, [new(1_550_000, 0, true)]);
        host.Present(retired);
        var transition = retired.Transitions.Last(t => t.After.Id == 10);
        Assert.That(retired.Transitions.Single(t => t.After.Id == 11).Input, Is.EqualTo(new InputEvent(1_550_000, 0, true)));
        Assert.That(transition.Input, Is.Null);
        Assert.That(transition.Time, Is.EqualTo(1_550_000));
        Assert.That(transition.Before.Completed, Is.False);
        Assert.That(transition.After.Completed, Is.True);
        Assert.That(host.Notes[10].Parts.Select(p => p.Part), Is.EqualTo([NoteParts.HEAD, NoteParts.BODY, NoteParts.TAIL]));
        Assert.That(host.Notes[10].Parts.All(p => p.Resolved && !p.Active), Is.True);
    }

    [Test]
    public void SameTimeExpiryAndInputKeepOrderAndLaneIdentity()
    {
        var rule = new TapRule(new TapConfiguration(new TimingWindows([new(new ResultId("perfect"), -1, 1)]), 1, new ResultId("miss")));
        var chart = new Chart(2, [
            new ChartNote(7, 0, 1_000, 1_000, rule), new ChartNote(3, 0, 1_000, 1_000, rule),
            new ChartNote(9, 1, 1_002, 1_002, rule)
        ], []);
        IGameplaySession session = new GameplaySession(chart, unscoredConfiguration());
        var host = new RecordingHost();
        host.Present(session.Current);
        var frame = session.AdvanceTo(1_002, [new(1_002, 1, true)]);
        host.Present(frame);
        Assert.That(frame.Transitions.Select(t => t.After.Id), Is.EqualTo([7, 3, 9]));
        Assert.That(frame.Transitions.Select(t => t.Input.HasValue), Is.EqualTo([false, false, true]));
        Assert.That(host.Notes[7].Lane, Is.Zero);
        Assert.That(host.Notes[3].Lane, Is.Zero);
        Assert.That(host.Notes[9].Lane, Is.EqualTo(1));
        Assert.That(host.Notes.Values.All(n => n.Completed), Is.True);
    }

    private static SessionConfiguration unscoredConfiguration()
        => new("unscored", new ScoreConfiguration(new ScoreState(), (state, _) => state, _ => 0),
            HealthRules.LatchedGauge(0, 1, 1, _ => 0, _ => false, _ => true),
            context => [.. context.Candidates.OrderBy(c => c.StartTime).ThenBy(c => c.NoteIndex).Take(1)]);

    private static IGameplaySession createStableHold()
    {
        var profile = ManiaProfiles.CreateStable(new KeyChart(1, [new KeyNote(10, 0, 1_000_000, 2_000_000, KeyNoteKind.Hold)]));
        return new GameplaySession(profile.Chart, profile.Configuration);
    }
}
