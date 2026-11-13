using System.Collections.Immutable;
using RhythmEngine.Core;

namespace RhythmEngine.Hosting;

/// <summary>
/// Separates incremental presentation from replay reconstruction. A generation change invalidates
/// the game's previous presentation state; reconstruction events must not retrigger live audio.
/// </summary>
public sealed class GameplaySession : IGameplaySession
{
    private readonly Chart chart;
    private readonly SessionConfiguration configuration;
    private JudgementSession session;
    private long generation;

    public string ProfileId => configuration.ProfileId;

    /// <summary>A presentation snapshot, without replaying historical transitions.</summary>
    public SessionFrame Current => frame([], true);

    public GameplaySession(Chart chart, SessionConfiguration configuration)
    {
        this.chart = chart;
        this.configuration = configuration;
        session = new JudgementSession(chart, configuration);
    }

    private GameplaySession(GameplaySession source)
    {
        chart = source.chart;
        configuration = source.configuration;
        session = source.session.Clone();
        generation = source.generation;
    }

    public SessionFrame AdvanceTo(long time, ReadOnlySpan<InputEvent> inputs = default)
    {
        var firstTransition = session.TransitionCount;
        session.AdvanceTo(time, inputs);
        return frame(session.GetTransitionsSince(firstTransition), false);
    }

    public SessionFrame Reconstruct(long time, ReadOnlySpan<InputEvent> replayInputs)
    {
        // Build independently so invalid replay input leaves the live session usable.
        var rebuilt = new JudgementSession(chart, configuration);
        rebuilt.AdvanceTo(time, replayInputs);
        session = rebuilt;
        generation++;
        return frame(session.GetTransitionsSince(0), true);
    }

    public SessionFrame Restart()
    {
        session.Reset();
        generation++;
        return frame([], true);
    }

    public IGameplaySession Clone() => new GameplaySession(this);

    public NoteSnapshot GetNote(int noteId) => session.GetNote(noteId);

    private SessionFrame frame(ImmutableArray<NoteTransition> transitions, bool isSnapshot)
        => new(generation, isSnapshot, session.CurrentTime, transitions, isSnapshot ? session.GetNotes() : [],
            session.Score, session.Health, session.MaximumScore, session.Completed, session.Outcome);
}
