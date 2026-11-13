using System.Collections.Immutable;

namespace RhythmEngine.Profiles;

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
        return [new("head", hold.PressTime != null || hold.Done), new("body", hold.Done, hold.Held && !hold.Done), new("tail", hold.Done)];
    }

    public NoteState CreateState(ChartNote note) => new StableManiaHoldState(new NoteTimer(note.EndTime + windows[3], TimerOrder.BeforeInput),
        null, null, null, false, false, false);

    public IEnumerable<ScoringOpportunity> GetScoringOpportunities(ChartNote note)
    {
        yield return new ScoringOpportunity(note.Id, "tail", note.EndTime, 300);
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
            return new NoteUpdate(schedule(context, state with { Broken = true }), [new NoteEmission("body", "hold-break", context.Note.EndTime, time - context.Note.EndTime)]);

        return resolve(context, state);
    }

    public NoteUpdate ApplyTimer(NoteContext context)
    {
        var state = (StableManiaHoldState)context.State;
        if (state.TickTime == context.Time && context.Time <= context.Note.EndTime)
        {
            var next = schedule(context, state with { TickTime = context.Time + tick_interval });
            return new NoteUpdate(next, [new NoteEmission("body", "hold-tick", context.Time)]);
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
        var result = "meh";
        if (release < note.EndTime - windows[4])
            result = "miss";
        else
        {
            string[] grades = ["perfect", "great", "good", "ok"];
            for (var i = 0; i < grades.Length; i++)
            {
                var factor = i == 0 ? 1.2 : i == 1 ? 1.1 : 1;
                if (headError <= windows[i] * factor && combined <= windows[i] * factor * 2)
                {
                    result = state.Broken && i < 2 ? "good" : grades[i];
                    break;
                }
            }
        }

        return new NoteUpdate(state with { Done = true, Timer = null, TickTime = null },
            [new NoteEmission("tail", result, note.EndTime, (state.Held ? clockTime(context.Time) : state.ReleaseTime) - note.EndTime)]);
    }

    private StableManiaHoldState schedule(NoteContext context, StableManiaHoldState state)
    {
        var deadline = context.Note.EndTime + windows[3];
        var tick = state.Held && state.TickTime is { } tickTime && tickTime <= context.Note.EndTime ? tickTime : long.MaxValue;
        return state with { Timer = tick < deadline ? new NoteTimer(tick) : new NoteTimer(deadline, TimerOrder.BeforeInput) };
    }

    private static long clockTime(long time) => time / 1000 * 1000;
}
