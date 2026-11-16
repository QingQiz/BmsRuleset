using System.Collections.Immutable;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using RhythmEngine.Profiles.Patterns;

namespace RhythmEngine.Profiles.Mania;

public sealed record ManiaOptions(
    double OverallDifficulty = 8,
    double DrainRate = 5,
    double ClockRate = 1,
    double DifficultyMultiplier = 1,
    bool Converted = false,
    long DrainStartTime = 0);

public static class ManiaProfiles
{
    public static AccuracyGrades Grades { get; } = new(.95, .9, .8, .7);

    public static ScoreGrade GradeFor(double accuracy, IReadOnlyDictionary<ResultId, int> results)
        => accuracy >= Grades.S && new[] { ManiaResults.GOOD, ManiaResults.OK, ManiaResults.MEH, ManiaResults.MISS }.All(result => results.GetValueOrDefault(result) == 0)
            ? ScoreGrade.Perfect
            : Grades.GradeFor(accuracy);

    private static readonly ImmutableDictionary<ResultId, ResultValue> lazer_values = ProfileSupport.Values(
        (ManiaResults.PERFECT, 305, 305, ComboChange.Increment), (ManiaResults.GREAT, 300, 300, ComboChange.Increment),
        (ManiaResults.GOOD, 200, 200, ComboChange.Increment), (ManiaResults.OK, 100, 100, ComboChange.Increment),
        (ManiaResults.MEH, 50, 50, ComboChange.Increment), (ManiaResults.MISS, 0, 0, ComboChange.Reset));

    public static ResultValue GetLazerResultValue(ResultId result) => lazer_values[result];

    private static readonly ImmutableDictionary<ResultId, ResultValue> stable_values = ProfileSupport.Values(
        (ManiaResults.PERFECT, 320, 300, ComboChange.Increment), (ManiaResults.GREAT, 300, 300, ComboChange.Increment),
        (ManiaResults.GOOD, 200, 200, ComboChange.Increment), (ManiaResults.OK, 100, 100, ComboChange.Increment),
        (ManiaResults.MEH, 50, 50, ComboChange.Increment), (ManiaResults.MISS, 0, 0, ComboChange.Reset));

    public static ResultValue GetStableResultValue(ResultId result) => stable_values[result];

    public static int GetStableMaximumCombo(Chart chart)
    {
        var ticks = chart.Notes.Sum(note => note.Rule is StableManiaHoldRule hold ? hold.MaximumTicks(note) : 0);
        return chart.ScorePlan.Opportunities.Length + ticks;
    }

    public static CompiledProfile CreateLazer(KeyChart source, ManiaOptions? options = null) => compile(source, options ?? new ManiaOptions(), false);

    public static CompiledProfile CreateStable(KeyChart source, ManiaOptions? options = null) => compile(source, options ?? new ManiaOptions(), true);

