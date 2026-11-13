using System.Collections.Immutable;

namespace RhythmEngine.Core;

/// <summary>
/// A single-threaded deterministic session. AdvanceTo closes its timestamp: inputs at that timestamp
/// must be submitted together, in source order. Timers can explicitly run before or after those inputs.
/// No wall clock, audio, rendering, framework or source chart types enter this runtime.
/// </summary>
public sealed class JudgementSession
{
    private readonly Chart chart;
    private readonly SessionConfiguration configuration;
    private readonly ImmutableArray<int>[] laneNotes;
    private readonly Dictionary<int, int> noteIndices;
    private readonly Dictionary<(int, string), ScoringOpportunity> opportunities;
    private readonly NoteState[] states;
    private readonly int[] revisions;
    private readonly HashSet<int>[] heldControls;
    private readonly PriorityQueue<(int Index, int Revision), (long Time, TimerOrder Order, int Index)> timers = new();
    private readonly List<NoteTransition> transitions = [];
    private int remainingNotes;

    public long? CurrentTime { get; private set; }

    public ScoreState Score { get; private set; }

    public HealthState Health { get; private set; }

    public double MaximumScore { get; }

    public bool Completed => remainingNotes == 0;

    public SessionOutcome Outcome => configuration.Health.EvaluateOutcome(new SessionOutcomeContext(Completed, Health, Score, chart.ScorePlan));

    public bool Cleared => Outcome == SessionOutcome.Cleared;

    public int EventCount { get; private set; }

    public int TransitionCount => transitions.Count;

