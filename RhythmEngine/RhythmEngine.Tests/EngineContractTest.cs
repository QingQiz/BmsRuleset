using NUnit.Framework;
using RhythmEngine.Profiles.Patterns;

namespace RhythmEngine.Tests;

[TestFixture]
public class EngineContractTest
{
    [TestCase(-20001, null)]
    [TestCase(-20000, "outer")]
    [TestCase(-10001, "outer")]
    [TestCase(-10000, "inner")]
    [TestCase(10000, "inner")]
    [TestCase(10001, "outer")]
    [TestCase(30000, "outer")]
    [TestCase(30001, null)]
    public void AsymmetricWindowBoundaries(long offset, string? expected)
    {
        var windows = new TimingWindows([new TimingWindow("inner", -10000, 10000), new TimingWindow("outer", -20000, 30000)]);
        Assert.That(windows.ResultFor(offset), Is.EqualTo(expected));
    }

    [Test]
    public void NonNestedWindowsAndExclusiveEndpoints()
    {
        var windows = new TimingWindows([new TimingWindow("late", 10, 20, false, false), new TimingWindow("early", -20, -10)]);
        Assert.Multiple(() =>
        {
            Assert.That(windows.ResultFor(10), Is.Null);
            Assert.That(windows.ResultFor(11), Is.EqualTo("late"));
            Assert.That(windows.ResultFor(20), Is.Null);
            Assert.That(windows.ResultFor(-15), Is.EqualTo("early"));
            Assert.That(windows.ResultFor(0), Is.Null);
        });
    }

    [Test]
    public void DeadlineIsInclusiveAndSilenceProducesOneMiss()
    {
        var session = createTapSession();
        session.AdvanceTo(1030000);
        Assert.That(session.EventCount, Is.Zero);
        session.AdvanceTo(1030001);
        session.AdvanceTo(2000000);
        Assert.That(session.GetEventsSince(0).Select(e => (e.Time, e.Result)), Is.EqualTo([(1030001L, "miss")]));

        var atBoundary = createTapSession();
        atBoundary.AdvanceTo(1030000, [new InputEvent(1030000, 0, true)]);
        Assert.That(atBoundary.GetEventsSince(0).Single().Result, Is.EqualTo("hit"));

        var afterBoundary = createTapSession();
        afterBoundary.AdvanceTo(1030001, [new InputEvent(1030001, 0, true)]);
        Assert.That(afterBoundary.GetEventsSince(0).Single().Result, Is.EqualTo("miss"));
    }

    [Test]
    public void BatchValidationPrecedesAnyMutation()
    {
        var session = createTapSession();
        Assert.Throws<ArgumentException>(() => session.AdvanceTo(1000000, [new InputEvent(1000000, 0, true), new InputEvent(1000000, 1, true)]));
        Assert.That(session.EventCount, Is.Zero);
        Assert.That(session.CurrentTime, Is.Null);
        Assert.Throws<ArgumentException>(() => session.AdvanceTo(1000000, [new InputEvent(1000000, 0, true), new InputEvent(999999, 0, false)]));
        Assert.That(session.EventCount, Is.Zero);
    }

