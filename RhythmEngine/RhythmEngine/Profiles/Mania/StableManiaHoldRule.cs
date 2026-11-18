using System.Collections.Immutable;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using RhythmEngine.Profiles.Patterns;

namespace RhythmEngine.Profiles.Mania;

internal sealed record StableManiaHoldState(NoteTimer? Timer, long? PressTime, long? ReleaseTime, long? TickTime, bool Held, bool Broken, ResultId? Result) : NoteState
{
    public override NoteTimer? NextTimer => Timer;

    public override bool Completed => Result != null;
}

internal sealed class StableManiaHoldRule(ImmutableArray<long> windows) : INoteRule, INoteTiming
{
    private const long tick_interval = 100_000;

    internal int MaximumTicks(ChartNote note)
    {
        // Early presses can start ticking at the head; a late release can include the final tick.
        var earliestPress = (long)Math.Ceiling((note.StartTime - windows[^1]) / 1000.0) * 1000;
        var firstTick = Math.Max(note.StartTime, earliestPress + tick_interval);
        return firstTick > note.EndTime ? 0 : (int)((note.EndTime - firstTick) / tick_interval) + 1;
    }

    // Stable combines head and tail errors. These are the maximum endpoint extents for presentation.
    public NoteTiming Timing { get; } = createTiming(windows);

    private static NoteTiming createTiming(ImmutableArray<long> windows)
    {
        var head = new TimingWindows(new[] { ManiaResults.PERFECT, ManiaResults.GREAT, ManiaResults.GOOD, ManiaResults.OK, ManiaResults.MEH, ManiaResults.MISS }
            .Select((result, index) =>
            {
                var extent = (long)(windows[index] * (index == 0 ? 1.2 : index == 1 ? 1.1 : 1));
                return new TimingWindow(result, -extent, extent);
            }));
        var tail = new TimingWindows(head.Entries.Select(window => window with
        {
            Early = Math.Max(-windows[4], window.Early * 2),
            Late = Math.Min(windows[4], window.Late * 2),
        }));
        return new NoteTiming(head, windows[3], TailDeadline: windows[4], TailWindows: tail, HeadRetriggerDeadline: windows[3]);
    }

    public ImmutableArray<NotePartState> GetParts(NoteContext context)
    {
        var hold = (StableManiaHoldState)context.State;
        return
        [
            new NotePartState(NoteParts.HEAD, hold.PressTime != null || hold.Completed, Result: hold.Result),
            new(NoteParts.BODY, hold.Completed, hold.Held && !hold.Completed,
                Reason: hold.Broken || hold.PressTime > context.Note.StartTime + windows[3] ? HoldReasons.DROP : null),
            new(NoteParts.TAIL, hold.Completed, Offset: hold.ReleaseTime - context.Note.EndTime, Result: hold.Result)
        ];
    }

    public NoteState CreateState(ChartNote note) => new StableManiaHoldState(new NoteTimer(note.EndTime + windows[3], TimerOrder.BeforeInput),
        null, null, null, false, false, null);

    // Millisecond truncation can move a negative session timestamp forward.
    long? INoteRule.EarliestCandidateOffset => -windows[^1] - 999;

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

        return new NoteUpdate(state with { Result = result, Timer = null, TickTime = null },
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