    private static CompiledProfile compile(KeyChart source, ManiaOptions options, bool stable)
    {
        ProfileSupport.RequireRange(options.OverallDifficulty, 0, 10, nameof(options.OverallDifficulty));
        ProfileSupport.RequireRange(options.DrainRate, 0, 10, nameof(options.DrainRate));
        ProfileSupport.RequireRange(options.ClockRate, 0.01, 100, nameof(options.ClockRate));
        ProfileSupport.RequireRange(options.DifficultyMultiplier, 0.01, 100, nameof(options.DifficultyMultiplier));
        var od = options.OverallDifficulty;
        var scale = options.ClockRate / options.DifficultyMultiplier;
        var bounds = stable && options.Converted
            ? new[] { 16.0, Math.Round(od) > 4 ? 34 : 47, Math.Round(od) > 4 ? 67 : 77, 97, 121, 158 }
            : [stable ? 16 : ProfileSupport.DifficultyRange(od, 22.4, 19.4, 13.9), 64 - 3 * od, 97 - 3 * od, 127 - 3 * od, 151 - 3 * od, 188 - 3 * od];
        bounds = bounds.Select(w => Math.Floor(w * scale) + 0.5).ToArray();
        var windows = ProfileSupport.Symmetric((ManiaResults.PERFECT, bounds[0]), (ManiaResults.GREAT, bounds[1]), (ManiaResults.GOOD, bounds[2]),
            (ManiaResults.OK, bounds[3]), (ManiaResults.MEH, bounds[4]), (ManiaResults.MISS, bounds[5]));
        var chart = compileChart(source, windows, bounds, stable);
        var score = stable ? stableScore(chart.ScorePlan) : lazerScore(chart.ScorePlan);
        var health = createHealth(source, options.DrainRate, stable, ManiaHealthCalibration.ComputeRecoveryMultiplier(source, options.DrainRate, options.DrainStartTime, !stable));
        var id = stable ? "osu-stable-mania-score-v1" : "osu-lazer-mania-standardised";
        var descriptor = new ProfileDescriptor(id, stable ? "osu-stable-3adc2d001aac" : "9a10d935b05a3b896f4bd811d620f2023331db93",
            stable ? EvidenceLevel.StaticBinary : EvidenceLevel.PublicSource,
            stable
                ?
                [
                    "https://m1.ppy.sh/r/osu!install.exe", "https://github.com/ppy/osu-wiki/blob/master/wiki/Gameplay/Judgement/osu!mania/en.md",
                    "https://github.com/ppy/osu-wiki/blob/master/wiki/Gameplay/Score/ScoreV1/osu!mania/en.md"
                ]
                : ["https://github.com/ppy/osu/tree/9a10d935b05a3b896f4bd811d620f2023331db93/osu.Game.Rulesets.Mania"],
            [
                stable
                    ? "Pins original CIL hold resolution and score/Bonus/Combo/HP arithmetic, with static manager/calibration comparisons. Binary-equivalent input uses integer millisecond chart times and the stable song clock; complete frame/input routing remains outside the CIL fixture."
                    : "Normal gameplay; mod-specific score multipliers and pre-v8 beatmap break drain are not inferred from difficulty values.",
            ]);
        return new CompiledProfile(chart, new SessionConfiguration(id, score, health, context =>
                !stable && context.State.Held == context.State.PreviouslyHeld ? [] : ProfileSupport.Oldest(context),
            context => context.Time >= source.EndTime && context.NotesRetired), descriptor);
    }

    // Release compilation-only lane/rule lookups before creating runtime delegates.
    private static Chart compileChart(KeyChart source, TimingWindows windows, double[] bounds, bool stable)
    {
        if (stable && source.Notes.Any(n => n.Kind == KeyNoteKind.Hold && n.EndTime <= n.StartTime))
            throw new ArgumentException("Mania holds require a positive duration.", nameof(source));

        var laneStarts = stable
            ? null
            : source.Notes.GroupBy(n => n.Lane)
                .ToDictionary(g => g.Key, g => g.Select(n => n.StartTime).Distinct().Order().ToImmutableArray());
        ImmutableArray<long> stableWindows = stable ? [.. bounds.Select(b => Time.FromMilliseconds(b - 0.5))] : [];
        var tail = stable ? null : new TimingWindows(windows.Entries.Select(w => w with { Early = w.Early * 3 / 2, Late = w.Late * 3 / 2 }));
        var rules = new Dictionary<KeyNoteKind, INoteRule>();
        return source.Compile(note =>
        {
            if (!rules.TryGetValue(note.Kind, out var rule))
                rules.Add(note.Kind, rule = createRule(note.Kind, windows, tail, stableWindows, stable));
            return stable
                ? rule
                : new OrderedNoteRule(rule,
                    laneStarts![note.Lane], laneStarts[note.Lane].BinarySearch(note.StartTime) + 1, windows);
        }, note => scoringOpportunities(note, stable));
    }

