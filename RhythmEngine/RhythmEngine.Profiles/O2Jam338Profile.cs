using System.Collections.Immutable;
using RhythmEngine.Profiles.Patterns;

namespace RhythmEngine.Profiles;

public readonly record struct O2JamClockPoint(int Milliseconds, int Tick);

public sealed record O2JamTickNote(int Id, int Lane, int HeadTick, int? TailTick = null);

/// <summary>The original client's ordered integer clock anchors, with 192 ticks per measure.</summary>
public sealed class O2JamClock
{
    public ImmutableArray<O2JamClockPoint> Points { get; }

    public O2JamClock(IEnumerable<O2JamClockPoint> points)
    {
        Points = [..points];
        if (Points.Length < 2 || Points[0].Milliseconds < 0 || Points[0].Tick < 0 || Points[^1].Tick >= 1_000_000_000
            || Points.Zip(Points.Skip(1)).Any(p => p.First.Milliseconds >= p.Second.Milliseconds || p.First.Tick >= p.Second.Tick))
            throw new ArgumentException("O2Jam requires at least two clock anchors in strict time and tick order.", nameof(points));
    }

    public int TickAt(long time)
    {
        if (time < Points[0].Milliseconds * 1000L)
            return Points[0].Tick - 1;

        var milliseconds = time / 1000;
        if (milliseconds >= Points[^1].Milliseconds)
            return 1_000_000_000;

        var index = findSegment(milliseconds, false);
        var before = Points[index];
        var after = Points[index + 1];
        return (int)((double)(milliseconds - before.Milliseconds) / (after.Milliseconds - before.Milliseconds)
            * (after.Tick - before.Tick) + before.Tick);
    }

    public long TimeAt(int tick)
    {
        if (tick <= Points[0].Tick)
            return Points[0].Milliseconds * 1000L;
        if (tick >= Points[^1].Tick)
            return Points[^1].Milliseconds * 1000L;

        var index = findSegment(tick, true);
        var before = Points[index];
        var after = Points[index + 1];
        var low = before.Milliseconds;
        var high = after.Milliseconds;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (TickAt(middle * 1000L) < tick)
                low = middle + 1;
            else
                high = middle;
        }

        return low * 1000L;
    }

    private int findSegment(long value, bool ticks)
    {
        var low = 0;
        var high = Points.Length - 1;
        while (high - low > 1)
        {
            var middle = low + (high - low) / 2;
            if ((ticks ? Points[middle].Tick : Points[middle].Milliseconds) <= value)
                low = middle;
            else
                high = middle;
        }

        return low;
    }
}

/// <summary>Shipped e-Games 3.38 rules, compiled independently of the Open2Jam compatibility profile.</summary>
public static class O2Jam338Profile
{
    public static CompiledProfile Create(int laneCount, IEnumerable<O2JamTickNote> notes, O2JamClock clock,
                                         O2JamDifficulty difficulty = O2JamDifficulty.Normal)
    {
        if (!Enum.IsDefined(difficulty))
            throw new ArgumentOutOfRangeException(nameof(difficulty));

        var definitions = notes.ToImmutableArray();
        if (definitions.Any(n => n.HeadTick < clock.Points[0].Tick || n.HeadTick >= clock.Points[^1].Tick
                                                                   || (n.TailTick is { } tail && (tail <= n.HeadTick || tail >= clock.Points[^1].Tick)))
            || definitions.GroupBy(n => (n.Lane, n.HeadTick)).Any(g => g.Count() > 1))
            throw new ArgumentException("Notes require unique lane/tick cells and ordered endpoints inside the clock.", nameof(notes));

        var chart = new Chart(laneCount, definitions.Select(n => new ChartNote(n.Id, n.Lane, clock.TimeAt(n.HeadTick),
            clock.TimeAt(n.TailTick ?? n.HeadTick), new O2Jam338NoteRule(clock, n.HeadTick, n.TailTick))));
        int[] life = difficulty switch { O2JamDifficulty.Easy => [3, 2, -10, -50], O2JamDifficulty.Normal => [2, 1, -7, -40], _ => [1, 0, -5, -30] };
        var health = HealthRules.LatchedGauge(0, 1000, 1000, context => context.Health.HasFailed ? 0 : life[resultIndex(context.Event.Result)],
            value => value <= 0, value => value > 0);
        var descriptor = new ProfileDescriptor("o2jam-egames-3.38", "OTwo-3f40d7f4e828", EvidenceLevel.StaticBinary,
            ["https://www.mediafire.com/file/lgd4gp8azgmryie/O2Jam-3.38.zip/file"],
            [
                "Original integer millisecond clock anchors and note tick positions are required; they preserve tempo-boundary quantisation.",
                "Single-player normal play; couple/course, avatar items, network correction and client frame/audio scheduling are outside this preset. Stop the host session on failure.",
                "Accuracy is normalized 200/100/4/0 credit. Combo and maximum combo use the original displayed chain count, which starts at zero for the first hit."
            ]);
        return new CompiledProfile(chart, new SessionConfiguration(descriptor.Id, createScore(), health, context =>
            [.. context.Candidates.OrderBy(c => c.Priority).ThenBy(c => Math.Abs(c.Offset)).ThenByDescending(c => c.Offset).ThenBy(c => c.NoteIndex).Take(1)]), descriptor);
    }

    private static int resultIndex(string result) => result switch
    {
        "cool" => 0, "good" => 1, "bad" => 2, "miss" => 3,
        _ => throw new InvalidOperationException($"Undefined O2Jam result {result}."),
    };

