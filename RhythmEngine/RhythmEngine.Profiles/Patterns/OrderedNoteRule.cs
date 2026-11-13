using System.Collections.Immutable;

namespace RhythmEngine.Profiles.Patterns;

public sealed record OrderedNoteState(NoteState Inner, bool Retired = false) : NoteState
{
    public override NoteTimer? NextTimer => Retired ? null : Inner.NextTimer;

    public override bool Completed => Retired || Inner.Completed;
}

/// <summary>Time-ordered note lock: a successful later input retires earlier finished objects.</summary>
public sealed class OrderedNoteRule(INoteRule inner, ImmutableArray<long> laneStarts, int nextIndex, TimingWindows windows) : INoteRule
{
    public INoteRule Inner => inner;

    public NoteState CreateState(ChartNote note) => new OrderedNoteState(inner.CreateState(note));

    public ImmutableArray<NotePartState> GetParts(NoteContext context)
    {
        var ordered = (OrderedNoteState)context.State;
        var parts = inner.GetParts(unwrap(context));
        return ordered.Retired ? [.. parts.Select(p => p with { Resolved = true, Active = false })] : parts;
    }

    public IEnumerable<ScoringOpportunity> GetScoringOpportunities(ChartNote note) => inner.GetScoringOpportunities(note);

    public InputCandidate? GetCandidate(NoteContext context, InputEvent input, int noteIndex)
    {
        if (input.Pressed && nextIndex < laneStarts.Length && context.Time >= laneStarts[nextIndex])
        {
            var index = laneStarts.BinarySearch(context.Time);
            var successor = laneStarts[index < 0 ? ~index - 1 : index];
            if (context.Note.EndTime < successor && windows.ResultFor(context.Time - successor) != null)
                return new InputCandidate(noteIndex, context.Note.StartTime, 0, 0, true);

            return null;
        }

        return inner.GetCandidate(unwrap(context), input, noteIndex);
    }

    public NoteUpdate ApplyInput(NoteContext context, InputEvent input)
    {
        if (input.Pressed && nextIndex < laneStarts.Length && context.Time >= laneStarts[nextIndex])
        {
            var state = ((OrderedNoteState)context.State).Inner;
            var emissions = ImmutableArray.CreateBuilder<NoteEmission>();
            if (state is TapState { Resolved: false })
                emissions.Add(new NoteEmission("tap", "miss", context.Note.StartTime));
            if (state is HoldState hold)
            {
                if (hold.HeadResult == null)
                    emissions.Add(new NoteEmission("head", "miss", context.Note.StartTime));
                if (!hold.TailResolved)
                    emissions.Add(new NoteEmission("tail", "miss", context.Note.EndTime));
                if (!hold.Broken)
                    emissions.Add(new NoteEmission("body", "combo-break", context.Note.EndTime));
            }

            return new NoteUpdate(new OrderedNoteState(state, true), emissions.ToImmutable());
        }

        return wrap(inner.ApplyInput(unwrap(context), input));
    }

    public NoteUpdate ApplyTimer(NoteContext context) => wrap(inner.ApplyTimer(unwrap(context)));

    private static NoteContext unwrap(NoteContext context) => context with { State = ((OrderedNoteState)context.State).Inner };

    private static NoteUpdate wrap(NoteUpdate update) => update with { State = new OrderedNoteState(update.State) };
}