    [Test]
    public void TimestampClosesAfterAdvance()
    {
        var session = createTapSession();
        session.AdvanceTo(1000000);
        Assert.Throws<ArgumentException>(() => session.AdvanceTo(1000000, [new InputEvent(1000000, 0, true)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.AdvanceTo(999999));
    }

    [Test]
    public void RepeatedPhysicalPressDoesNotHitAnotherNote()
    {
        var rule = tapRule();
        var chart = new Chart(1, [new ChartNote(0, 0, 1000000, 1000000, rule), new ChartNote(1, 0, 1000000, 1000000, rule)]);
        var session = new JudgementSession(chart, configuration());
        session.AdvanceTo(1000000, [new InputEvent(1000000, 0, true), new InputEvent(1000000, 0, true)]);
        Assert.That(session.EventCount, Is.EqualTo(1));
        session.AdvanceTo(1000001, [new InputEvent(1000001, 0, true, 1)]);
        Assert.That(session.EventCount, Is.EqualTo(2));
    }

    [TestCase(TimerOrder.BeforeInput, "avoided")]
    [TestCase(TimerOrder.AfterInput, "mine")]
    public void SameTimeTimerOrderIsConfigured(TimerOrder order, string expectedMine)
    {
        var chart = new Chart(1, [
            new ChartNote(0, 0, 1000000, 1000000, new MineRule(0, 0, "mine", "avoided", order)),
            new ChartNote(1, 0, 1000000, 1000000, tapRule())
        ]);
        var session = new JudgementSession(chart, configuration());
        session.AdvanceTo(1000000, [new InputEvent(1000000, 0, true)]);
        Assert.That(session.GetEventsSince(0).Select(e => e.Result), Is.EqualTo([expectedMine, "hit"]));
    }

    [Test]
    public void SimultaneousInputKeepsSourceOrder()
    {
        var chart = new Chart(2, [new ChartNote(7, 0, 1000000, 1000000, tapRule()), new ChartNote(3, 1, 1000000, 1000000, tapRule())]);
        var session = new JudgementSession(chart, configuration());
        session.AdvanceTo(1000000, [new InputEvent(1000000, 1, true), new InputEvent(1000000, 0, true)]);
        Assert.That(session.GetEventsSince(0).Select(e => (e.Sequence, e.NoteId)), Is.EqualTo([(0L, 3), (1L, 7)]));
    }

    [Test]
    public void SimultaneousAutomaticEventsKeepChartSourceOrder()
    {
        var chart = new Chart(1, [new ChartNote(7, 0, 1000000, 1000000, tapRule()), new ChartNote(3, 0, 1000000, 1000000, tapRule())]);
        var session = new JudgementSession(chart, configuration());
        session.AdvanceTo(2000000);
        Assert.That(session.GetEventsSince(0).Select(e => e.NoteId), Is.EqualTo([7, 3]));
    }

    [Test]
    public void ResetReplayAndCloneAreIndependent()
    {
        var session = createTapSession();
        session.AdvanceTo(900000, [new InputEvent(900000, 0, true)]);
        var clone = session.Clone();
        session.AdvanceTo(2000000);
        clone.AdvanceTo(2000000, [new InputEvent(999999, 0, false), new InputEvent(1000000, 0, true)]);
        Assert.That(session.GetEventsSince(0).Single().Result, Is.EqualTo("miss"));
        Assert.That(clone.GetEventsSince(0).Single().Result, Is.EqualTo("hit"));
        var expected = clone.GetEventsSince(0);
        session.Reset();
        session.AdvanceTo(2000000, [new InputEvent(900000, 0, true), new InputEvent(999999, 0, false), new InputEvent(1000000, 0, true)]);
        Assert.That(session.GetEventsSince(0), Is.EqualTo(expected));
        Assert.That(session.Score, Is.EqualTo(clone.Score));
    }

    [Test]
    public void PartitioningSilenceDoesNotChangeResults()
    {
        var whole = createTapSession();
        var partitioned = createTapSession();
        whole.AdvanceTo(2000000);
        for (var time = 0L; time <= 2000000; time += 1000)
            partitioned.AdvanceTo(time);
        Assert.That(partitioned.GetEventsSince(0), Is.EqualTo(whole.GetEventsSince(0)));
        Assert.That(partitioned.Health, Is.EqualTo(whole.Health));
    }

    [Test]
    public void HealthBoundsAndFailureAreLatched()
    {
        var rule = tapRule();
        var chart = new Chart(1, [new ChartNote(0, 0, 1000000, 1000000, rule), new ChartNote(1, 0, 2000000, 2000000, rule)]);
        var config = configuration() with { Health = HealthRules.LatchedGauge(0, 1, 0.5, c => c.Event.Result == "hit" ? 2 : -2, v => v <= 0, v => v > 0) };
        var session = new JudgementSession(chart, config);
        session.AdvanceTo(1500000);
        Assert.That(session.Health, Is.EqualTo(new HealthState(0, true)));
        session.AdvanceTo(3000000, [new InputEvent(2000000, 0, true)]);
        Assert.That(session.Health, Is.EqualTo(new HealthState(1, true)));
        Assert.That(session.Cleared, Is.False);
    }

    [Test]
    public void ChartAndWindowsSnapshotCallerCollections()
    {
        var windows = new[] { new TimingWindow("hit", -30000, 30000) };
        var table = new TimingWindows(windows);
        windows[0] = new TimingWindow("changed", 0, 0);
        var notes = new List<ChartNote> { new(0, 0, 1000000, 1000000, new TapRule(new TapConfiguration(table, 30000, "miss", 1))) };
        var chart = new Chart(1, notes);
        notes.Clear();
        var session = new JudgementSession(chart, configuration());
        session.AdvanceTo(1000000, [new InputEvent(1000000, 0, true)]);
        Assert.That(session.GetEventsSince(0).Single().Result, Is.EqualTo("hit"));
    }

    [Test]
    public void InputDispatchPreservesUnmatchedRepeatedEdgesAndTimerOrder()
    {
        var session = new JudgementSession(new Chart(1,
            [new ChartNote(0, 0, 1000000, 1000000, tapRule()), new ChartNote(1, 0, 2000000, 2000000, tapRule())]), configuration());
        var dispatched = session.AdvanceTo(3000000,
        [
            new InputEvent(1000000, 0, true), new InputEvent(1000000, 0, false),
            new InputEvent(1000000, 0, true), new InputEvent(1000000, 0, true),
            new InputEvent(1000000, 0, false), new InputEvent(2500000, 0, true),
        ]);
        Assert.That(dispatched.Select(d => (d.FirstTransitionIndex, d.TransitionCount)),
            Is.EqualTo(new[] { (0, 1), (1, 0), (1, 0), (1, 0), (2, 0) }));
        Assert.That(session.GetEventsSince(0).Select(e => e.Result), Is.EqualTo(new[] { "hit", "miss" }));
        Assert.That(session.GetNote(0).Held, Is.True);
    }

    [Test]
    public void AppendedBatchKeepsClosedTimersAndValidatesBeforeMutation()
    {
        var session = new JudgementSession(new Chart(1,
        [
            new ChartNote(0, 0, 1000000, 1000000, tapRule()),
            new ChartNote(1, 0, 1000000, 1000000, tapRule()),
            new ChartNote(2, 0, 1000000, 1000000, new MineRule(0, 0, "mine", "avoided", TimerOrder.AfterInput, acceptsInput: false)),
        ]), configuration());
        session.AdvanceTo(1000000, [new InputEvent(1000000, 0, true)]);
        Assert.Throws<ArgumentException>(() => session.AppendInputs([new InputEvent(1000000, 0, false), new InputEvent(999999, 0, true)]));
        Assert.That(session.GetNote(0).Held, Is.True);
        var dispatched = session.AppendInputs([new InputEvent(1000000, 0, false), new InputEvent(1000000, 0, true)]);
        Assert.That(dispatched.Select(d => d.TransitionCount), Is.EqualTo(new[] { 0, 1 }));
        Assert.That(session.GetEventsSince(0).Select(e => e.Result), Is.EqualTo(new[] { "hit", "mine", "hit" }));
    }

    [Test]
    public void LayeredHealthSnapshotsRemainIndependentAcrossCloneAndReset()
    {
        var initial = new HealthState(.5, false) { Layers = [new(.5, false), new(.8, false)] };
        var config = configuration() with
        {
            Health = new HealthConfiguration(initial, c => c.Health with
            {
                Layers = [.. c.Health.Layers.Select(layer => layer with { Value = layer.Value + (c.Event.Result == "hit" ? .1 : -.2) })],
            }, _ => SessionOutcome.InProgress),
        };
        var session = new JudgementSession(new Chart(1, [new ChartNote(0, 0, 1000000, 1000000, tapRule())]), config);
        var clone = session.Clone();
        session.AdvanceTo(2000000, [new InputEvent(1000000, 0, true)]);
        var snapshot = session.Health;
        clone.AdvanceTo(2000000);
        session.Reset();
        Assert.That(snapshot.Layers.Select(layer => layer.Value), Is.EqualTo(new[] { .6, .9 }).Within(1e-9));
        Assert.That(clone.Health.Layers.Select(layer => layer.Value), Is.EqualTo(new[] { .3, .6 }).Within(1e-9));
        Assert.That(session.Health, Is.EqualTo(initial));
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void InvalidClockInputIsRejected(double time) => Assert.Throws<ArgumentOutOfRangeException>(() => Time.FromMilliseconds(time));

    private static TapRule tapRule() => new(new TapConfiguration(new TimingWindows([new TimingWindow("hit", -30000, 30000)]), 30000, "miss", 1));

    private static JudgementSession createTapSession() => new(new Chart(1, [new ChartNote(0, 0, 1000000, 1000000, tapRule())]), configuration());

    private static SessionConfiguration configuration() => new("test-unfamiliar-results", new ScoreConfiguration(new ScoreState(),
            (state, context) => context.Opportunity == null ? state : state with { Score = state.Score + (context.Event.Result == "hit" ? 1 : 0) },
            plan => plan.MaximumAccuracy), HealthRules.LatchedGauge(0, 1, 1, _ => 0, _ => false, _ => true),
        context => [.. context.Candidates.Where(c => c.Shared), .. context.Candidates.Where(c => !c.Shared).OrderBy(c => c.Priority).ThenBy(c => c.StartTime).ThenBy(c => c.NoteIndex).Take(1)]);
}
