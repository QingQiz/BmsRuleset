using NUnit.Framework;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using RhythmEngine.Profiles.Patterns;

namespace RhythmEngine.Tests;

[TestFixture]
public class EngineContractTest
{
    [Test]
    public void BatchesOwnTheirOutputAcrossAdvancesCloneAndReset()
    {
        var chart = new Chart(1,
        [
            new ChartNote(7, 0, 1000000, 1000000, tapRule()),
            new ChartNote(3, 0, 2000000, 2000000, tapRule()),
        ], [new ScoringOpportunity(7, NoteParts.TAP, 1), new ScoringOpportunity(3, NoteParts.TAP, 1)]);
        var session = new JudgementSession(chart, configuration());
        var first = session.AdvanceTo(1000000, [new InputEvent(1000000, 0, true)]);
        var clone = session.Clone();
        var second = session.AdvanceTo(2000000, [new InputEvent(1500000, 0, false), new InputEvent(2000000, 0, true)]);
        var missed = clone.AdvanceTo(2100000);
        session.Reset();

        Assert.That(first.Transitions.Single().Sequence, Is.Zero);
        Assert.That(first.Transitions.Single().After.Id, Is.EqualTo(7));
        Assert.That(first.Transitions.Single().Judgements.Single().Score.Score, Is.EqualTo(1));
        Assert.That(second.Inputs.Select(i => i.FirstTransitionIndex), Is.EqualTo([0, 0]));
        Assert.That(second.Transitions.Single().Sequence, Is.EqualTo(1));
        Assert.That(second.Transitions.Single().Judgements.Single().Event.Sequence, Is.EqualTo(1));
        Assert.That(second.Transitions.Single().Judgements.Single().Score.Score, Is.EqualTo(2));
        Assert.That(missed.Transitions.Single().Judgements.Single().Event.Result.Value, Is.EqualTo("miss"));
        Assert.That(clone.GetNote(3).Completed, Is.True);
        Assert.That(session.GetNote(7).Completed, Is.False);
        Assert.That(session.EventCount, Is.Zero);
    }