    private static ScoreConfiguration createScore()
    {
        var values = new[] { 200, 100, 4, 0 };
        return new ScoreConfiguration(new ScoreState(), (state, context) =>
            {
                var result = context.Event.Result;
                var index = resultIndex(result);
                var raw = context.Event.OriginalResult;
                var pills = state.Counters.GetValueOrDefault("pills");
                var streak = state.Counters.GetValueOrDefault("cool-streak");
                if (raw == "cool" && pills < 5)
                {
                    streak++;
                    if (streak > 14)
                    {
                        pills++;
                        streak = 0;
                    }
                }
                else if (raw != "cool")
                    streak = 0;

                if (raw == "bad" && result == "cool")
                    pills--;

                var jam = state.Counters.GetValueOrDefault("jam");
                var progress = state.Counters.GetValueOrDefault("jam-progress");
                var scoreDelta = index switch { 0 => 10 * (jam + 20), 1 => 5 * (jam + 20), 2 => 4, _ => -10 };
                var chain = index < 2 ? state.Counters.GetValueOrDefault("chain") + 1 : 0;
                progress = index switch { 0 => progress + 2, 1 => progress + 1, _ => 0 };
                if (index >= 2)
                    jam = 0;
                else if (progress > 50 * (jam + 1))
                    jam++;

                return state with
                {
                    Score = Math.Max(0, state.Score + scoreDelta),
                    AccuracyPoints = state.AccuracyPoints + values[index],
                    AccuracyMaximum = state.AccuracyMaximum + 200,
                    Combo = (int)Math.Max(0, chain - 1),
                    MaximumCombo = Math.Max(state.MaximumCombo, (int)Math.Max(0, chain - 1)),
                    Counters = state.Counters.SetItem("chain", chain).SetItem("pills", pills).SetItem("cool-streak", streak)
                        .SetItem("jam", jam).SetItem("jam-progress", progress)
                        .SetItem(result, state.Counters.GetValueOrDefault(result) + 1),
                };
            }, plan => Enumerable.Range(0, plan.Opportunities.Length).Sum(i => 200 + 10 * Math.Max(0, Math.Floor((2 * i - 1) / 50.0))),
            (state, judgement) => judgement.Result == "bad" && state.Counters.GetValueOrDefault("pills") > 0
                ? judgement with { Result = "cool" }
                : judgement);
    }
}

internal sealed record O2Jam338NoteState(NoteTimer? Timer, bool HeadResolved, bool Done) : NoteState
{
    public override NoteTimer? NextTimer => Timer;

    public override bool Completed => Done;
}

internal sealed class O2Jam338NoteRule(O2JamClock clock, int headTick, int? tailTick) : INoteRule
{
    public ImmutableArray<NotePartState> GetParts(NoteContext context)
    {
        var current = (O2Jam338NoteState)context.State;
        return tailTick != null ? [new("head", current.HeadResolved), new("body", current.Done, current.HeadResolved && !current.Done), new("tail", current.Done)]
            : [new("head", current.HeadResolved)];
    }

    public NoteState CreateState(ChartNote note) => new O2Jam338NoteState(new NoteTimer(clock.TimeAt(headTick + 26)), false, false);

    public IEnumerable<ScoringOpportunity> GetScoringOpportunities(ChartNote note)
    {
        yield return new ScoringOpportunity(note.Id, "head", note.StartTime, 200);

        if (tailTick != null)
            yield return new ScoringOpportunity(note.Id, "tail", note.EndTime, 200);
    }

    public InputCandidate? GetCandidate(NoteContext context, InputEvent input, int noteIndex)
    {
        var state = (O2Jam338NoteState)context.State;
        if (state.HeadResolved)
            return !input.Pressed && !context.Input.Held
                ? new InputCandidate(noteIndex, context.Note.StartTime, clock.TickAt(input.Time) - tailTick!.Value, 0)
                : null;

        var offset = clock.TickAt(input.Time) - headTick;
        return input.Pressed && input.Time >= clock.Points[0].Milliseconds * 1000L && Math.Abs(offset) <= 25
            ? new InputCandidate(noteIndex, context.Note.StartTime, offset, 1)
            : null;
    }

    public NoteUpdate ApplyInput(NoteContext context, InputEvent input)
    {
        var state = (O2Jam338NoteState)context.State;
        var tick = state.HeadResolved ? tailTick!.Value : headTick;
        var error = Math.Abs(clock.TickAt(context.Time) - tick);
        var result = error <= 6 ? "cool" : error <= 18 ? "good" : error <= 25 ? "bad" : "miss";
        var expectedTime = state.HeadResolved ? context.Note.EndTime : context.Note.StartTime;
        var emission = new NoteEmission(state.HeadResolved ? "tail" : "head", result, expectedTime, context.Time - expectedTime);
        if (state.HeadResolved || tailTick == null)
            return new NoteUpdate(new O2Jam338NoteState(null, true, true), [emission]);
        if (result == "bad")
            return new NoteUpdate(new O2Jam338NoteState(null, true, true), [emission, new NoteEmission("tail", "miss", context.Note.EndTime)]);

        return new NoteUpdate(new O2Jam338NoteState(new NoteTimer(clock.TimeAt(tailTick.Value + 26)), true, false), [emission]);
    }

    public NoteUpdate ApplyTimer(NoteContext context)
    {
        var state = (O2Jam338NoteState)context.State;
        var emissions = state.HeadResolved
            ? [new NoteEmission("tail", "miss", context.Note.EndTime)]
            : tailTick == null
                ? [new NoteEmission("head", "miss", context.Note.StartTime)]
                : ImmutableArray.Create(new NoteEmission("head", "miss", context.Note.StartTime), new NoteEmission("tail", "miss", context.Note.EndTime));
        return new NoteUpdate(new O2Jam338NoteState(null, true, true), emissions);
    }
}
