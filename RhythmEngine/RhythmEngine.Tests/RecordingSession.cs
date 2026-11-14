using System.Collections.Immutable;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;

namespace RhythmEngine.Tests;

/// <summary>Test consumers retain the batches they need to compare across advances.</summary>
internal sealed class RecordingSession
{
    private readonly JudgementSession session;
    private readonly List<NoteTransition> transitions = [];

    internal RecordingSession(Chart chart, SessionConfiguration configuration)
        : this(new JudgementSession(chart, configuration))
    {
    }

    internal RecordingSession(JudgementSession session)
    {
        this.session = session;
    }

    internal long? CurrentTime => session.CurrentTime;

    internal ScoreState Score => session.Score;

    internal HealthState Health => session.Health;

    internal double MaximumScore => session.MaximumScore;

    internal bool Completed => session.Completed;

    internal bool Cleared => session.Cleared;

    internal int EventCount => session.EventCount;

    internal NoteSnapshot GetNote(int noteId) => session.GetNote(noteId);

    internal JudgementBatch AdvanceTo(long time, ReadOnlySpan<InputEvent> inputs = default) => record(session.AdvanceTo(time, inputs));

    internal JudgementBatch AppendInputs(ReadOnlySpan<InputEvent> inputs) => record(session.AppendInputs(inputs));

    internal JudgementBatch EndAt(long time) => record(session.EndAt(time));

    internal ImmutableArray<JudgementEvent> GetEventsSince(int index)
        => [.. transitions.SelectMany(t => t.Judgements).Skip(index).Select(j => j.Event)];

    internal void Reset()
    {
        session.Reset();
        transitions.Clear();
    }

    internal RecordingSession Clone()
    {
        var clone = new RecordingSession(session.Clone());
        clone.transitions.AddRange(transitions);
        return clone;
    }

    private JudgementBatch record(JudgementBatch batch)
    {
        transitions.AddRange(batch.Transitions);
        return batch;
    }
}

internal static class RecordingSessionExtensions
{
    internal static RecordingSession CreateRecordingSession(this CompiledProfile profile) => new(profile.CreateSession());
}