    [Test]
    public void RepeatedNonConsumingInputsKeepOneDeadlineAcrossCloneAndResolution()
    {
        var empty = new ResultId("empty");
        var hit = new ResultId("hit");
        var miss = new ResultId("miss");
        var rule = new TapRule(new TapConfiguration(new TimingWindows([new(hit, -30000, 30000), new(empty, -500000, -30001)]),
            30000, miss, NonResolvingResults: [empty]));
        var session = new JudgementSession(new Chart(1, [new ChartNote(7, 0, 1000000, 1000000, rule)], []), configuration());
        var early = session.AdvanceTo(700000,
        [
            new InputEvent(600000, 0, true), new InputEvent(600001, 0, false),
            new InputEvent(700000, 0, true),
        ]);
        var clone = session.Clone();
        var resolved = session.AdvanceTo(2000000, [new InputEvent(700001, 0, false), new InputEvent(1000000, 0, true)]);
        var expired = clone.AdvanceTo(2000000);

        Assert.That(early.Transitions.SelectMany(t => t.Judgements).Select(j => j.Event.Result), Is.EqualTo([empty, empty]));
        Assert.That(resolved.Transitions.SelectMany(t => t.Judgements).Select(j => j.Event.Result), Is.EqualTo([hit]));
        Assert.That(expired.Transitions.SelectMany(t => t.Judgements).Select(j => (j.Event.Time, j.Event.Result)), Is.EqualTo([(1030001L, miss)]));
    }

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
        var windows = new TimingWindows([new TimingWindow(new ResultId("inner"), -10000, 10000), new TimingWindow(new ResultId("outer"), -20000, 30000)]);
        Assert.That(windows.ResultFor(offset)?.Value, Is.EqualTo(expected));
    }

    [Test]
    public void NonNestedWindowsAndExclusiveEndpoints()
    {
        var windows = new TimingWindows([new TimingWindow(new ResultId("late"), 10, 20, false, false), new TimingWindow(new ResultId("early"), -20, -10)]);
        Assert.Multiple(() =>
        {
            Assert.That(windows.ResultFor(10)?.Value, Is.Null);
            Assert.That(windows.ResultFor(11)?.Value, Is.EqualTo("late"));
            Assert.That(windows.ResultFor(20)?.Value, Is.Null);
            Assert.That(windows.ResultFor(-15)?.Value, Is.EqualTo("early"));
            Assert.That(windows.ResultFor(0)?.Value, Is.Null);
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
        Assert.That(session.GetEventsSince(0).Select(e => (e.Time, e.Result.Value)), Is.EqualTo([(1030001L, "miss")]));

        var atBoundary = createTapSession();
        atBoundary.AdvanceTo(1030000, [new InputEvent(1030000, 0, true)]);
        Assert.That(atBoundary.GetEventsSince(0).Single().Result.Value, Is.EqualTo("hit"));

        var afterBoundary = createTapSession();
        afterBoundary.AdvanceTo(1030001, [new InputEvent(1030001, 0, true)]);
        Assert.That(afterBoundary.GetEventsSince(0).Single().Result.Value, Is.EqualTo("miss"));
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
        var chart = new Chart(1, [new ChartNote(0, 0, 1000000, 1000000, rule), new ChartNote(1, 0, 1000000, 1000000, rule)], []);
        var session = new RecordingSession(chart, configuration());
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
            new ChartNote(0, 0, 1000000, 1000000, new MineRule(0, 0, new ResultId("mine"), new ResultId("avoided"), order)),
            new ChartNote(1, 0, 1000000, 1000000, tapRule())
        ], []);
        var session = new RecordingSession(chart, configuration());
        session.AdvanceTo(1000000, [new InputEvent(1000000, 0, true)]);
        Assert.That(session.GetEventsSince(0).Select(e => e.Result.Value), Is.EqualTo([expectedMine, "hit"]));
    }

    [Test]
    public void SimultaneousInputKeepsSourceOrder()
    {
        var chart = new Chart(2, [new ChartNote(7, 0, 1000000, 1000000, tapRule()), new ChartNote(3, 1, 1000000, 1000000, tapRule())], []);
        var session = new RecordingSession(chart, configuration());
        session.AdvanceTo(1000000, [new InputEvent(1000000, 1, true), new InputEvent(1000000, 0, true)]);
        Assert.That(session.GetEventsSince(0).Select(e => (e.Sequence, e.NoteId)), Is.EqualTo([(0L, 3), (1L, 7)]));
    }

    [Test]
    public void UnsortedLaneKeepsCandidatesAtTheEarlyBoundaryInSourceOrder()
    {
        var chart = new Chart(1,
        [
            new ChartNote(3, 0, 2_000_000, 2_000_000, tapRule()),
            new ChartNote(7, 0, 1_000_000, 1_000_000, tapRule()),
            new ChartNote(5, 0, 1_000_000, 1_000_000, tapRule()),
        ], []);
        var session = new JudgementSession(chart, configuration() with { SelectCandidates = context => context.Candidates });
        var batch = session.AdvanceTo(970_000, [new InputEvent(970_000, 0, true)]);

        Assert.That(batch.Transitions.Select(t => t.After.Id), Is.EqualTo([7, 5]));
    }

    [Test]
    public void ScoringPlanDoesNotChangeJudgementRules()
    {
        var note = new ChartNote(7, 0, 1_000_000, 1_000_000, tapRule());
        var unscoredChart = new Chart(1, [note], []);
        var scoredChart = new Chart(1, [note], [new ScoringOpportunity(note.Id, NoteParts.TAP, 3)]);
        var unscoredSession = new JudgementSession(unscoredChart, configuration());
        var scoredSession = new JudgementSession(scoredChart, configuration());

        var unscoredBatch = unscoredSession.AdvanceTo(1_000_000, [new InputEvent(1_000_000, 0, true)]);
        var scoredBatch = scoredSession.AdvanceTo(1_000_000, [new InputEvent(1_000_000, 0, true)]);

        Assert.That(unscoredChart.ScorePlan.Opportunities, Is.Empty);
        Assert.That(scoredChart.ScorePlan.MaximumAccuracy, Is.EqualTo(3));
        Assert.That(unscoredBatch.Transitions.Single().Judgements.Single().Event.Result,
            Is.EqualTo(scoredBatch.Transitions.Single().Judgements.Single().Event.Result));
    }

    [Test]
    public void SimultaneousAutomaticEventsKeepChartSourceOrder()
    {
        var chart = new Chart(1, [new ChartNote(7, 0, 1000000, 1000000, tapRule()), new ChartNote(3, 0, 1000000, 1000000, tapRule())], []);
        var session = new RecordingSession(chart, configuration());
        session.AdvanceTo(2000000);
        Assert.That(session.GetEventsSince(0).Select(e => e.NoteId), Is.EqualTo([7, 3]));
    }

    [Test]
    public void SessionsUsingOneChartKeepIndependentJudgementsAndSourceOrder()
    {
        var chart = new Chart(4,
        [
            new ChartNote(42, 2, 1000000, 1000000, tapRule()),
            new ChartNote(7, 0, 1000000, 1000000, tapRule()),
            new ChartNote(-9, 2, 1000000, 1000000, tapRule()),
        ],
        [
            new ScoringOpportunity(42, NoteParts.TAP, 1), new ScoringOpportunity(7, NoteParts.TAP, 1),
            new ScoringOpportunity(-9, NoteParts.TAP, 1),
        ]);
        var first = new RecordingSession(chart, configuration());
        var second = new RecordingSession(chart, configuration());

        first.AdvanceTo(1000000, [new InputEvent(1000000, 2, true)]);
        second.AdvanceTo(2000000);

        Assert.That(first.GetNote(42).Completed, Is.True);
        Assert.That(first.GetNote(-9).Completed, Is.False);
        Assert.That(first.GetNote(7).Completed, Is.False);
        Assert.That(first.Score.Score, Is.EqualTo(1));
        Assert.That(second.GetEventsSince(0).Select(e => e.NoteId), Is.EqualTo([42, 7, -9]));
        Assert.That(second.Score.Score, Is.Zero);

        first.Reset();
        first.AdvanceTo(1000000, [new InputEvent(1000000, 0, true)]);
        Assert.That(first.GetEventsSince(0).Single().NoteId, Is.EqualTo(7));
        Assert.That(second.Completed, Is.True);
        Assert.That(second.EventCount, Is.EqualTo(3));
    }

    [Test]
    public void ResetReplayAndCloneAreIndependent()
    {
        var session = createTapSession();
        session.AdvanceTo(900000, [new InputEvent(900000, 0, true)]);
        var clone = session.Clone();
        session.AdvanceTo(2000000);
        clone.AdvanceTo(2000000, [new InputEvent(999999, 0, false), new InputEvent(1000000, 0, true)]);
        Assert.That(session.GetEventsSince(0).Single().Result.Value, Is.EqualTo("miss"));
        Assert.That(clone.GetEventsSince(0).Single().Result.Value, Is.EqualTo("hit"));
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
        var chart = new Chart(1, [new ChartNote(0, 0, 1000000, 1000000, rule), new ChartNote(1, 0, 2000000, 2000000, rule)], []);
        var config = configuration() with { Health = HealthRules.LatchedGauge(0, 1, 0.5, c => c.Event.Result.Value == "hit" ? 2 : -2, v => v <= 0, v => v > 0) };
        var session = new RecordingSession(chart, config);
        session.AdvanceTo(1500000);
        Assert.That(session.Health, Is.EqualTo(new HealthState(0, true)));
        session.AdvanceTo(3000000, [new InputEvent(2000000, 0, true)]);
        Assert.That(session.Health, Is.EqualTo(new HealthState(1, true)));
        Assert.That(session.Cleared, Is.False);
    }

    [Test]
    public void ChartAndWindowsSnapshotCallerCollections()
    {
        var windows = new[] { new TimingWindow(new ResultId("hit"), -30000, 30000) };
        var table = new TimingWindows(windows);
        windows[0] = new TimingWindow(new ResultId("changed"), 0, 0);
        var notes = new List<ChartNote> { new(0, 0, 1000000, 1000000, new TapRule(new TapConfiguration(table, 30000, new ResultId("miss")))) };
        var chart = new Chart(1, notes, []);
        notes.Clear();
        var session = new RecordingSession(chart, configuration());
        session.AdvanceTo(1000000, [new InputEvent(1000000, 0, true)]);
        Assert.That(session.GetEventsSince(0).Single().Result.Value, Is.EqualTo("hit"));
    }

    [Test]
    public void InputDispatchPreservesUnmatchedRepeatedEdgesAndTimerOrder()
    {
        var session = new RecordingSession(new Chart(1,
            [new ChartNote(0, 0, 1000000, 1000000, tapRule()), new ChartNote(1, 0, 2000000, 2000000, tapRule())], []), configuration());
        var dispatched = session.AdvanceTo(3000000,
        [
            new InputEvent(1000000, 0, true), new InputEvent(1000000, 0, false),
            new InputEvent(1000000, 0, true), new InputEvent(1000000, 0, true),
            new InputEvent(1000000, 0, false), new InputEvent(2500000, 0, true),
        ]);
        Assert.That(dispatched.Inputs.Select(d => (d.FirstTransitionIndex, d.TransitionCount)),
            Is.EqualTo([(0, 1), (1, 0), (1, 0), (1, 0), (2, 0)]));
        Assert.That(session.GetEventsSince(0).Select(e => e.Result.Value), Is.EqualTo(["hit", "miss"]));
        Assert.That(session.GetNote(0).Held, Is.True);
    }

    [Test]
    public void AppendedBatchKeepsClosedTimersAndValidatesBeforeMutation()
    {
        var session = new RecordingSession(new Chart(1,
        [
            new ChartNote(0, 0, 1000000, 1000000, tapRule()),
            new ChartNote(1, 0, 1000000, 1000000, tapRule()),
            new ChartNote(2, 0, 1000000, 1000000, new MineRule(0, 0, new ResultId("mine"), new ResultId("avoided"), acceptsInput: false)),
        ], []), configuration());
        session.AdvanceTo(1000000, [new InputEvent(1000000, 0, true)]);
        Assert.Throws<ArgumentException>(() => session.AppendInputs([new InputEvent(1000000, 0, false), new InputEvent(999999, 0, true)]));
        Assert.That(session.GetNote(0).Held, Is.True);
        var dispatched = session.AppendInputs([new InputEvent(1000000, 0, false), new InputEvent(1000000, 0, true)]);
        Assert.That(dispatched.Inputs.Select(d => d.TransitionCount), Is.EqualTo([0, 1]));
        Assert.That(session.GetEventsSince(0).Select(e => e.Result.Value), Is.EqualTo(["hit", "mine", "hit"]));
    }

    [Test]
    public void LayeredHealthSnapshotsRemainIndependentAcrossCloneAndReset()
    {
        var initial = new HealthState(.5, false) { Layers = [new(.5, false), new(.8, false)] };
        var config = configuration() with
        {
            Health = new HealthConfiguration(initial, c => c.Health with
            {
                Layers = [.. c.Health.Layers.Select(layer => layer with { Value = layer.Value + (c.Event.Result.Value == "hit" ? .1 : -.2) })],
            }, _ => SessionOutcome.InProgress),
        };
        var session = new RecordingSession(new Chart(1, [new ChartNote(0, 0, 1000000, 1000000, tapRule())], []), config);
        var clone = session.Clone();
        session.AdvanceTo(2000000, [new InputEvent(1000000, 0, true)]);
        var snapshot = session.Health;
        clone.AdvanceTo(2000000);
        session.Reset();
        Assert.That(snapshot.Layers.Select(layer => layer.Value), Is.EqualTo([.6, .9]).Within(1e-9));
        Assert.That(clone.Health.Layers.Select(layer => layer.Value), Is.EqualTo([.3, .6]).Within(1e-9));
        Assert.That(session.Health, Is.EqualTo(initial));
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void InvalidClockInputIsRejected(double time) => Assert.Throws<ArgumentOutOfRangeException>(() => Time.FromMilliseconds(time));

    [TestCase(1500, false)]
    [TestCase(1500, true)]
    [TestCase(1950, false)]
    [TestCase(1950, true)]
    public void HostEndAndReleaseKeepTailResolutionSeparateFromBodyShutdown(long time, bool hostEnd)
    {
        var hit = new ResultId("hit");
        var drop = new ResultId("drop");
        var windows = new TimingWindows([new(hit, -100, 100)]);
        var rule = new HoldRule(new HoldConfiguration(windows, windows, 100, 100, new ResultId("miss"), hit, drop,
            HoldEndMode.Release, HoldDropMode.Resolve, e => new NoteEmission(new PartId("tail"), e.TailResult, e.Note.EndTime)));
        var session = new RecordingSession(new Chart(1, [new ChartNote(7, 0, 1000, 2000, rule)],
            [new ScoringOpportunity(7, NoteParts.TAIL, 1)]), configuration());
        session.AdvanceTo(1000, [new InputEvent(1000, 0, true)]);
        var active = session.GetNote(7);

        if (hostEnd)
            session.EndAt(time);
        else
            session.AdvanceTo(time, [new InputEvent(time, 0, false)]);

        var snapshot = session.GetNote(7);
        var tail = snapshot.Parts.Single(p => p.Part == NoteParts.TAIL);
        var body = snapshot.Parts.Single(p => p.Part == NoteParts.BODY);
        Assert.That(snapshot.Completed, Is.True);
        Assert.That(body.Active, Is.False);
        Assert.That(body.Reason, Is.EqualTo(hostEnd ? HoldReasons.HOST_END : time < 1900 ? HoldReasons.DROP : null));
        Assert.That(tail.Reason, Is.EqualTo(time < 1900 ? HoldReasons.DROP : HoldReasons.RELEASE));
        Assert.That(tail.Result, Is.EqualTo(time < 1900 ? drop : hit));
        Assert.That(session.GetEventsSince(0).Single(e => e.Part == NoteParts.TAIL).Result, Is.EqualTo(tail.Result));
        Assert.That(session.Score.Score, Is.EqualTo(time < 1900 ? 0 : 1));
        Assert.That(active.Parts.Single(p => p.Part == NoteParts.TAIL).Reason, Is.Null);

        var clone = session.Clone();
        session.Reset();
        Assert.That(clone.GetNote(7).Parts, Is.EqualTo(snapshot.Parts));
        Assert.That(session.GetNote(7).Parts.All(p => !p.Resolved && p.Reason == null), Is.True);
    }

    private static TapRule tapRule() => new(new TapConfiguration(new TimingWindows([new TimingWindow(new ResultId("hit"), -30000, 30000)]), 30000, new ResultId("miss")));

    private static RecordingSession createTapSession() => new(new Chart(1, [new ChartNote(0, 0, 1000000, 1000000, tapRule())], []), configuration());

    private static SessionConfiguration configuration() => new("test-unfamiliar-results", new ScoreConfiguration(new ScoreState(),
            (state, context) => context.Opportunity == null ? state : state with { Score = state.Score + (context.Event.Result.Value == "hit" ? 1 : 0) },
            plan => plan.MaximumAccuracy), HealthRules.LatchedGauge(0, 1, 1, _ => 0, _ => false, _ => true),
        context => [.. context.Candidates.Where(c => c.Shared), .. context.Candidates.Where(c => !c.Shared).OrderBy(c => c.Priority).ThenBy(c => c.StartTime).ThenBy(c => c.NoteIndex).Take(1)]);
}
