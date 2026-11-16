using System.Collections.Immutable;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using RhythmEngine.Profiles.Patterns;

namespace RhythmEngine.Profiles.Etterna;

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
        var windows = ProfileSupport.Symmetric((EtternaResults.W1, Math.Min(180, 22.5 * scale)), (EtternaResults.W2, Math.Min(180, 45 * scale)),
            (EtternaResults.W3, Math.Min(180, 90 * scale)), (EtternaResults.W4, Math.Min(180, 135 * scale)), (EtternaResults.W5, 180));
        var timeline = new EtternaSearchTimeline(source);
        var holdIntervals = source.Notes.Where(n => n.Kind is KeyNoteKind.Hold or KeyNoteKind.Roll)
            .GroupBy(n => n.Lane).ToImmutableDictionary(g => g.Key, g => g.ToImmutableArray());
        var chart = compileChart(source, windows, timeline);
        var lifeDifficulty = (float)options.LifeDifficulty;
        var health = HealthRules.LatchedGauge(0, 1, 0.5, context =>
        {
            if (context.Health.HasFailed)
                return 0;

            var delta = context.Event.Result switch
            {
                var id when id == EtternaResults.W1 || id == EtternaResults.W2 || id == EtternaResults.HELD => 0.008f,
                var id when id == EtternaResults.W3 => 0.004f,
                var id when id == EtternaResults.W4 || id == EtternaResults.MINE_AVOIDED || id == EtternaResults.MISSED_HOLD => 0f,
                var id when id == EtternaResults.W5 => -0.04f,
                var id when id == EtternaResults.MISS || id == EtternaResults.LET_GO => -0.08f,
                var id when id == EtternaResults.MINE_HIT => -0.16f,
                _ => throw new InvalidOperationException($"Undefined Etterna health result {context.Event.Result}."),
            };
            var change = delta > 0 ? delta * lifeDifficulty : delta / lifeDifficulty;
            return (float)context.Health.Value + change - context.Health.Value;
        }, value => value <= 0, value => value > 0);
        var descriptor = new ProfileDescriptor("etterna-wife3-v1", "2d706147cc4bcf95a8ceb24dc4f4129be207f803", EvidenceLevel.PublicSource,
            ["https://github.com/etternagame/etterna/tree/2d706147cc4bcf95a8ceb24dc4f4129be207f803"],
            ["CountNotesSeparately, immediate hold let-go and the fallback life table; chart warps, fakes and combo segments must be normalized by the importer."]);
        return new CompiledProfile(chart, new SessionConfiguration(descriptor.Id, createScore(scale), health, context =>
        {
            var activeHold = holdIntervals.TryGetValue(context.Input.Lane, out var intervals)
                             && intervals.Any(n => n.StartTime <= context.Input.Time && n.EndTime > context.Input.Time);
            var search = timeline.SearchAt(context.Input.Time);
            InputCandidate? previous = null;
            InputCandidate? next = null;

            foreach (var candidate in context.Candidates)
            {
                if (candidate.Shared)
                    continue;

                var row = ((EtternaNoteRule)chart.Notes[candidate.NoteIndex].Rule).Row;
                if (candidate.Priority != 0 && (row < search.Row - search.Radius || row >= search.Row + search.Radius))
                    continue;

                if (row < search.Row)
                {
                    if (previous is not { } priorCandidate || candidate.StartTime > priorCandidate.StartTime
                                                           || (candidate.StartTime == priorCandidate.StartTime && candidate.NoteIndex < priorCandidate.NoteIndex))
                        previous = candidate;
                }
                else if (next is not { } nextCandidate || candidate.StartTime < nextCandidate.StartTime
                                                       || (candidate.StartTime == nextCandidate.StartTime && candidate.NoteIndex < nextCandidate.NoteIndex))
                    next = candidate;
            }

            var target = previous is { } before && next is { } after
                ? source.Notes[before.NoteIndex].Kind == KeyNoteKind.Mine || Math.Abs(before.Offset) >= Math.Abs(after.Offset) ? next : previous
                : previous ?? next;
            if (activeHold && target is { } selected && source.Notes[selected.NoteIndex].Kind is not (KeyNoteKind.Hold or KeyNoteKind.Roll or KeyNoteKind.Mine)
                && Math.Abs(selected.Offset) > Time.FromMilliseconds(135))
                target = null;

            return target is { } chosen ? [.. context.Candidates.Where(c => c.Shared), chosen] : [.. context.Candidates.Where(c => c.Shared)];
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
        var y = 1 - ((((1.061405429 * t - 1.453152027) * t + 1.421413741) * t - 0.284496736) * t + 0.254829592)
            * t * Math.Exp(-x * x);
        return sign * y;
    }

    private static Chart compileChart(KeyChart source, TimingWindows windows, EtternaSearchTimeline timeline)
    {
        var rules = new Dictionary<KeyNoteKind, INoteRule>();
        return source.Compile(note =>
        {
            if (!rules.TryGetValue(note.Kind, out var rule))
                rules.Add(note.Kind, rule = createRule(note.Kind, windows));
            return new EtternaNoteRule(rule, timeline.RowAt(note.StartTime));
        }, note => scoringOpportunities(note));
    }

    private static INoteRule createRule(KeyNoteKind kind, TimingWindows windows)
    {
        if (kind == KeyNoteKind.Mine)
        {
            return new MineRule(Time.FromMilliseconds(75), Time.FromMilliseconds(75), EtternaResults.MINE_HIT, EtternaResults.MINE_AVOIDED, shareInput: false,
                expiryWindow: Time.FromMilliseconds(180));
        }

        if (kind is KeyNoteKind.Tap or KeyNoteKind.Lift)
            return new TapRule(new TapConfiguration(windows, Time.FromMilliseconds(180), EtternaResults.MISS, kind == KeyNoteKind.Lift));

        if (kind is not (KeyNoteKind.Hold or KeyNoteKind.Roll))
            throw ProfileSupport.Unsupported("Etterna", kind);

        var tail = ProfileSupport.Symmetric((EtternaResults.HELD, 0));
        return new HoldRule(new HoldConfiguration(windows, tail, Time.FromMilliseconds(180), 0,
            EtternaResults.MISS, EtternaResults.HELD, EtternaResults.LET_GO, HoldEndMode.Automatic, HoldDropMode.Grace,
            evaluation => new NoteEmission(NoteParts.TAIL, evaluation.HeadResult == EtternaResults.MISS ? EtternaResults.MISSED_HOLD : evaluation.TailResult, evaluation.Note.EndTime))
        {
            Grace = Time.FromMilliseconds(kind == KeyNoteKind.Roll ? 500 : 250),
            RequiresRetrigger = kind == KeyNoteKind.Roll,
            ContinueAfterMissedHead = true,
            ObserveActiveInput = true,
        });
    }

    private static ImmutableArray<ScoringOpportunity> scoringOpportunities(KeyNote note) => note.Kind switch
    {
        KeyNoteKind.Tap or KeyNoteKind.Lift => [new ScoringOpportunity(note.Id, NoteParts.TAP, 2)],
        KeyNoteKind.Hold or KeyNoteKind.Roll => [new ScoringOpportunity(note.Id, NoteParts.HEAD, 2)],
        _ => [],
    };

    private static ScoreConfiguration createScore(double scale)
    {
        return new ScoreConfiguration(new ScoreState(), (state, context) =>
        {
            var result = context.Event.Result;
            var points = result switch
            {
                var id when id == EtternaResults.MINE_HIT => -7,
                var id when id == EtternaResults.LET_GO || id == EtternaResults.MISSED_HOLD => -4.5,
                var id when id == EtternaResults.MINE_AVOIDED || id == EtternaResults.HELD => 0,
                var id when id == EtternaResults.MISS => -5.5,
                _ => Wife3(Time.ToMilliseconds(context.Event.Offset!.Value), scale),
            };
            var maximum = state.AccuracyMaximum + (context.Opportunity?.MaximumAccuracy ?? 0);
            var combo = context.Opportunity == null ? state.Combo : (result == EtternaResults.W1 || result == EtternaResults.W2 || result == EtternaResults.W3) ? state.Combo + 1 : 0;
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
