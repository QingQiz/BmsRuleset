using System.Collections.Immutable;
using RhythmEngine.Profiles.Patterns;

namespace RhythmEngine.Profiles;

public sealed record ManiaOptions(
    double OverallDifficulty = 8,
    double DrainRate = 5,
    double ClockRate = 1,
    double DifficultyMultiplier = 1,
    bool Converted = false,
    long DrainStartTime = 0);

public static class ManiaProfiles
{
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
        var windows = ProfileSupport.Symmetric(("perfect", bounds[0]), ("great", bounds[1]), ("good", bounds[2]),
            ("ok", bounds[3]), ("meh", bounds[4]), ("miss", bounds[5]));
        var laneStarts = source.Notes.GroupBy(n => n.Lane).ToDictionary(g => g.Key, g => g.Select(n => n.StartTime).Distinct().Order().ToImmutableArray());
        var chart = source.Compile(note => stable
            ? createRule(note, windows, bounds, true)
            : new OrderedNoteRule(createRule(note, windows, bounds, false),
                laneStarts[note.Lane], laneStarts[note.Lane].BinarySearch(note.StartTime) + 1, windows));
        var score = stable ? stableScore(chart.ScorePlan) : lazerScore(chart.ScorePlan);
        var health = createHealth(source, options.DrainRate, stable, ManiaHealthCalibration.ComputeRecoveryMultiplier(source, options.DrainRate, options.DrainStartTime, !stable));
        var id = stable ? "osu-stable-mania-score-v1" : "osu-lazer-mania-standardised";
        var descriptor = new ProfileDescriptor(id, stable ? "osu-stable-3adc2d001aac" : "9a10d935b05a3b896f4bd811d620f2023331db93",
            stable ? EvidenceLevel.StaticBinary : EvidenceLevel.PublicSource,
            stable
                ? ["https://m1.ppy.sh/r/osu!install.exe", "https://github.com/ppy/osu-wiki/blob/master/wiki/Gameplay/Judgement/osu!mania/en.md", "https://github.com/ppy/osu-wiki/blob/master/wiki/Gameplay/Score/ScoreV1/osu!mania/en.md"]
                : ["https://github.com/ppy/osu/tree/9a10d935b05a3b896f4bd811d620f2023331db93/osu.Game.Rulesets.Mania"],
            [
                stable
                    ? "Pins original CIL hold resolution and score/Bonus/Combo/HP arithmetic, with static manager/calibration comparisons. Binary-equivalent input uses integer millisecond chart times and the stable song clock; complete frame/input routing remains outside the CIL fixture."
                    : "Normal gameplay; mod-specific score multipliers and pre-v8 beatmap break drain are not inferred from difficulty values."
            ]);
        return new CompiledProfile(chart, new SessionConfiguration(id, score, health, context =>
            !stable && context.State.Held == context.State.PreviouslyHeld ? [] : ProfileSupport.Oldest(context)), descriptor);
    }

    private static INoteRule createRule(KeyNote note, TimingWindows windows, double[] bounds, bool stable)
    {
        var late = Time.FromMilliseconds(stable ? bounds[3] : bounds[4]);
        if (note.Kind == KeyNoteKind.Tap)
            return new TapRule(new TapConfiguration(windows, stable ? Time.FromMilliseconds(bounds[3] - 0.5) - 1 : late, "miss", stable ? 300 : 305,
                BlockResolvedUntilStart: stable));

        if (note.Kind != KeyNoteKind.Hold)
            throw ProfileSupport.Unsupported(stable ? "osu!stable mania" : "osu!lazer mania", note.Kind);

        if (stable)
        {
            if (note.EndTime <= note.StartTime)
                throw new ArgumentException("Mania holds require a positive duration.", nameof(note));

            return new StableManiaHoldRule([..bounds.Select(b => Time.FromMilliseconds(b - 0.5))]);
        }

        var tail = new TimingWindows(windows.Entries.Select(w => w with { Early = w.Early * 3 / 2, Late = w.Late * 3 / 2 }));
        var config = new HoldConfiguration(windows, tail, late, late * 3 / 2, "miss", "perfect", "miss",
            305, 305, HoldEndMode.Release, HoldDropMode.Recover,
            evaluation => new NoteEmission("tail", resolveGrade(evaluation), evaluation.Note.EndTime, evaluation.TailOffset))
        {
            RecoverMissedHead = true,
            LatestRecoveryOffset = late,
            BreakResult = "combo-break",
        };
        return new HoldRule(config);

        string resolveGrade(HoldEvaluation evaluation)
        {
            if (evaluation.Resolution is HoldResolution.Timeout or HoldResolution.MissedHead or HoldResolution.Drop)
                return "miss";
            if (evaluation.TailResult == "miss")
                return "miss";
            if (evaluation.Broken || evaluation.HeadResult == "miss")
                return "meh";

            return evaluation.TailResult;
        }
    }

    private static ScoreConfiguration stableScore(ScorePlan plan)
    {
        var values = ProfileSupport.Values(("perfect", 320, 300, ComboChange.Increment), ("great", 300, 300, ComboChange.Increment),
            ("good", 200, 200, ComboChange.Increment), ("ok", 100, 100, ComboChange.Increment),
            ("meh", 50, 50, ComboChange.Increment), ("miss", 0, 0, ComboChange.Reset));
        return new ScoreConfiguration(new ScoreState { Bonus = 100 }, (state, context) =>
        {
            var nativeCombo = state.Combo & ushort.MaxValue;
            var resetBonus = nativeCombo != 0 && nativeCombo % 384 == 0;
            if (context.Event.Result == "hold-tick")
                return state with
                {
                    Combo = state.Combo + 1, MaximumCombo = Math.Max(state.MaximumCombo, state.Combo + 1),
                    Bonus = resetBonus ? 100 : state.Bonus
                };
            if (context.Event.Result == "hold-break")
                return state with { Combo = 0, Bonus = resetBonus ? 100 : state.Bonus };
            if (context.Opportunity == null)
                return state;

            var result = context.Event.Result;
            var bonus = result switch
            {
                "perfect" => Math.Min(100, state.Bonus + 2), "great" => Math.Min(100, state.Bonus + 1),
                "good" => Math.Max(0, state.Bonus - 8), "ok" => Math.Max(0, state.Bonus - 24),
                "meh" => Math.Max(0, state.Bonus - 44), _ => 0,
            };
            if (resetBonus)
                bonus = 100;
            var bonusValue = result switch { "perfect" or "great" => 32, "good" => 16, "ok" => 8, "meh" => 4, _ => 0 };
            var value = values[result].Score;
            var next = ProfileSupport.Accumulate(state, context, values);
            var delta = 500000.0 / plan.Opportunities.Length * (value + bonusValue * Math.Sqrt(bonus)) / 320;
            var unrounded = state.Counters.GetValueOrDefault("score-v1-unrounded") + delta;
            // Stable displays truncated hit increments, then recomputes its rounded total when play finishes.
            var displayed = next.AccuracyMaximum == plan.MaximumAccuracy ? Math.Round(unrounded)
                : result == "miss" ? state.Score : (int)state.Counters.GetValueOrDefault("score-v1-unrounded") + (int)delta;
            if (next.AccuracyMaximum == plan.MaximumAccuracy && displayed == 999999)
                displayed = 1000000;
            return next with { Score = displayed, Bonus = bonus, Counters = state.Counters.SetItem("score-v1-unrounded", unrounded) };
        }, p => p.Opportunities.IsEmpty ? 0 : 1000000);
    }

    private static ScoreConfiguration lazerScore(ScorePlan plan)
    {
        var values = ProfileSupport.Values(("perfect", 305, 305, ComboChange.Increment), ("great", 300, 300, ComboChange.Increment),
            ("good", 200, 200, ComboChange.Increment), ("ok", 100, 100, ComboChange.Increment),
            ("meh", 50, 50, ComboChange.Increment), ("miss", 0, 0, ComboChange.Reset));
        var maximumCombo = Enumerable.Range(1, plan.Opportunities.Length).Sum(combo => 300 * comboFactor(combo));
        return new ScoreConfiguration(new ScoreState(), (state, context) =>
        {
            if (context.Event.Result == "combo-break")
                return state with { Combo = 0 };

            if (context.Opportunity == null)
                return state;

            var next = ProfileSupport.Accumulate(state, context, values);
            var baseCombo = context.Event.Result == "perfect" ? 300 : values[context.Event.Result].Score;
            var comboPortion = state.Counters.GetValueOrDefault("combo-portion") + baseCombo * comboFactor(next.Combo);
            var progress = next.AccuracyMaximum / plan.MaximumAccuracy;
            return next with
            {
                Score = Math.Round(150000 * comboPortion / maximumCombo + 850000 * Math.Pow(next.Accuracy, 2 + 2 * next.Accuracy) * progress),
                Counters = state.Counters.SetItem("combo-portion", comboPortion),
            };
        }, p => p.Opportunities.IsEmpty ? 0 : 1000000);
    }

    private static double comboFactor(int combo) => Math.Min(Math.Max(0.5, Math.Log(Math.Max(1, combo), 4)), Math.Log(400, 4));

    private static HealthConfiguration createHealth(KeyChart source, double drain, bool stable, double recoveryMultiplier)
    {
        var holds = source.Notes.Where(n => n.Kind == KeyNoteKind.Hold).Select(n => n.Id).ToImmutableHashSet();
        return HealthRules.LatchedGauge(0, 1, 1, context =>
        {
            if (stable && context.Event.Part == "head")
                return 0;

            var endpoint = !stable && holds.Contains(context.Event.NoteId);
            return context.Event.Result switch
            {
                "combo-break" => 0,
                "hold-break" => 0,
                "hold-tick" => (0.5 - drain * 0.05) / 200,
                "miss" => -(drain + 1) * (endpoint ? 0.00375 : 0.0075),
                "meh" => -(drain + 1) * 0.0016,
                "ok" => 0,
                "good" => recoveryMultiplier * (0.004 - drain * 0.0004),
                "great" => recoveryMultiplier * (0.005 - drain * 0.0005),
                "perfect" => recoveryMultiplier * (0.0055 - drain * 0.0005),
                _ => throw new InvalidOperationException($"Undefined mania health result {context.Event.Result}."),
            };
        }, value => value <= 0, value => value > 0);
    }
}
