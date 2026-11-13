using RhythmEngine.Core;
using RhythmEngine.Hosting;

namespace RhythmEngine.Tests.Hosting;

/// <summary>
/// An independent presentation consumer: it knows no profiles, grades, windows or rule state types.
/// Audio entries stand in for starting/stopping resources owned by the game.
/// </summary>
internal sealed class RecordingHost
{
    public Dictionary<int, NoteSnapshot> Notes { get; } = [];

    public List<(long Time, int NoteId, string Part, bool Active)> Audio { get; } = [];

    public List<JudgementResult> Judgements { get; } = [];

    public List<SessionOutcome> Outcomes { get; } = [];

    public ScoreState Score { get; private set; } = new();

    public HealthState Health { get; private set; }

    public void Present(SessionFrame frame)
    {
        if (frame.IsSnapshot)
        {
            Notes.Clear();
            foreach (var note in frame.RestoredNotes)
                Notes.Add(note.Id, note);
            Judgements.Clear();
            Outcomes.Clear();
        }
        else
        {
            foreach (var transition in frame.Transitions)
            {
                foreach (var (before, after) in transition.Before.Parts.Zip(transition.After.Parts))
                {
                    if (before.Active != after.Active)
                        Audio.Add((transition.Time, transition.After.Id, after.Part, after.Active));
                }

                Notes[transition.After.Id] = transition.After;
                Judgements.AddRange(transition.Judgements);
                Outcomes.Add(transition.Outcome);
            }
        }

        Score = frame.Score;
        Health = frame.Health;
    }
}