    private static INoteRule createRule(KeyNoteKind kind, TimingWindows windows, TimingWindows? tail, ImmutableArray<long> stableWindows, bool stable)
    {
        var late = windows.Entries[stable ? 3 : 4].Late;

        if (kind == KeyNoteKind.Tap)
        {
            return new TapRule(new TapConfiguration(windows, stable ? stableWindows[3] - 1 : late, ManiaResults.MISS,
                BlockResolvedUntilStart: stable));
        }

        if (kind != KeyNoteKind.Hold)
            throw ProfileSupport.Unsupported(stable ? "osu!stable mania" : "osu!lazer mania", kind);

        if (stable)
            return new StableManiaHoldRule(stableWindows);

        var config = new HoldConfiguration(windows, tail!, late, late * 3 / 2, ManiaResults.MISS, ManiaResults.PERFECT, ManiaResults.MISS,
            HoldEndMode.Release, HoldDropMode.Recover,
            evaluation => new NoteEmission(NoteParts.TAIL, resolveGrade(evaluation), evaluation.Note.EndTime, evaluation.TailOffset))
        {
            RecoverMissedHead = true,
            LatestRecoveryOffset = late,
            BreakResult = ManiaResults.COMBO_BREAK,
        };
        return new HoldRule(config);

        ResultId resolveGrade(HoldEvaluation evaluation)
        {
            if (evaluation.Resolution is HoldResolution.Timeout or HoldResolution.MissedHead or HoldResolution.Drop)
                return ManiaResults.MISS;
            if (evaluation.TailResult == ManiaResults.MISS)
                return ManiaResults.MISS;
            if (evaluation.Broken || evaluation.HeadResult == ManiaResults.MISS)
                return ManiaResults.MEH;

            return evaluation.TailResult;
        }
    }

    private static ImmutableArray<ScoringOpportunity> scoringOpportunities(KeyNote note, bool stable) => note.Kind switch
    {
        KeyNoteKind.Tap => [new ScoringOpportunity(note.Id, NoteParts.TAP, stable ? 300 : 305)],
        KeyNoteKind.Hold when stable => [new ScoringOpportunity(note.Id, NoteParts.TAIL, 300)],
        KeyNoteKind.Hold => [new ScoringOpportunity(note.Id, NoteParts.HEAD, 305), new ScoringOpportunity(note.Id, NoteParts.TAIL, 305)],
        _ => [],
    };

    private static ScoreConfiguration stableScore(ScorePlan plan)
    {
        return new ScoreConfiguration(new ScoreState { Bonus = 100 }, (state, context) =>
        {
            var nativeCombo = state.Combo & ushort.MaxValue;
            var resetBonus = nativeCombo != 0 && nativeCombo % 384 == 0;

            if (context.Event.Result == ManiaResults.HOLD_TICK)
            {
                return state with
                {
                    Combo = state.Combo + 1, MaximumCombo = Math.Max(state.MaximumCombo, state.Combo + 1),
                    Bonus = resetBonus ? 100 : state.Bonus,
                    ResultCounts = state.ResultCounts.SetItem(ManiaResults.HOLD_TICK, state.ResultCounts.GetValueOrDefault(ManiaResults.HOLD_TICK) + 1),
                };
            }

            if (context.Event.Result == ManiaResults.HOLD_BREAK)
                return state with
                {
                    Combo = 0, Bonus = resetBonus ? 100 : state.Bonus,
                    ResultCounts = state.ResultCounts.SetItem(ManiaResults.HOLD_BREAK, state.ResultCounts.GetValueOrDefault(ManiaResults.HOLD_BREAK) + 1),
                };
            if (context.Opportunity == null)
                return state;

            var result = context.Event.Result;
            var bonus = result switch
            {
                var id when id == ManiaResults.PERFECT => Math.Min(100, state.Bonus + 2),
                var id when id == ManiaResults.GREAT => Math.Min(100, state.Bonus + 1),
                var id when id == ManiaResults.GOOD => Math.Max(0, state.Bonus - 8),
                var id when id == ManiaResults.OK => Math.Max(0, state.Bonus - 24),
                var id when id == ManiaResults.MEH => Math.Max(0, state.Bonus - 44),
                _ => 0,
            };
            if (resetBonus)
                bonus = 100;
            var bonusValue = result switch
            {
                var id when id == ManiaResults.PERFECT || id == ManiaResults.GREAT => 32,
                var id when id == ManiaResults.GOOD => 16,
                var id when id == ManiaResults.OK => 8,
                var id when id == ManiaResults.MEH => 4,
                _ => 0,
            };
            var value = stable_values[result].Score;
            var next = ProfileSupport.Accumulate(state, context, stable_values);
            var delta = 500000.0 / plan.Opportunities.Length * (value + bonusValue * Math.Sqrt(bonus)) / 320;
            var unrounded = state.Counters.GetValueOrDefault(ManiaCounters.SCORE_V1_UNROUNDED) + delta;
            // Stable displays truncated hit increments, then recomputes its rounded total when play finishes.
            var displayed = next.AccuracyMaximum == plan.MaximumAccuracy ? Math.Round(unrounded)
                : result == ManiaResults.MISS ? state.Score : (int)state.Counters.GetValueOrDefault(ManiaCounters.SCORE_V1_UNROUNDED) + (int)delta;
            if (next.AccuracyMaximum == plan.MaximumAccuracy && displayed == 999999)
                displayed = 1000000;
            return next with { Score = displayed, Bonus = bonus, Counters = state.Counters.SetItem(ManiaCounters.SCORE_V1_UNROUNDED, unrounded) };
        }, p => p.Opportunities.IsEmpty ? 0 : 1000000);
    }

