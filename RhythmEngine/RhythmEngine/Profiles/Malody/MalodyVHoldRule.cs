using System.Collections.Immutable;
using RhythmEngine.Core;

namespace RhythmEngine.Profiles.Malody;

internal sealed class MalodyVHoldRule(TimingWindows windows) : INoteRule
{
    private const long tick_interval = 100_000;

    public ImmutableArray<NotePartState> GetParts(NoteContext context) => ((MalodyHoldState)context.State).GetParts(true);

    public NoteState CreateState(ChartNote note) => new MalodyHoldState(
        new NoteTimer(note.StartTime + windows.Entries[^1].Late + 1, TimerOrder.BeforeInput), false, false, null);

    public IEnumerable<ScoringOpportunity> GetScoringOpportunities(ChartNote note)
    {
        yield return new ScoringOpportunity(note.Id, "head", note.StartTime, 1000);
    }

    public InputCandidate? GetCandidate(NoteContext context, InputEvent input, int noteIndex)
    {
        var state = (MalodyHoldState)context.State;
        var offset = context.Time - context.Note.StartTime;
        return state.HeadResolved
            ? new InputCandidate(noteIndex, context.Note.StartTime, context.Time - context.Note.EndTime, 0)
            : input.Pressed && windows.ResultFor(offset) != null
                ? new InputCandidate(noteIndex, context.Note.StartTime, offset, 1)
                : null;
    }

    public NoteUpdate ApplyInput(NoteContext context, InputEvent input)
    {
        var state = (MalodyHoldState)context.State;
        var emissions = ImmutableArray.CreateBuilder<NoteEmission>();
        if (!state.HeadResolved)
        {
            var offset = context.Time - context.Note.StartTime;
            var result = windows.ResultFor(offset)!;
            emissions.Add(new NoteEmission("head", result, context.Note.StartTime, offset));
            state = state with { HeadResolved = true, TickTime = result == "miss" ? null : context.Note.StartTime + tick_interval };
            state = emitTicks(context.Note, state, context.Time, emissions);
        }
        else if (input.Pressed)
            state = state with { TickTime = context.Time + tick_interval };
        else if (!context.Input.Held)
        {
            // RC 2.0 drains the remaining body combo ticks before deciding the release result.
            state = emitTicks(context.Note, state, context.Note.EndTime, emissions);
            if (context.Time >= context.Note.EndTime - windows.Entries[2].Late)
                return new NoteUpdate(state with { Timer = null, Done = true, TickTime = null }, emissions.ToImmutable());

            emissions.Add(new NoteEmission("body", "hold-drop", context.Note.EndTime, context.Time - context.Note.EndTime));
            state = state with { TickTime = null };
        }

        return new NoteUpdate(schedule(context, state), emissions.ToImmutable());
    }

    public NoteUpdate ApplyTimer(NoteContext context)
    {
        var state = (MalodyHoldState)context.State;
        var emissions = ImmutableArray.CreateBuilder<NoteEmission>();
        if (!state.HeadResolved)
        {
            state = state with { HeadResolved = true };
            emissions.Add(new NoteEmission("head", "miss", context.Note.StartTime));
        }
        else
            state = emitTicks(context.Note, state, context.Time, emissions);

        return new NoteUpdate(schedule(context, state), emissions.ToImmutable());
    }

    private static MalodyHoldState emitTicks(ChartNote note, MalodyHoldState state, long until, ImmutableArray<NoteEmission>.Builder emissions)
    {
        while (state.TickTime is { } tick && tick <= until && tick <= note.EndTime)
        {
            emissions.Add(new NoteEmission("body", "hold-tick", tick));
            state = state with { TickTime = tick + tick_interval };
        }

        return state;
    }

    private static MalodyHoldState schedule(NoteContext context, MalodyHoldState state)
    {
        if (context.Time >= context.Note.EndTime)
            return state with { Timer = null, Done = true, TickTime = null };

        var next = state.TickTime is { } tick && tick <= context.Note.EndTime ? tick : context.Note.EndTime;
        return state with { Timer = new NoteTimer(next) };
    }
}
