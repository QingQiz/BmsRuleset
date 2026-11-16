using System.Collections.Immutable;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using RhythmEngine.Profiles.Patterns;

namespace RhythmEngine.Profiles.Bms;

public static class Lr2Beta3Profile
{
    public static CompiledProfile Create(KeyChart source, Lr2Beta3Options? options = null)
    {
        options ??= new Lr2Beta3Options();
        if (options.Rank is < BmsRank.VeryHard or > BmsRank.Easy || !Enum.IsDefined(options.Gauge) || options.Total <= 0)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (source.Notes.Any(n => n.StartTime < 0 || n.StartTime % 1000 != 0 || n.EndTime % 1000 != 0))
            throw new ArgumentException("This LR2 preset requires nonnegative integer-millisecond chart times.", nameof(source));

        int[] bounds = options.Rank switch
        {
            BmsRank.VeryHard => [8, 24, 40], BmsRank.Hard => [15, 30, 60],
            BmsRank.Normal => [18, 40, 100], _ => [21, 60, 120],
        };
        var chart = source.Compile(note =>
        {
            if (note.Kind is not (KeyNoteKind.Tap or KeyNoteKind.Hold))
                throw ProfileSupport.Unsupported("LR2 beta3 100201", note.Kind);
            if (note.Kind == KeyNoteKind.Hold && note.EndTime <= note.StartTime)
                throw new ArgumentException("LR2 LN endpoints must have positive duration.", nameof(source));

            return new Lr2Beta3NoteRule([.. bounds], note.Kind == KeyNoteKind.Hold);
        }, note => [new ScoringOpportunity(note.Id, NoteParts.NOTE, 2)]);
        var values = ProfileSupport.Values((BmsResults.PGREAT, 2, 2, ComboChange.Increment), (BmsResults.GREAT, 1, 1, ComboChange.Increment),
            (BmsResults.GOOD, 0, 0, ComboChange.Increment), (BmsResults.BAD, 0, 0, ComboChange.Reset), (BmsResults.POOR, 0, 0, ComboChange.Reset));
        var score = new ScoreConfiguration(new ScoreState(), (state, context) => ProfileSupport.Accumulate(state, context, values), p => p.MaximumAccuracy);
        var descriptor = new ProfileDescriptor("lr2-beta3-100201", "LR2body-a46ef15343e4", EvidenceLevel.StaticBinary,
            ["https://www.mediafire.com/file/96m0wbp0aw343pu/LR2_100201.zip/file", "https://archive.org/details/lr2beta3"],
            [
                "Pins the unmodified beta3 100201 executable. Normal integer-millisecond key play, LN and four standard gauges; the LunaticVibes compatibility preset remains separate.",
                "Importer supplies integer-millisecond chart times and normalized positive TOTAL. PMS, dynamic RANK events, judge mods, course gauges and client frame/audio scheduling are outside this preset.",
            ]);
        return new CompiledProfile(chart, new SessionConfiguration(descriptor.Id, score, createHealth(source.Notes.Length, options), context =>
        {
            var active = context.Candidates.Where(c => c.Priority == 0).OrderBy(c => c.StartTime).ThenBy(c => c.NoteIndex).Take(1).ToImmutableArray();
            if (!active.IsEmpty)
                return active;

            var shared = context.Candidates.Where(c => c.Shared).OrderBy(c => c.StartTime).ThenBy(c => c.NoteIndex).ToImmutableArray();
            return
            [
                .. shared, .. context.Candidates.Where(c => !c.Shared && (shared.IsEmpty || Math.Abs(c.Offset) <= 200_000))
                    .OrderBy(c => c.Priority).ThenBy(c => c.StartTime).ThenBy(c => c.NoteIndex).Take(1),
            ];
        }), descriptor);
    }

    private static HealthConfiguration createHealth(int count, Lr2Beta3Options options)
    {
        var recovery = count == 0 ? 0 : (double)options.Total / count;
        var normal = options.Gauge is Lr2Beta3Gauge.Normal or Lr2Beta3Gauge.Easy;
        var damage = Math.Max(noteDamage(count), 10.0 / Math.Max(1, (int)((options.Total - 80.0) * 0.125) / 2));
        double[] coefficients = options.Gauge switch
        {
            Lr2Beta3Gauge.Normal => [-2, -6, -4, recovery / 2, recovery, recovery],
            Lr2Beta3Gauge.Easy => [-1.6, -4.8, -3.2, recovery / 2 * 1.2, recovery * 1.2, recovery * 1.2],
            Lr2Beta3Gauge.Hard => [-2 * damage, -10 * damage, -6 * damage, 0.05, 0.1, 0.1],
            _ => [0, -100, -100, 0, 0, 0],
        };
        return HealthRules.LatchedGauge(normal ? 2 : 0, 100, normal ? 20 : 100, context =>
        {
            var index = context.Event.Result switch
            {
                var id when id == BmsResults.EMPTY_POOR => 0,
                var id when id == BmsResults.POOR => 1,
                var id when id == BmsResults.BAD => 2,
                var id when id == BmsResults.GOOD => 3,
                var id when id == BmsResults.GREAT => 4,
                var id when id == BmsResults.PGREAT => 5,
                _ => -1,
            };
            if (index < 0)
                throw new InvalidOperationException($"Undefined LR2 result {context.Event.Result}.");

            var quantized = 2 * ((int)context.Health.Value / 2);
            var increment = coefficients[index];
            if (!normal && quantized <= 30 && index <= 2)
                increment *= 0.6;
            var next = Math.Clamp(context.Health.Value + increment, normal ? 2 : 0, 100);
            if (!normal && quantized == 0)
                next = 0;
            return next - context.Health.Value;
        }, value => !normal && value < 2, value => normal ? value >= 80 : value >= 2);
    }