    private static ScoreConfiguration lazerScore(ScorePlan plan)
    {
        var maximumCombo = Enumerable.Range(1, plan.Opportunities.Length).Sum(combo => 300 * comboFactor(combo));
        return new ScoreConfiguration(new ScoreState(), (state, context) =>
        {
            if (context.Event.Result == ManiaResults.COMBO_BREAK)
                return state with { Combo = 0 };

            if (context.Opportunity == null)
                return state;

            var next = ProfileSupport.Accumulate(state, context, lazer_values);
            var baseCombo = context.Event.Result == ManiaResults.PERFECT ? 300 : lazer_values[context.Event.Result].Score;
            var comboPortion = state.Counters.GetValueOrDefault(ManiaCounters.COMBO_PORTION) + baseCombo * comboFactor(next.Combo);
            var progress = next.AccuracyMaximum / plan.MaximumAccuracy;
            return next with
            {
                Score = Math.Round(150000 * comboPortion / maximumCombo + 850000 * Math.Pow(next.Accuracy, 2 + 2 * next.Accuracy) * progress),
                Counters = state.Counters.SetItem(ManiaCounters.COMBO_PORTION, comboPortion),
            };
        }, p => p.Opportunities.IsEmpty ? 0 : 1000000);
    }

    private static double comboFactor(int combo) => Math.Min(Math.Max(0.5, Math.Log(Math.Max(1, combo), 4)), Math.Log(400, 4));

    private static HealthConfiguration createHealth(KeyChart source, double drain, bool stable, double recoveryMultiplier)
    {
        var holds = source.Notes.Where(n => n.Kind == KeyNoteKind.Hold).Select(n => n.Id).ToImmutableHashSet();
        return HealthRules.LatchedGauge(0, 1, 1, context =>
        {
            if (stable && context.Event.Part == NoteParts.HEAD)
                return 0;

            var endpoint = !stable && holds.Contains(context.Event.NoteId);
            return context.Event.Result switch
            {
                var id when id == ManiaResults.COMBO_BREAK => 0,
                var id when id == ManiaResults.HOLD_BREAK => 0,
                var id when id == ManiaResults.HOLD_TICK => (0.5 - drain * 0.05) / 200,
                var id when id == ManiaResults.MISS => -(drain + 1) * (endpoint ? 0.00375 : 0.0075),
                var id when id == ManiaResults.MEH => -(drain + 1) * 0.0016,
                var id when id == ManiaResults.OK => 0,
                var id when id == ManiaResults.GOOD => recoveryMultiplier * (0.004 - drain * 0.0004),
                var id when id == ManiaResults.GREAT => recoveryMultiplier * (0.005 - drain * 0.0005),
                var id when id == ManiaResults.PERFECT => recoveryMultiplier * (0.0055 - drain * 0.0005),
                _ => throw new InvalidOperationException($"Undefined mania health result {context.Event.Result}."),
            };
        }, value => value <= 0, value => value > 0);
    }
}
