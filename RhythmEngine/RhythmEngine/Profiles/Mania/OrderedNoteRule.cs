using System.Collections.Immutable;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using RhythmEngine.Profiles.Patterns;

namespace RhythmEngine.Profiles.Mania;

public sealed record OrderedNoteState(NoteState Inner, bool Retired = false) : NoteState
{
    public override NoteTimer? NextTimer => Retired ? null : Inner.NextTimer;

    public override bool Completed => Retired || Inner.Completed;
}

/// <summary>Time-ordered note lock: a successful later input retires earlier finished objects.</summary>
public sealed class OrderedNoteRule(INoteRule inner, long? nextStart) : INoteRule, INoteTiming
{
    public INoteRule Inner => inner;

    public NoteTiming Timing => ((INoteTiming)inner).Timing;

    public NoteState CreateState(ChartNote note) => new OrderedNoteState(inner.CreateState(note));

    public ImmutableArray<NotePartState> GetParts(NoteContext context)
    {
        var ordered = (OrderedNoteState)context.State;
        var parts = inner.GetParts(unwrap(context));
        return ordered.Retired ? [.. parts.Select(p => p with { Resolved = true, Active = false })] : parts;
    }

    long? INoteRule.EarliestCandidateOffset => inner.EarliestCandidateOffset;

    public InputCandidate? GetCandidate(NoteContext context, InputEvent input, int noteIndex)
    {
        if (input.Pressed && context.Time >= nextStart)
            return null;

        return inner.GetCandidate(unwrap(context), input, noteIndex);
    }

    public NoteUpdate ApplyInput(NoteContext context, InputEvent input)
        => wrap(inner.ApplyInput(unwrap(context), input));

    public NoteUpdate ApplyTimer(NoteContext context) => wrap(inner.ApplyTimer(unwrap(context)));

    internal static long? RetirementBoundaryAfterHit(NoteUpdate update)
    {
        foreach (var emission in update.Emissions)
        {
            if ((emission.Part == NoteParts.TAP || emission.Part == NoteParts.HEAD || emission.Part == NoteParts.TAIL)
                && emission.Result != ManiaResults.MISS)
                return emission.ExpectedTime;
        }

        return null;
    }

    internal static NoteUpdate Retire(NoteContext context)
    {
        var state = ((OrderedNoteState)context.State).Inner;
        var emissions = ImmutableArray.CreateBuilder<NoteEmission>();
        if (state is TapState { Resolved: false })
            emissions.Add(new NoteEmission(NoteParts.TAP, ManiaResults.MISS, context.Note.StartTime));
        if (state is HoldState { TailResolved: false } hold)
        {
            if (hold.HeadResult == null)
                emissions.Add(new NoteEmission(NoteParts.HEAD, ManiaResults.MISS, context.Note.StartTime));
            emissions.Add(new NoteEmission(NoteParts.TAIL, ManiaResults.MISS, context.Note.EndTime));
            if (!hold.Broken)
                emissions.Add(new NoteEmission(NoteParts.BODY, ManiaResults.COMBO_BREAK, context.Note.EndTime));

            state = hold with
            {
                HeadResult = hold.HeadResult ?? ManiaResults.MISS,
                HeadTime = hold.HeadTime ?? context.Time,
                TailResult = ManiaResults.MISS,
                TailTime = context.Time,
            };
        }

        return new NoteUpdate(new OrderedNoteState(state, true), emissions.ToImmutable());
    }

    private static NoteContext unwrap(NoteContext context) => context with { State = ((OrderedNoteState)context.State).Inner };

    private static NoteUpdate wrap(NoteUpdate update) => update with { State = new OrderedNoteState(update.State) };
}