    private static double noteDamage(int count) => count switch
    {
        < 20 => 10, < 30 => 10 - 2 * ((count - 20.0) / 10), < 45 => 7 - (count - 30.0) / 15,
        < 60 => 6 - (count - 45.0) / 15, < 125 => 5 - (count - 60.0) / 65,
        < 250 => 4 - (count - 125.0) / 125, < 500 => 3 - (count - 250.0) / 250,
        < 1000 => 2 - (count - 500.0) / 500, _ => 1,
    };
}

internal sealed record Lr2Beta3NoteState(NoteTimer? Timer, ResultId? Head, long? Offset, bool Done) : NoteState
{
    public override NoteTimer? NextTimer => Timer;

    public override bool Completed => Done;
}

internal sealed class Lr2Beta3NoteRule(ImmutableArray<int> bounds, bool hold) : INoteRule
{
    public ImmutableArray<NotePartState> GetParts(NoteContext context)
    {
        var current = (Lr2Beta3NoteState)context.State;
        return hold
            ? [new NotePartState(NoteParts.HEAD, current.Head != null || current.Done), new(NoteParts.BODY, current.Done, current.Head != null && !current.Done), new(NoteParts.NOTE, current.Done)]
            : [new NotePartState(NoteParts.NOTE, current.Done)];
    }

    public NoteState CreateState(ChartNote note) => new Lr2Beta3NoteState(
        new NoteTimer(note.StartTime + (hold ? bounds[2] : 200) * 1000L + 1000, TimerOrder.BeforeInput), null, null, false);

    public InputCandidate? GetCandidate(NoteContext context, InputEvent input, int noteIndex)
    {
        var state = (Lr2Beta3NoteState)context.State;
        if (state.Head != null)
            return input.Pressed || !context.Input.Held ? new InputCandidate(noteIndex, context.Note.StartTime, 0, 0) : null;
        if (!input.Pressed)
            return null;

        var offset = clockTime(input.Time) - context.Note.StartTime;
        if (offset <= -1_000_000)
            return null;

        // A late BAD can recurse into the next cell on the same press.
        var bad = offset > bounds[2] * 1000L && offset <= 200_000;
        return new InputCandidate(noteIndex, context.Note.StartTime, offset, offset < -200_000 ? 2 : 1, !hold && bad);
    }

    public NoteUpdate ApplyInput(NoteContext context, InputEvent input)
    {
        var state = (Lr2Beta3NoteState)context.State;

        if (state.Head is { } head && !input.Pressed)
        {
            var result = clockTime(context.Time) < context.Note.EndTime - bounds[2] * 1000L ? BmsResults.BAD : head;
            return resolve(context, state, result, state.Offset);
        }

        var offset = clockTime(context.Time) - context.Note.StartTime;
        var resultId = grade(offset);
        if (resultId == BmsResults.EMPTY_POOR)
            return new NoteUpdate(state, [new NoteEmission(NoteParts.EMPTY, resultId, context.Note.StartTime, offset)]);
        if (!hold)
            return resolve(context, state, resultId, offset);

        var completion = Math.Max(context.Note.EndTime + 1000, clockTime(context.Time) + 1000);
        return NoteUpdate.Silent(state with { Head = resultId, Offset = offset, Timer = new NoteTimer(completion, TimerOrder.BeforeInput) });
    }

    public NoteUpdate ApplyTimer(NoteContext context)
    {
        var state = (Lr2Beta3NoteState)context.State;
        return resolve(context, state, state.Head ?? BmsResults.POOR, state.Offset);
    }

    private ResultId grade(long offset)
    {
        var error = Math.Abs(offset);
        return error <= bounds[0] * 1000L ? BmsResults.PGREAT
            : error <= bounds[1] * 1000L ? BmsResults.GREAT
            : error <= bounds[2] * 1000L ? BmsResults.GOOD
            : error <= 200_000 ? BmsResults.BAD : BmsResults.EMPTY_POOR;
    }

    private static long clockTime(long time) => time / 1000 * 1000;

    private static NoteUpdate resolve(NoteContext context, Lr2Beta3NoteState state, ResultId result, long? offset)
        => new(state with { Timer = null, Done = true }, [new NoteEmission(NoteParts.NOTE, result, context.Note.StartTime, offset)]);
}
