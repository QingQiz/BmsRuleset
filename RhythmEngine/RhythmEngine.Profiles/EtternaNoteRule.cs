using System.Collections.Immutable;
using RhythmEngine.Profiles.Patterns;

namespace RhythmEngine.Profiles;

internal sealed record EtternaNoteState(NoteState Inner) : NoteState
{
    public override NoteTimer? NextTimer => Inner.NextTimer;

    public override bool Completed => Inner.Completed;
}

internal sealed class EtternaNoteRule(INoteRule inner, EtternaSearchTimeline timeline, int row) : INoteRule
{
    internal int Row { get; } = row;

    public NoteState CreateState(ChartNote note) => new EtternaNoteState(inner.CreateState(note));

    public ImmutableArray<NotePartState> GetParts(NoteContext context) => inner.GetParts(unwrap(context));

    public IEnumerable<ScoringOpportunity> GetScoringOpportunities(ChartNote note) => inner.GetScoringOpportunities(note);

    public InputCandidate? GetCandidate(NoteContext context, InputEvent input, int noteIndex)
    {
        var unwrapped = unwrap(context);
        var candidate = inner.GetCandidate(unwrapped, input, noteIndex);
        var unresolved = unwrapped.State is TapState { Resolved: false } or HoldState { HeadResult: null } or MineState { Done: false };
        var offset = context.Time - context.Note.StartTime;
        var search = timeline.SearchAt(context.Time);
        // Player chooses the nearest row before checking note kind, input edge or the smaller mine window.
        if (unresolved && Row >= search.Row - search.Radius && Row < search.Row + search.Radius)
            return new InputCandidate(noteIndex, context.Note.StartTime, offset, 1);

        return candidate;
    }

    public NoteUpdate ApplyInput(NoteContext context, InputEvent input)
    {
        var unwrapped = unwrap(context);
        return inner.GetCandidate(unwrapped, input, 0) == null
            ? NoteUpdate.Silent(context.State)
            : wrap(inner.ApplyInput(unwrapped, input));
    }

    public NoteUpdate ApplyTimer(NoteContext context) => wrap(inner.ApplyTimer(unwrap(context)));

    private static NoteContext unwrap(NoteContext context) => context with { State = ((EtternaNoteState)context.State).Inner };

    private static NoteUpdate wrap(NoteUpdate update) => update with { State = new EtternaNoteState(update.State) };
}
