using System.Collections.Immutable;
using RhythmEngine.Core;

namespace RhythmEngine.Hosting;

/// <summary>
/// Live frames carry ordered deltas. Snapshot frames replace all presentation state from RestoredNotes;
/// their transitions, if any, are historical and must not trigger live effects.
/// </summary>
public sealed record SessionFrame(
    long Generation,
    bool IsSnapshot,
    long? Time,
    ImmutableArray<NoteTransition> Transitions,
    ImmutableArray<NoteSnapshot> RestoredNotes,
    ScoreState Score,
    HealthState Health,
    double MaximumScore,
    bool Completed,
    SessionOutcome Outcome)
{
    public bool Cleared => Outcome == SessionOutcome.Cleared;

    public ImmutableArray<JudgementEvent> Events => [.. Transitions.SelectMany(t => t.Judgements).Select(j => j.Event)];
}

/// <summary>
/// Game integration boundary. The game maps source objects and physical controls before creating
/// a session, batches all inputs at a timestamp, and owns audio/rendering and replay storage.
/// All timestamps use the core's song-clock microseconds. Returned frames are immutable.
/// </summary>
public interface IGameplaySession
{
    string ProfileId { get; }

    SessionFrame Current { get; }

    SessionFrame AdvanceTo(long time, ReadOnlySpan<InputEvent> inputs = default);

    SessionFrame Reconstruct(long time, ReadOnlySpan<InputEvent> replayInputs);

    SessionFrame Restart();

    IGameplaySession Clone();

    NoteSnapshot GetNote(int noteId);
}
