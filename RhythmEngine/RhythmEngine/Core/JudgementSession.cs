using System.Collections.Immutable;
using System.Runtime.InteropServices;

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
    private readonly NoteState[] states;
    private readonly int[] revisions;
    private readonly int[] laneStarts;
    private readonly int?[]? keySoundMemory;
    private readonly HashSet<int>[] heldControls;
    private readonly PriorityQueue<(int Index, int Revision), (long Time, TimerOrder Order, int Index)> timers = new();
    private int remainingNotes;

    public long? CurrentTime { get; private set; }

    public ScoreState Score { get; private set; }

    public HealthState Health { get; private set; }

    public double MaximumScore { get; }

    public bool Completed => completedAt(CurrentTime);

    public SessionOutcome Outcome => outcomeAt(CurrentTime);

    private bool completedAt(long? time) => configuration.IsComplete?.Invoke(new SessionCompletionContext(time, remainingNotes == 0, Score, chart.ScorePlan)) ?? remainingNotes == 0;

    private SessionOutcome outcomeAt(long? time) => configuration.Health.EvaluateOutcome(new SessionOutcomeContext(completedAt(time), Health, Score, chart.ScorePlan));

    public bool Cleared => Outcome == SessionOutcome.Cleared;

    public int EventCount { get; private set; }

    public int TransitionCount { get; private set; }

    public JudgementSession(Chart chart, SessionConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.ProfileId))
            throw new ArgumentException("A named profile is required.", nameof(configuration));

        this.chart = chart;
        this.configuration = configuration;
        keySoundMemory = configuration.KeySounds == null ? null : new int?[chart.LaneCount];
        states = new NoteState[chart.Notes.Length];
        revisions = new int[states.Length];
        laneStarts = new int[chart.LaneCount];
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
        states = (NoteState[])source.states.Clone();
        revisions = new int[states.Length];
        laneStarts = (int[])source.laneStarts.Clone();
        keySoundMemory = source.keySoundMemory?.ToArray();
        heldControls = source.heldControls.Select(c => new HashSet<int>(c)).ToArray();
        TransitionCount = source.TransitionCount;
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

    public NoteSnapshot GetNote(int noteId) => snapshot(chart.NoteIndices[noteId]);

    public ImmutableArray<NoteSnapshot> GetNotes() => [.. Enumerable.Range(0, states.Length).Select(snapshot)];

    public void Reset()
    {
        timers.Clear();
        TransitionCount = 0;
        EventCount = 0;
        Array.Clear(revisions);
        Array.Clear(laneStarts);
        if (keySoundMemory != null)
            Array.Clear(keySoundMemory);
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
    public JudgementBatch AdvanceTo(long time, ReadOnlySpan<InputEvent> inputs = default)
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

        var transitions = new List<NoteTransition>();
        var dispatched = ImmutableArray.CreateBuilder<InputDispatch>();
        var index = 0;

        while (index < inputs.Length)
        {
            var inputTime = inputs[index].Time;
            runTimers(inputTime, TimerOrder.BeforeInput, transitions);
            var changedInputs = new List<(InputEvent Event, LaneInput State)>();

            do
            {
                var input = inputs[index++];
                if (updateInputState(input) is { } state)
                    changedInputs.Add((input, state));
            } while (index < inputs.Length && inputs[index].Time == inputTime);

            // Sampled hazards observe the physical state for this instant before note judgements mutate scores or health.
            runTimers(inputTime, TimerOrder.AfterInputState, transitions);
            foreach (var input in changedInputs)
                dispatched.Add(applyInput(input.Event, input.State, transitions));

            runTimers(inputTime, TimerOrder.AfterInput, transitions);
        }

        runTimers(time, TimerOrder.AfterInput, transitions);
        CurrentTime = time;
        return new JudgementBatch([.. transitions], dispatched.ToImmutable());
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

    public JudgementBatch EndAt(long time)
    {
        var transitions = new List<NoteTransition>(AdvanceTo(time).Transitions);

        for (var i = 0; i < states.Length; i++)
        {
            if (states[i].Completed) continue;

            var note = chart.Notes[i];
            var held = heldControls[note.Lane].Count > 0;
            var context = new NoteContext(note, states[i], time, new LaneInput(held, held));
            var update = note.Rule.ApplyEnd(context);
            if (update.State != states[i] || !update.Emissions.IsEmpty)
                applyUpdate(i, context, null, update, transitions);
        }

        return new JudgementBatch([.. transitions], []);
    }

    /// <summary>
    /// Appends a later host batch at the current timestamp. Already dispatched timers keep their order;
    /// replay must retain this batch boundary when callbacks produce more input at the same instant.
    /// </summary>
    public JudgementBatch AppendInputs(ReadOnlySpan<InputEvent> inputs)
    {
        foreach (var input in inputs)
        {
            if (input.Time != CurrentTime || input.Lane < 0 || input.Lane >= chart.LaneCount)
                throw new ArgumentException("Appended inputs must belong to the current timestamp and a valid lane.", nameof(inputs));
        }

        var transitions = new List<NoteTransition>();
        var dispatched = ImmutableArray.CreateBuilder<InputDispatch>();

        foreach (var input in inputs)
        {
            if (updateInputState(input) is { } state)
                dispatched.Add(applyInput(input, state, transitions));
        }

        return new JudgementBatch([.. transitions], dispatched.ToImmutable());
    }

    private InputDispatch applyInput(InputEvent input, LaneInput laneInput, List<NoteTransition> transitions)
    {
        var firstTransition = transitions.Count;
        ImmutableArray<InputCandidate>.Builder? candidates = null;

        var lane = chart.LaneNotes[input.Lane];
        var timeOrdered = lane == chart.LaneNotesByTime[input.Lane];
        ref var firstIncomplete = ref laneStarts[input.Lane];
        while (firstIncomplete < lane.Length && states[lane[firstIncomplete]].Completed)
            firstIncomplete++;

        var earliestOffset = chart.LaneCandidateOffsets[input.Lane];
        var latestStart = earliestOffset == long.MinValue || input.Time >= long.MaxValue + earliestOffset
            ? long.MaxValue
            : input.Time - earliestOffset;
        for (var i = firstIncomplete; i < lane.Length; i++)
        {
            var index = lane[i];
            var note = chart.Notes[index];
            if (note.StartTime > latestStart)
            {
                if (timeOrdered)
                    break;

                continue;
            }

            var state = states[index];
            if (state.Completed)
                continue;

            if (note.Rule.GetCandidate(new NoteContext(note, state, input.Time, laneInput), input, index) is { } candidate)
                (candidates ??= ImmutableArray.CreateBuilder<InputCandidate>()).Add(candidate);
        }

        foreach (var candidate in configuration.SelectCandidates(new InputArbitrationContext(input, candidates?.ToImmutable() ?? [], laneInput)))
            applyCandidate(candidate.NoteIndex, input, laneInput, firstIncomplete, transitions);
        var count = transitions.Count - firstTransition;
        if (!input.Pressed || configuration.KeySounds is not { } keySounds)
            return new InputDispatch(input, firstTransition, count);

        var soundContext = new KeySoundContext(input, chart, CollectionsMarshal.AsSpan(transitions).Slice(firstTransition, count),
            this, timeOrdered ? firstIncomplete : 0);
        var decision = keySounds.Select(soundContext, keySoundMemory![input.Lane]);
        keySoundMemory[input.Lane] = decision.Remember;
        return new InputDispatch(input, firstTransition, count, decision.Playback);
    }

    private void applyCandidate(int index, InputEvent input, LaneInput laneInput, int firstIncomplete, List<NoteTransition> transitions)
    {
        var note = chart.Notes[index];
        var context = new NoteContext(note, states[index], input.Time, laneInput);
        var update = note.Rule.ApplyInput(context, input);
        applyUpdate(index, context, input, update, transitions);

        if (!input.Pressed || update.Emissions.IsEmpty || configuration.RetireNotesEndingBeforeHit?.Invoke(update) is not { } retire)
            return;

        var lane = chart.LaneNotes[note.Lane];
        var timeOrdered = lane == chart.LaneNotesByTime[note.Lane];
        for (var i = firstIncomplete; i < lane.Length; i++)
        {
            var earlierIndex = lane[i];
            var earlier = chart.Notes[earlierIndex];
            if (timeOrdered && earlier.StartTime >= note.StartTime)
                break;

            // A tail at the same chart time as the new head can still be released.
            if (earlier.EndTime >= note.StartTime || states[earlierIndex].Completed)
                continue;

            var earlierContext = new NoteContext(earlier, states[earlierIndex], input.Time, laneInput);
            applyUpdate(earlierIndex, earlierContext, null, retire(earlierContext), transitions);
        }
    }

    private void runTimers(long time, TimerOrder throughOrder, List<NoteTransition> transitions)
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
            applyUpdate(pending.Index, context, null, note.Rule.ApplyTimer(context), transitions);
        }
    }

    private void applyUpdate(int index, NoteContext context, InputEvent? input, NoteUpdate update, List<NoteTransition> transitions)
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

        if (context.State.NextTimer != update.State.NextTimer)
        {
            revisions[index]++;
            schedule(index);
        }

        var note = chart.Notes[index];
        var judgements = ImmutableArray.CreateBuilder<JudgementResult>(update.Emissions.Length);

        foreach (var emission in update.Emissions)
        {
            var judgement = new JudgementEvent(EventCount, note.Id, note.Lane, emission.Part, emission.Result,
                time, emission.ExpectedTime, emission.Offset) { SuppressPenalty = emission.SuppressPenalty };
            judgement = configuration.Score.TransformResult?.Invoke(Score, judgement) ?? judgement;
            chart.ScorePlan.OpportunitiesByPart.TryGetValue((note.Id, emission.Part), out var opportunity);
            Score = configuration.Score.Reduce(Score, new ScoreContext(judgement, opportunity, chart.ScorePlan));
            Health = configuration.Health.Reduce(new HealthContext(judgement, Health, Score, chart.ScorePlan));
            judgements.Add(new JudgementResult(judgement, Score, Health));
            EventCount++;
        }

        var transition = new NoteTransition(TransitionCount++, time, input, before, snapshot(context with { State = update.State }),
            judgements.MoveToImmutable(), outcomeAt(time));
        if (configuration.KeySounds?.TransitionSounds?.Invoke(transition) is { IsDefaultOrEmpty: false } sounds)
            transition = transition with { KeySounds = sounds };
        transitions.Add(transition);
        if (configuration.KeySounds?.Observe is { } observe)
            keySoundMemory![note.Lane] = observe(transition, keySoundMemory[note.Lane]);
    }

    private NoteSnapshot snapshot(int index) => SnapshotAt(index, CurrentTime ?? 0);

    internal bool IsNoteCompleted(int index) => states[index].Completed;

    internal ImmutableArray<NotePartState> GetPartsAt(int index, long time)
    {
        var context = contextAt(index, time);
        return context.Note.Rule.GetParts(context);
    }

    internal NoteSnapshot SnapshotAt(int index, long time) => snapshot(contextAt(index, time));

    private NoteContext contextAt(int index, long time)
    {
        var note = chart.Notes[index];
        var held = heldControls[note.Lane].Count > 0;
        return new NoteContext(note, states[index], time, new LaneInput(held, held));
    }

    private static NoteSnapshot snapshot(NoteContext context)
        => new(context.Note.Id, context.Note.Lane, context.State.Completed, context.Note.Rule.GetParts(context), context.Input.Held);

    private void schedule(int index)
    {
        if (states[index].NextTimer is { } timer)
            timers.Enqueue((index, revisions[index]), (timer.Time, timer.Order, index));
    }
}
