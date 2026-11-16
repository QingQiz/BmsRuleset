using System.Collections.Immutable;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;

namespace RhythmEngine.Profiles.Malody;

internal sealed record MalodyHoldState(NoteTimer? Timer, bool Done, bool HeadResolved, long? TickTime) : NoteState
{
    public override NoteTimer? NextTimer => Timer;

    public override bool Completed => Done;

    internal ImmutableArray<NotePartState> GetParts(bool hold)
        => hold
            ? [new NotePartState(NoteParts.HEAD, HeadResolved), new NotePartState(NoteParts.BODY, Done, TickTime != null && !Done), new NotePartState(NoteParts.TAIL, Done)]
            : [new NotePartState(NoteParts.HEAD, HeadResolved)];
}

internal sealed class MalodyClassicHoldRule(TimingWindows windows, long headDeadline, long releaseLenience) : INoteRule
{
    private const long tick_interval = 100_000;

    public ImmutableArray<NotePartState> GetParts(NoteContext context) => ((MalodyHoldState)context.State).GetParts(true);

    public NoteState CreateState(ChartNote note) => new MalodyHoldState(
        new NoteTimer(note.StartTime + headDeadline + 1, TimerOrder.BeforeInput), false, false, null);

    public InputCandidate? GetCandidate(NoteContext context, InputEvent input, int noteIndex)
    {
        var state = (MalodyHoldState)context.State;
        if (state.HeadResolved)
            return new InputCandidate(noteIndex, context.Note.StartTime, context.Time - context.Note.EndTime, 0);

        var offset = context.Time - context.Note.StartTime;
        return input.Pressed && windows.ResultFor(offset) != null
            ? new InputCandidate(noteIndex, context.Note.StartTime, offset, 1)
            : null;
    }

    public NoteUpdate ApplyInput(NoteContext context, InputEvent input)
    {
        var state = (MalodyHoldState)context.State;

        if (!state.HeadResolved)
        {
            var offset = context.Time - context.Note.StartTime;
            var result = windows.ResultFor(offset)!;
            if (result == MalodyResults.MISS)
                return new NoteUpdate(schedule(context, state with { HeadResolved = true }), [new NoteEmission(NoteParts.HEAD, result, context.Note.StartTime, offset)]);

            var tickTime = Math.Max(context.Time, context.Note.StartTime) + tick_interval;
            state = state with { HeadResolved = true, TickTime = tickTime };
            return new NoteUpdate(schedule(context, state), [new NoteEmission(NoteParts.HEAD, result, context.Note.StartTime, offset)]);
        }

        if (input.Pressed)
            return NoteUpdate.Silent(schedule(context, state with { TickTime = context.Time + tick_interval }));
        if (context.Input.Held)
            return NoteUpdate.Silent(state);
        if (context.Time >= context.Note.EndTime - releaseLenience)
            return NoteUpdate.Silent(state with { Timer = null, Done = true, TickTime = null });

        return new NoteUpdate(schedule(context, state with { TickTime = null }),
            [new NoteEmission(NoteParts.BODY, MalodyResults.HOLD_DROP, context.Note.EndTime, context.Time - context.Note.EndTime)]);
    }

    public NoteUpdate ApplyTimer(NoteContext context)
    {
        var state = (MalodyHoldState)context.State;
        if (!state.HeadResolved)
            return new NoteUpdate(schedule(context, state with { HeadResolved = true }), [new NoteEmission(NoteParts.HEAD, MalodyResults.MISS, context.Note.StartTime)]);

        var emitsTick = state.TickTime == context.Time;
        state = state with { TickTime = emitsTick ? context.Time + tick_interval : state.TickTime };
        return new NoteUpdate(schedule(context, state), emitsTick ? [new NoteEmission(NoteParts.BODY, MalodyResults.HOLD_TICK, context.Time)] : []);
    }

    private static MalodyHoldState schedule(NoteContext context, MalodyHoldState state)
    {
        if (context.Time >= context.Note.EndTime)
            return state with { Timer = null, Done = true, TickTime = null };

        var next = state.TickTime is { } tick && tick <= context.Note.EndTime ? tick : context.Note.EndTime;
        return state with { Timer = new NoteTimer(next) };
    }
}
