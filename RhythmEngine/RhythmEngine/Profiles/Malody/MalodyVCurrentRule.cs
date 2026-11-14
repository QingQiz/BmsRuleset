using System.Collections.Immutable;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;

namespace RhythmEngine.Profiles.Malody;

internal sealed record MalodyVTiming(TimingWindows Windows, long GoodWindow, long MissWindow, long CriticalWindow)
{
    internal static MalodyVTiming Create(MalodyJudge judge, bool pro, bool allowEarlyMiss, float scale, int conversionPercent)
    {
        if (!Enum.IsDefined(judge) || !float.IsFinite(scale) || scale <= 0 || conversionPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(judge));

        var offset = new[] { 20, 10, 0, -8, -16 }[(int)judge];
        var best = (pro ? 36 : 45) + offset;
        var cool = (pro ? 76 : 85) + offset;
        var good = (pro ? 110 : 130) + offset;
        var miss = (pro ? 160 : 170) + offset;

        if (conversionPercent > 0)
        {
            best += (good - best) * conversionPercent / 100;
            cool += (good - cool) * conversionPercent / 100;
            if (conversionPercent <= 50)
                miss += (miss - good) * (50 - conversionPercent) / 50;
        }

        var inverseScale = 1 / scale;
        var entries = ImmutableArray.CreateBuilder<TimingWindow>();

        foreach (var (result, bound) in new[] { (MalodyResults.BEST, best), (MalodyResults.COOL, cool), (MalodyResults.GOOD, good) })
        {
            var edge = firstOutside(bound, false);
            entries.Add(new TimingWindow(result, -edge, edge, false, false));
        }

        var missEdge = firstOutside(Math.Min(miss, good + 34), true) - 1;
        entries.Add(new TimingWindow(MalodyResults.MISS, allowEarlyMiss ? -missEdge : 0, missEdge));
        return new MalodyVTiming(new TimingWindows(entries), Time.FromMilliseconds(good), Time.FromMilliseconds(miss), firstOutside(20, true) - 1);

        long firstOutside(int bound, bool inclusive)
        {
            // Freeze the native float comparison into exact microsecond boundaries once per configuration.
            var low = 0L;
            var upperMilliseconds = ((double)bound + 1) * scale;
            if (upperMilliseconds >= long.MaxValue / 1000.0)
                throw new ArgumentOutOfRangeException(nameof(scale));

            var high = Math.Max(1, (long)Math.Ceiling(upperMilliseconds * 1000));

            while (low < high)
            {
                var middle = low + (high - low) / 2;
                var error = (float)Time.ToMilliseconds(middle) * inverseScale;
                if (inclusive ? error <= bound : error < bound)
                    low = middle + 1;
                else
                    high = middle;
            }

            return low;
        }
    }
}

internal sealed class MalodyVCurrentRule(MalodyVTiming timing, bool hold, bool judgeAtEnd) : INoteRule
{
    private const long tick_interval = 100_000;

    public ImmutableArray<NotePartState> GetParts(NoteContext context) => ((MalodyHoldState)context.State).GetParts(hold);

    public NoteState CreateState(ChartNote note) => new MalodyHoldState(
        new NoteTimer(note.StartTime + timing.GoodWindow + 1, TimerOrder.BeforeInput), false, false, null);

    public IEnumerable<ScoringOpportunity> GetScoringOpportunities(ChartNote note)
    {
        yield return new ScoringOpportunity(note.Id, NoteParts.HEAD, 1000);

        if (hold && judgeAtEnd)
            yield return new ScoringOpportunity(note.Id, NoteParts.TAIL, 1000);
    }

