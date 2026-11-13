using System.Collections.Immutable;

namespace RhythmEngine;

public readonly record struct InputEvent(long Time, int Lane, bool Pressed, int Control = 0);

public enum TimerOrder
{
    BeforeInput,
    AfterInputState,
    AfterInput,
}

public readonly record struct NoteTimer(long Time, TimerOrder Order = TimerOrder.AfterInput);

/// <summary>
/// Rule state must be transitively immutable. Evolving state belongs here, never in a rule or delegate closure.
/// A rule schedules strictly after its last invocation; simultaneous work is returned in one update.
/// </summary>
public abstract record NoteState
{
    public abstract NoteTimer? NextTimer { get; }

    public abstract bool Completed { get; }
}

public readonly record struct LaneInput(bool Held, bool PreviouslyHeld);

public readonly record struct NoteContext(ChartNote Note, NoteState State, long Time, LaneInput Input);

public readonly record struct NoteEmission(string Part, string Result, long ExpectedTime, long? Offset = null, bool SuppressPenalty = false);

public sealed record NoteUpdate(NoteState State, ImmutableArray<NoteEmission> Emissions)
{
    public static NoteUpdate Silent(NoteState state) => new(state, []);
}

public readonly record struct InputCandidate(int NoteIndex, long StartTime, long Offset, int Priority, bool Shared = false);

public readonly record struct InputArbitrationContext(InputEvent Input, ImmutableArray<InputCandidate> Candidates, LaneInput State = default);

/// <summary>
/// An immutable executable judgement configuration. Implementations receive no game objects or services.
/// Methods must be deterministic and side-effect free. Result names and part names are profile-owned IDs.
/// </summary>
public interface INoteRule
{
    NoteState CreateState(ChartNote note);

    /// <summary>
    /// Projects authoritative state for a host without exposing the rule's state type.
    /// Part IDs and their order are stable for a note, including after completion.
    /// Time-dependent changes must be scheduled by the rule, not inferred by this projection.
    /// </summary>
    ImmutableArray<NotePartState> GetParts(NoteContext context);

    IEnumerable<ScoringOpportunity> GetScoringOpportunities(ChartNote note);

    InputCandidate? GetCandidate(NoteContext context, InputEvent input, int noteIndex);

    NoteUpdate ApplyInput(NoteContext context, InputEvent input);

    NoteUpdate ApplyTimer(NoteContext context);

    /// <summary>Settles an interaction when the host ends an attempt, including an aborted replay.</summary>
    NoteUpdate ApplyEnd(NoteContext context) => NoteUpdate.Silent(context.State);
}
