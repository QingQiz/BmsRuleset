using System.Collections.Immutable;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Patterns;

namespace RhythmEngine.Profiles.Etterna;

internal sealed class EtternaNoteRule(INoteRule inner, int row) : INoteRule
{
    internal int Row { get; } = row;

    public NoteState CreateState(ChartNote note) => inner.CreateState(note);

    public ImmutableArray<NotePartState> GetParts(NoteContext context) => inner.GetParts(context);

    public IEnumerable<ScoringOpportunity> GetScoringOpportunities(ChartNote note) => inner.GetScoringOpportunities(note);

    public InputCandidate? GetCandidate(NoteContext context, InputEvent input, int noteIndex)
    {
        var unresolved = context.State is TapState { Resolved: false } or HoldState { HeadResult: null } or MineState { Done: false };
        // Player chooses the nearest row before checking note kind, input edge or the smaller mine window.
        return unresolved
            ? new InputCandidate(noteIndex, context.Note.StartTime, context.Time - context.Note.StartTime, 1)
            : inner.GetCandidate(context, input, noteIndex);
    }

    public NoteUpdate ApplyInput(NoteContext context, InputEvent input)
        => inner.GetCandidate(context, input, 0) == null
            ? NoteUpdate.Silent(context.State)
            : inner.ApplyInput(context, input);

    public NoteUpdate ApplyTimer(NoteContext context) => inner.ApplyTimer(context);
}