    public InputCandidate? GetCandidate(NoteContext context, InputEvent input, int noteIndex)
    {
        var state = (MalodyHoldState)context.State;

        if (state.HeadResolved)
        {
            return input.Pressed && context.Time >= context.Note.EndTime
                ? null
                : new InputCandidate(noteIndex, context.Note.StartTime, context.Time - context.Note.EndTime, 0);
        }

        var offset = context.Time - context.Note.StartTime;
        return input.Pressed && timing.Windows.ResultFor(offset) != null
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
            var result = timing.Windows.ResultFor(offset)!;
            emitGrade(emissions, NoteParts.HEAD, result, context.Note.StartTime, offset);
            state = state with { HeadResolved = true, TickTime = result == MalodyResults.MISS || !hold ? null : context.Note.StartTime + tick_interval };

            if (result == MalodyResults.MISS && hold && judgeAtEnd)
            {
                emissions.Add(new NoteEmission(NoteParts.TAIL, MalodyResults.MISS, context.Note.EndTime));
                return new NoteUpdate(finish(state), emissions.ToImmutable());
            }

            if (!hold || context.Time >= context.Note.EndTime)
                return new NoteUpdate(finish(state), emissions.ToImmutable());
        }
        else if (input.Pressed)
            state = state with { TickTime = context.Time + tick_interval };
        else if (!context.Input.Held && state.TickTime != null)
        {
            state = emitTicks(context.Note, state, Math.Min(context.Time, context.Note.EndTime), emissions);

            if (judgeAtEnd)
            {
                var offset = context.Time - context.Note.EndTime;
                var grade = timing.Windows.ResultFor(offset) switch
                {
                    var id when id == MalodyResults.COOL => MalodyResults.BEST,
                    var id when id == MalodyResults.GOOD => MalodyResults.COOL,
                    { } result => result,
                    _ => MalodyResults.MISS,
                };
                emitGrade(emissions, NoteParts.TAIL, grade, context.Note.EndTime, offset);
                return new NoteUpdate(finish(state), emissions.ToImmutable());
            }

            if (context.Time >= context.Note.EndTime - timing.GoodWindow)
                return new NoteUpdate(finish(state), emissions.ToImmutable());

            emissions.Add(new NoteEmission(NoteParts.BODY, MalodyResults.HOLD_DROP, context.Note.EndTime, context.Time - context.Note.EndTime));
            state = state with { TickTime = null };
        }

        state = emitTicks(context.Note, state, context.Time, emissions);
        return new NoteUpdate(schedule(context, state), emissions.ToImmutable());
    }

    public NoteUpdate ApplyTimer(NoteContext context)
    {
        var state = (MalodyHoldState)context.State;
        var emissions = ImmutableArray.CreateBuilder<NoteEmission>();

        if (!state.HeadResolved)
        {
            state = state with { HeadResolved = true };
            emissions.Add(new NoteEmission(NoteParts.HEAD, MalodyResults.MISS, context.Note.StartTime));
            if (hold && judgeAtEnd)
                emissions.Add(new NoteEmission(NoteParts.TAIL, MalodyResults.MISS, context.Note.EndTime));
            if (!hold || judgeAtEnd)
                return new NoteUpdate(finish(state), emissions.ToImmutable());
        }
        else
        {
            state = emitTicks(context.Note, state, context.Time, emissions);

            if (judgeAtEnd && context.Time > context.Note.EndTime + timing.MissWindow)
            {
                if (state.TickTime != null && context.Input.Held)
                    emissions.Add(new NoteEmission(NoteParts.TAIL, MalodyResults.MISS, context.Note.EndTime));
                return new NoteUpdate(finish(state), emissions.ToImmutable());
            }
        }

        return new NoteUpdate(schedule(context, state), emissions.ToImmutable());
    }

    private void emitGrade(ImmutableArray<NoteEmission>.Builder emissions, PartId part, ResultId result, long expectedTime, long offset)
    {
        emissions.Add(new NoteEmission(part, result, expectedTime, offset));
        // A converted Cool tail is Best without acquiring the separate critical flag.
        if (result == MalodyResults.BEST && timing.Windows.ResultFor(offset) == MalodyResults.BEST && Math.Abs(offset) <= timing.CriticalWindow)
            emissions.Add(new NoteEmission(MalodyParts.CRITICAL, MalodyResults.CRITICAL, expectedTime, offset));
    }

    private static MalodyHoldState emitTicks(ChartNote note, MalodyHoldState state, long until, ImmutableArray<NoteEmission>.Builder emissions)
    {
        while (state.TickTime is { } tick && tick <= until && tick <= note.EndTime)
        {
            emissions.Add(new NoteEmission(NoteParts.BODY, MalodyResults.HOLD_TICK, tick));
            state = state with { TickTime = tick + tick_interval };
        }

        return state;
    }

    private MalodyHoldState schedule(NoteContext context, MalodyHoldState state)
    {
        if (context.Time >= context.Note.EndTime && (!judgeAtEnd || state.TickTime == null))
            return finish(state);

        var next = state.TickTime is { } tick && tick <= context.Note.EndTime ? tick
            : judgeAtEnd ? context.Note.EndTime + timing.MissWindow + 1 : context.Note.EndTime;
        return state with { Timer = new NoteTimer(next) };
    }

    private static MalodyHoldState finish(MalodyHoldState state) => state with { Timer = null, Done = true, TickTime = null };
}