    public JudgementSession(Chart chart, SessionConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.ProfileId))
            throw new ArgumentException("A named profile is required.", nameof(configuration));

        this.chart = chart;
        this.configuration = configuration;
        laneNotes = Enumerable.Range(0, chart.LaneCount)
            .Select(lane => chart.Notes.Select((n, i) => (n, i)).Where(n => n.n.Lane == lane).Select(n => n.i).ToImmutableArray())
            .ToArray();
        noteIndices = chart.Notes.Select((n, i) => (n.Id, i)).ToDictionary(n => n.Id, n => n.i);
        opportunities = chart.ScorePlan.Opportunities.ToDictionary(o => (o.NoteId, o.Part));
        states = new NoteState[chart.Notes.Length];
        revisions = new int[states.Length];
        heldControls = Enumerable.Range(0, chart.LaneCount).Select(_ => new HashSet<int>()).ToArray();
        Score = configuration.Score.InitialState;
        MaximumScore = configuration.Score.MaximumScore(chart.ScorePlan);
        if (!double.IsFinite(MaximumScore) || MaximumScore < 0)
            throw new ArgumentException("The score configuration must declare finite nonnegative score potential.", nameof(configuration));

        Reset();
    }

    private JudgementSession(JudgementSession source)
    {
        chart = source.chart;
        configuration = source.configuration;
        laneNotes = source.laneNotes;
        noteIndices = source.noteIndices;
        opportunities = source.opportunities;
        states = (NoteState[])source.states.Clone();
        revisions = new int[states.Length];
        heldControls = source.heldControls.Select(c => new HashSet<int>(c)).ToArray();
        transitions.AddRange(source.transitions);
        EventCount = source.EventCount;
        remainingNotes = source.remainingNotes;
        CurrentTime = source.CurrentTime;
        Score = source.Score;
        Health = source.Health;
        MaximumScore = source.MaximumScore;

        for (var i = 0; i < states.Length; i++)
            schedule(i);
    }

    public JudgementSession Clone() => new(this);

    public NoteSnapshot GetNote(int noteId) => snapshot(noteIndices[noteId]);

    public ImmutableArray<NoteSnapshot> GetNotes() => [.. Enumerable.Range(0, states.Length).Select(snapshot)];

    public ImmutableArray<NoteTransition> GetTransitionsSince(int transitionIndex)
    {
        if (transitionIndex < 0 || transitionIndex > transitions.Count)
            throw new ArgumentOutOfRangeException(nameof(transitionIndex));

        return [.. transitions.Skip(transitionIndex)];
    }

    public ImmutableArray<JudgementEvent> GetEventsSince(int eventIndex)
    {
        if (eventIndex < 0 || eventIndex > EventCount)
            throw new ArgumentOutOfRangeException(nameof(eventIndex));

        return [.. transitions.SelectMany(t => t.Judgements).Skip(eventIndex).Select(j => j.Event)];
    }

    public void Reset()
    {
        timers.Clear();
        transitions.Clear();
        EventCount = 0;
        Array.Clear(revisions);
        foreach (var controls in heldControls)
            controls.Clear();

        CurrentTime = null;
        Score = configuration.Score.InitialState;
        Health = configuration.Health.InitialState;
        remainingNotes = 0;

        for (var i = 0; i < states.Length; i++)
        {
            states[i] = chart.Notes[i].Rule.CreateState(chart.Notes[i]);
            if (!states[i].Completed)
                remainingNotes++;
            schedule(i);
        }
    }

    /// <summary>
    /// Inputs must be sorted by timestamp, preserving source order for ties, and be later than the
    /// previous closed timestamp. Validate the entire batch before changing the session.
    /// </summary>
    public ImmutableArray<InputDispatch> AdvanceTo(long time, ReadOnlySpan<InputEvent> inputs = default)
    {
        if (CurrentTime is { } current && time < current)
            throw new ArgumentOutOfRangeException(nameof(time), "Rewind using Reset and replay, or restore an independent clone.");

        long? previous = null;

        foreach (var input in inputs)
        {
            if (input.Lane < 0 || input.Lane >= chart.LaneCount || input.Time > time
                || (CurrentTime is { } closed && input.Time <= closed) || (previous is { } p && input.Time < p))
                throw new ArgumentException("Inputs require valid lanes, ascending timestamps in the open advance interval.", nameof(inputs));

            previous = input.Time;
        }

        var dispatched = ImmutableArray.CreateBuilder<InputDispatch>();
        var index = 0;

        while (index < inputs.Length)
        {
            var inputTime = inputs[index].Time;
            runTimers(inputTime, TimerOrder.BeforeInput);
            var changedInputs = new List<(InputEvent Event, LaneInput State)>();
            do
            {
                var input = inputs[index++];
                if (updateInputState(input) is { } state)
                    changedInputs.Add((input, state));
            } while (index < inputs.Length && inputs[index].Time == inputTime);

            // Sampled hazards observe the physical state for this instant before note judgements mutate scores or health.
            runTimers(inputTime, TimerOrder.AfterInputState);
            foreach (var input in changedInputs)
                dispatched.Add(applyInput(input.Event, input.State));

            runTimers(inputTime, TimerOrder.AfterInput);
        }

        runTimers(time, TimerOrder.AfterInput);
        CurrentTime = time;
        return dispatched.ToImmutable();
    }

    private LaneInput? updateInputState(InputEvent input)
    {
        var controls = heldControls[input.Lane];
        var previouslyHeld = controls.Count > 0;
        var changed = input.Pressed ? controls.Add(input.Control) : controls.Remove(input.Control);
        if (!changed)
            return null;

        return new LaneInput(controls.Count > 0, previouslyHeld);
    }

    public void EndAt(long time)
    {
        AdvanceTo(time);
        for (var i = 0; i < states.Length; i++)
        {
            if (states[i].Completed) continue;

            var note = chart.Notes[i];
            var held = heldControls[note.Lane].Count > 0;
            var context = new NoteContext(note, states[i], time, new LaneInput(held, held));
            var update = note.Rule.ApplyEnd(context);
            if (update.State != states[i] || !update.Emissions.IsEmpty)
                applyUpdate(i, context, null, update);
        }
    }

    /// <summary>
    /// Appends a later host batch at the current timestamp. Already dispatched timers keep their order;
    /// replay must retain this batch boundary when callbacks produce more input at the same instant.
    /// </summary>
    public ImmutableArray<InputDispatch> AppendInputs(ReadOnlySpan<InputEvent> inputs)
    {
        foreach (var input in inputs)
        {
            if (input.Time != CurrentTime || input.Lane < 0 || input.Lane >= chart.LaneCount)
                throw new ArgumentException("Appended inputs must belong to the current timestamp and a valid lane.", nameof(inputs));
        }

        var dispatched = ImmutableArray.CreateBuilder<InputDispatch>();
        foreach (var input in inputs)
        {
            if (updateInputState(input) is { } state)
                dispatched.Add(applyInput(input, state));
        }

        return dispatched.ToImmutable();
    }

    private InputDispatch applyInput(InputEvent input, LaneInput laneInput)
    {
        var firstTransition = TransitionCount;
        var candidates = ImmutableArray.CreateBuilder<InputCandidate>();

        foreach (var index in laneNotes[input.Lane])
        {
            if (states[index].Completed)
                continue;

            var context = new NoteContext(chart.Notes[index], states[index], input.Time, laneInput);
            if (chart.Notes[index].Rule.GetCandidate(context, input, index) is { } candidate)
                candidates.Add(candidate);
        }

        foreach (var candidate in configuration.SelectCandidates(new InputArbitrationContext(input, candidates.ToImmutable(), laneInput)))
            applyCandidate(candidate.NoteIndex, input, laneInput);
        return new InputDispatch(input, firstTransition, TransitionCount - firstTransition);
    }

    private void applyCandidate(int index, InputEvent input, LaneInput laneInput)
    {
        var note = chart.Notes[index];
        var context = new NoteContext(note, states[index], input.Time, laneInput);
        applyUpdate(index, context, input, note.Rule.ApplyInput(context, input));
    }

    private void runTimers(long time, TimerOrder throughOrder)
    {
        while (timers.TryPeek(out var pending, out var priority)
               && (priority.Time < time || (priority.Time == time && priority.Order <= throughOrder)))
        {
            timers.Dequeue();
            if (pending.Revision != revisions[pending.Index])
                continue;

            var note = chart.Notes[pending.Index];
            var held = heldControls[note.Lane].Count > 0;
            var context = new NoteContext(note, states[pending.Index], priority.Time, new LaneInput(held, held));
            applyUpdate(pending.Index, context, null, note.Rule.ApplyTimer(context));
        }
    }

    private void applyUpdate(int index, NoteContext context, InputEvent? input, NoteUpdate update)
    {
        var time = context.Time;
        if ((update.State.Completed && update.State.NextTimer != null)
            || (update.State.NextTimer is { } next && next.Time <= time))
            throw new InvalidOperationException("A rule must complete without a timer or schedule strictly after its current invocation.");

        var before = snapshot(context with
        {
            Input = context.Input with
            {
                Held = context.Input.PreviouslyHeld,
            },
        });
        if (!before.Completed && update.State.Completed)
            remainingNotes--;
        states[index] = update.State;
        revisions[index]++;
        schedule(index);
        var note = chart.Notes[index];
        var judgements = ImmutableArray.CreateBuilder<JudgementResult>(update.Emissions.Length);

        foreach (var emission in update.Emissions)
        {
            var judgement = new JudgementEvent(EventCount, note.Id, note.Lane, emission.Part, emission.Result,
                time, emission.ExpectedTime, emission.Offset) { SuppressPenalty = emission.SuppressPenalty };
            judgement = configuration.Score.TransformResult?.Invoke(Score, judgement) ?? judgement;
            opportunities.TryGetValue((note.Id, emission.Part), out var opportunity);
            Score = configuration.Score.Reduce(Score, new ScoreContext(judgement, opportunity, chart.ScorePlan));
            Health = configuration.Health.Reduce(new HealthContext(judgement, Health, Score, chart.ScorePlan));
            judgements.Add(new JudgementResult(judgement, Score, Health));
            EventCount++;
        }

        transitions.Add(new NoteTransition(transitions.Count, time, input, before, snapshot(context with { State = update.State }),
            judgements.MoveToImmutable(), Outcome));
    }

    private NoteSnapshot snapshot(int index)
    {
        var note = chart.Notes[index];
        var held = heldControls[note.Lane].Count > 0;
        return snapshot(new NoteContext(note, states[index], CurrentTime ?? 0, new LaneInput(held, held)));
    }

    private static NoteSnapshot snapshot(NoteContext context)
        => new(context.Note.Id, context.Note.Lane, context.State.Completed, context.Note.Rule.GetParts(context), context.Input.Held);

    private void schedule(int index)
    {
        if (states[index].NextTimer is { } timer)
            timers.Enqueue((index, revisions[index]), (timer.Time, timer.Order, index));
    }
}
