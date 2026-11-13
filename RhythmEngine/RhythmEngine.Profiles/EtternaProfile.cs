using System.Collections.Immutable;
using RhythmEngine.Profiles.Patterns;

namespace RhythmEngine.Profiles;

public sealed record EtternaOptions(int Judge = 4, double LifeDifficulty = 1);

public static class EtternaProfile
{
    public static CompiledProfile Create(KeyChart source, EtternaOptions? options = null)
    {
        options ??= new EtternaOptions();
        ProfileSupport.RequireRange(options.LifeDifficulty, 0.01, 100, nameof(options.LifeDifficulty));
        var scale = options.Judge switch
        {
            1 => 1.5, 2 => 1.33, 3 => 1.16, 4 => 1, 5 => 0.84, 6 => 0.66, 7 => 0.5, 8 => 0.33, 9 => 0.2,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        scale = Math.Min(1, scale);
        // Etterna caps scaled tap windows at its fixed 180ms search/miss horizon.
        var windows = ProfileSupport.Symmetric(("w1", Math.Min(180, 22.5 * scale)), ("w2", Math.Min(180, 45 * scale)),
            ("w3", Math.Min(180, 90 * scale)), ("w4", Math.Min(180, 135 * scale)), ("w5", 180));
        var timeline = new EtternaSearchTimeline(source);
        var holdIntervals = source.Notes.Where(n => n.Kind is KeyNoteKind.Hold or KeyNoteKind.Roll)
            .GroupBy(n => n.Lane).ToImmutableDictionary(g => g.Key, g => g.ToImmutableArray());
        var chart = source.Compile(note => new EtternaNoteRule(createRule(note, windows), timeline, timeline.RowAt(note.StartTime)));
        var lifeDifficulty = (float)options.LifeDifficulty;
        var health = HealthRules.LatchedGauge(0, 1, 0.5, context =>
        {
            if (context.Health.HasFailed)
                return 0;

            var delta = context.Event.Result switch
            {
                "w1" or "w2" or "held" => 0.008f,
                "w3" => 0.004f,
                "w4" or "mine-avoided" or "missed-hold" => 0f,
                "w5" => -0.04f,
                "miss" or "let-go" => -0.08f,
                "mine-hit" => -0.16f,
                _ => throw new InvalidOperationException($"Undefined Etterna health result {context.Event.Result}."),
            };
            var change = delta > 0 ? delta * lifeDifficulty : delta / lifeDifficulty;
            return ((float)context.Health.Value + change) - context.Health.Value;
        }, value => value <= 0, value => value > 0);
        var descriptor = new ProfileDescriptor("etterna-wife3-v1", "2d706147cc4bcf95a8ceb24dc4f4129be207f803", EvidenceLevel.PublicSource,
            ["https://github.com/etternagame/etterna/tree/2d706147cc4bcf95a8ceb24dc4f4129be207f803"],
            ["CountNotesSeparately, immediate hold let-go and the fallback life table; chart warps, fakes and combo segments must be normalized by the importer."]);
        return new CompiledProfile(chart, new SessionConfiguration(descriptor.Id, createScore(scale), health, context =>
        {
            var activeHold = holdIntervals.TryGetValue(context.Input.Lane, out var intervals)
                             && intervals.Any(n => n.StartTime <= context.Input.Time && n.EndTime > context.Input.Time);
            var eligible = context.Candidates.Where(c => !c.Shared).ToArray();
            var row = timeline.RowAt(context.Input.Time);
            InputCandidate? previous = eligible.Where(c => rowOf(c) < row).OrderByDescending(c => c.StartTime)
                .ThenBy(c => c.NoteIndex).Select(c => (InputCandidate?)c).FirstOrDefault();
            InputCandidate? next = eligible.Where(c => rowOf(c) >= row).OrderBy(c => c.StartTime)
                .ThenBy(c => c.NoteIndex).Select(c => (InputCandidate?)c).FirstOrDefault();
            var target = previous is { } before && next is { } after
                ? source.Notes[before.NoteIndex].Kind == KeyNoteKind.Mine || Math.Abs(before.Offset) >= Math.Abs(after.Offset) ? next : previous
                : previous ?? next;
            if (activeHold && target is { } selected && source.Notes[selected.NoteIndex].Kind is not (KeyNoteKind.Hold or KeyNoteKind.Roll or KeyNoteKind.Mine)
                && Math.Abs(selected.Offset) > Time.FromMilliseconds(135))
                target = null;

            return target is { } chosen ? [.. context.Candidates.Where(c => c.Shared), chosen] : [.. context.Candidates.Where(c => c.Shared)];

            int rowOf(InputCandidate candidate) => ((EtternaNoteRule)chart.Notes[candidate.NoteIndex].Rule).Row;
        }), descriptor);
    }

    /// <summary>Wife3 credit per tap, using milliseconds and the selected judge's timing scale.</summary>
    public static double Wife3(double offsetMilliseconds, double timingScale = 1)
    {
        ProfileSupport.RequireRange(timingScale, 0.001, 1.5, nameof(timingScale));
        var offset = Math.Abs(offsetMilliseconds);
        if (offset <= 5 * timingScale)
            return 2;

        var zero = 65 * Math.Pow(timingScale, 0.75);
        var deviation = 22.7 * Math.Pow(timingScale, 0.75);
        if (offset <= zero)
            return 2 * erf((zero - offset) / deviation);

        var maximum = 180 * timingScale;
        return offset <= maximum ? (offset - zero) * -5.5 / (maximum - zero) : -5.5;
    }

    private static double erf(double value)
    {
        var sign = value < 0 ? -1 : 1;
        var x = Math.Abs(value);
        var t = 1 / (1 + 0.3275911 * x);
        var y = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592)
            * t * Math.Exp(-x * x);
        return sign * y;
    }

