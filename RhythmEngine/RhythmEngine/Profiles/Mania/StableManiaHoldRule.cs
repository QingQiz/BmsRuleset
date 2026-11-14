using System.Collections.Immutable;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;

namespace RhythmEngine.Profiles.Mania;

internal sealed record StableManiaHoldState(NoteTimer? Timer, long? PressTime, long? ReleaseTime, long? TickTime, bool Held, bool Broken, bool Done) : NoteState
{
    public override NoteTimer? NextTimer => Timer;

    public override bool Completed => Done;
}

internal sealed class StableManiaHoldRule(ImmutableArray<long> windows) : INoteRule
{
    private const long tick_interval = 100_000;

    public ImmutableArray<NotePartState> GetParts(NoteContext context)
    {
        var hold = (StableManiaHoldState)context.State;
        return [new NotePartState(NoteParts.HEAD, hold.PressTime != null || hold.Done), new(NoteParts.BODY, hold.Done, hold.Held && !hold.Done), new(NoteParts.TAIL, hold.Done)];
    }

    public NoteState CreateState(ChartNote note) => new StableManiaHoldState(new NoteTimer(note.EndTime + windows[3], TimerOrder.BeforeInput),
        null, null, null, false, false, false);

    public IEnumerable<ScoringOpportunity> GetScoringOpportunities(ChartNote note)
    {
        yield return new ScoringOpportunity(note.Id, NoteParts.TAIL, 300);
    }

    public InputCandidate? GetCandidate(NoteContext context, InputEvent input, int noteIndex)
    {
        var time = clockTime(context.Time);
        var state = (StableManiaHoldState)context.State;
        if (time < context.Note.StartTime - windows[^1] || time >= context.Note.EndTime + windows[3])
            return null;

        if (input.Pressed)
            return state.Held ? null : new InputCandidate(noteIndex, context.Note.StartTime, time - context.Note.StartTime, 1);

        return state.Held && !context.Input.Held ? new InputCandidate(noteIndex, context.Note.StartTime, time - context.Note.EndTime, 0) : null;
    }

    public NoteUpdate ApplyInput(NoteContext context, InputEvent input)
    {
        var state = (StableManiaHoldState)context.State;
        var time = clockTime(context.Time);

        if (input.Pressed)
        {
            state = state with { Held = true, PressTime = time, TickTime = Math.Max(context.Note.StartTime, time + tick_interval) };
            return NoteUpdate.Silent(schedule(context, state));
        }

        state = state with { Held = false, ReleaseTime = time, TickTime = null };
        if (time < context.Note.EndTime - windows[4])
            return new NoteUpdate(schedule(context, state with { Broken = true }), [new NoteEmission(NoteParts.BODY, ManiaResults.HOLD_BREAK, context.Note.EndTime, time - context.Note.EndTime)]);

        return resolve(context, state);
    }

    public NoteUpdate ApplyTimer(NoteContext context)
    {
        var state = (StableManiaHoldState)context.State;

        if (state.TickTime == context.Time && context.Time <= context.Note.EndTime)
        {
            var next = schedule(context, state with { TickTime = context.Time + tick_interval });
            return new NoteUpdate(next, [new NoteEmission(NoteParts.BODY, ManiaResults.HOLD_TICK, context.Time)]);
        }

        if (state.Held && context.Time < context.Note.EndTime + windows[4])
            return NoteUpdate.Silent(state with { Timer = new NoteTimer(context.Note.EndTime + windows[4], TimerOrder.BeforeInput) });

        return resolve(context, state);
    }

    private NoteUpdate resolve(NoteContext context, StableManiaHoldState state)
    {
        var note = context.Note;
        var press = state.PressTime is not { } head || head < note.StartTime - windows[4] ? note.EndTime - 1000
            : head >= note.StartTime ? head : 2 * note.StartTime - head;
        var release = state.Held ? clockTime(context.Time)
            : state.ReleaseTime is not { } tail ? 0
            : tail <= note.EndTime ? tail : 2 * note.EndTime - tail;
        var headError = Math.Abs(press - note.StartTime);
        var combined = headError + Math.Abs(release - note.EndTime);
        var result = ManiaResults.MEH;

        if (release < note.EndTime - windows[4])
            result = ManiaResults.MISS;
        else
        {
            ResultId[] grades = [ManiaResults.PERFECT, ManiaResults.GREAT, ManiaResults.GOOD, ManiaResults.OK];

            for (var i = 0; i < grades.Length; i++)
            {
                var factor = i == 0 ? 1.2 : i == 1 ? 1.1 : 1;

                if (headError <= windows[i] * factor && combined <= windows[i] * factor * 2)
                {
                    result = state.Broken && i < 2 ? ManiaResults.GOOD : grades[i];
                    break;
                }
            }
        }

        return new NoteUpdate(state with { Done = true, Timer = null, TickTime = null },
            [new NoteEmission(NoteParts.TAIL, result, note.EndTime, (state.Held ? clockTime(context.Time) : state.ReleaseTime) - note.EndTime)]);
    }

    private StableManiaHoldState schedule(NoteContext context, StableManiaHoldState state)
    {
        var deadline = context.Note.EndTime + windows[3];
        var tick = state.Held && state.TickTime is { } tickTime && tickTime <= context.Note.EndTime ? tickTime : long.MaxValue;
        return state with { Timer = tick < deadline ? new NoteTimer(tick) : new NoteTimer(deadline, TimerOrder.BeforeInput) };
    }

    private static long clockTime(long time) => time / 1000 * 1000;
}
