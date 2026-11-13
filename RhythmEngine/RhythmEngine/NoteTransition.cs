using System.Collections.Immutable;

namespace RhythmEngine;

/// <summary>
/// Resolved marks a part's primary decision; Active marks a sustained interaction.
/// Neither implies that the rule has retired: resolved objects can still arbitrate input.
/// Part IDs belong to the rule, not to an engine note-kind enumeration.
/// </summary>
public readonly record struct NotePartState(string Part, bool Resolved, bool Active = false);

public sealed record NoteSnapshot(int Id, int Lane, bool Completed, ImmutableArray<NotePartState> Parts);

/// <summary>The authoritative accumulators immediately after this judgement, in emission order.</summary>
public sealed record JudgementResult(JudgementEvent Event, ScoreState Score, HealthState Health);

/// <summary>
/// One rule invocation, including silent input and timer transitions. Input is null for a timer.
/// Ordered transitions preserve release/regrab edges within a single host frame.
/// </summary>
public sealed record NoteTransition(
    long Sequence,
    long Time,
    InputEvent? Input,
    NoteSnapshot Before,
    NoteSnapshot After,
    ImmutableArray<JudgementResult> Judgements,
    SessionOutcome Outcome);