    private static INoteRule createRule(KeyNote note, TimingWindows windows)
    {
        if (note.Kind == KeyNoteKind.Mine)
        {
            return new MineRule(Time.FromMilliseconds(75), Time.FromMilliseconds(75), "mine-hit", "mine-avoided", shareInput: false,
                expiryWindow: Time.FromMilliseconds(180));
        }

        if (note.Kind is KeyNoteKind.Tap or KeyNoteKind.Lift)
            return new TapRule(new TapConfiguration(windows, Time.FromMilliseconds(180), "miss", 2, note.Kind == KeyNoteKind.Lift));

        if (note.Kind is not (KeyNoteKind.Hold or KeyNoteKind.Roll))
            throw ProfileSupport.Unsupported("Etterna", note.Kind);

        var tail = ProfileSupport.Symmetric(("held", 0));
        return new HoldRule(new HoldConfiguration(windows, tail, Time.FromMilliseconds(180), 0,
            "miss", "held", "let-go", 2, 0, HoldEndMode.Automatic, HoldDropMode.Grace,
            evaluation => new NoteEmission("tail", evaluation.HeadResult == "miss" ? "missed-hold" : evaluation.TailResult, evaluation.Note.EndTime))
        {
            Grace = Time.FromMilliseconds(note.Kind == KeyNoteKind.Roll ? 500 : 250),
            RequiresRetrigger = note.Kind == KeyNoteKind.Roll,
            ContinueAfterMissedHead = true,
            ObserveActiveInput = true,
        });
    }

    private static ScoreConfiguration createScore(double scale)
    {
        return new ScoreConfiguration(new ScoreState(), (state, context) =>
        {
            var result = context.Event.Result;
            var points = result switch
            {
                "mine-hit" => -7,
                "let-go" or "missed-hold" => -4.5,
                "mine-avoided" or "held" => 0,
                "miss" => -5.5,
                _ => Wife3(Time.ToMilliseconds(context.Event.Offset!.Value), scale),
            };
            var maximum = state.AccuracyMaximum + (context.Opportunity?.MaximumAccuracy ?? 0);
            var combo = context.Opportunity == null ? state.Combo : result is "w1" or "w2" or "w3" ? state.Combo + 1 : 0;
            var numerator = state.AccuracyPoints + points;
            return state with
            {
                Score = context.Plan.MaximumAccuracy == 0 ? 0 : 100 * numerator / context.Plan.MaximumAccuracy,
                AccuracyPoints = numerator,
                AccuracyMaximum = maximum,
                Combo = combo,
                MaximumCombo = Math.Max(state.MaximumCombo, combo),
            };
        }, plan => plan.MaximumAccuracy > 0 ? 100 : 0);
    }
}
