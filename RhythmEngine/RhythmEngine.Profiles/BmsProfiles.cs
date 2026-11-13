using System.Collections.Immutable;
using RhythmEngine.Profiles.Patterns;

namespace RhythmEngine.Profiles;

public enum BmsRank
{
    VeryHard,
    Hard,
    Normal,
    Easy,
    VeryEasy,
}

public enum BmsGauge
{
    AssistEasy,
    Easy,
    Normal,
    Hard,
    ExHard,
    Hazard,
    Grade,
    GradeHard,
    GradeExHard,
}

public enum BmsSelection
{
    Default,
    Combo,
    Nearest,
    Oldest,
    Score,
}

public sealed record BmsOptions(
    BmsRank Rank = BmsRank.Normal,
    BmsGauge Gauge = BmsGauge.Normal,
    double Total = 260,
    BmsSelection Selection = BmsSelection.Default);

public static class BmsProfiles
{
    private const int empty_poor_priority = 2;

    private static readonly ImmutableDictionary<string, ResultValue> values = ProfileSupport.Values(
        ("pgreat", 2, 2, ComboChange.Increment), ("great", 1, 1, ComboChange.Increment),
        ("good", 0, 0, ComboChange.Increment), ("bad", 0, 0, ComboChange.Reset), ("poor", 0, 0, ComboChange.Reset));

    public static CompiledProfile CreateBeatoraja(KeyChart source, BmsOptions? options = null) => compile(source, options ?? new BmsOptions(), false);

    public static CompiledProfile CreateLr2(KeyChart source, BmsOptions? options = null) => compile(source, options ?? new BmsOptions(), true);

    private static CompiledProfile compile(KeyChart source, BmsOptions options, bool lr2)
    {
        ProfileSupport.RequireRange(options.Total, double.Epsilon, double.MaxValue, nameof(options.Total));
        if (!Enum.IsDefined(options.Rank) || !Enum.IsDefined(options.Gauge) || !Enum.IsDefined(options.Selection))
            throw new ArgumentOutOfRangeException(nameof(options));

        if (options.Selection == BmsSelection.Default)
            options = options with { Selection = lr2 ? BmsSelection.Oldest : BmsSelection.Combo };

        var chart = source.Compile(note => createRule(note, options.Rank, lr2));
        var score = new ScoreConfiguration(new ScoreState(), (state, context) => ProfileSupport.Accumulate(state, context, values),
            plan => plan.MaximumAccuracy);
        var health = createHealth(source, chart.ScorePlan, options, lr2);
        var id = lr2 ? "lr2-key-v1" : "beatoraja-7key-v1";
        var descriptor = new ProfileDescriptor(id, lr2 ? "f6286f316f68770e07ca76cd0672ce4f5c50ef8d" : "9cddf911113e74da87458344bb85a4ea45431d0b",
            lr2 ? EvidenceLevel.CommunityCompatibility : EvidenceLevel.PublicSource,
            lr2
                ?
                [
                    "https://github.com/yaasdf/lunaticvibes/tree/f6286f316f68770e07ca76cd0672ce4f5c50ef8d",
                    "https://github.com/exch-bms2/beatoraja/blob/9cddf911113e74da87458344bb85a4ea45431d0b/src/bms/player/beatoraja/play/GaugeProperty.java"
                ]
                : ["https://github.com/exch-bms2/beatoraja/tree/9cddf911113e74da87458344bb85a4ea45431d0b/src/bms/player/beatoraja/play"],
            lr2
                ? ["LR2 is closed source; this profile is a versioned compatibility configuration, not binary-verified parity."]
                : ["Seven-key judgement and gauge rules; five-key and PMS tables are separate modes and are not selected implicitly."]);

        return new CompiledProfile(chart, new SessionConfiguration(id, score, health,
            context =>
            {
                var candidates = context.Candidates;
                var activeHold = candidates.FirstOrDefault(c => c.Priority == 0 && !c.Shared);
                if (candidates.Any(c => c.Priority == 0 && !c.Shared))
                    return [activeHold, .. candidates.Where(c => c.Shared && chart.Notes[c.NoteIndex].Rule is HoldRule)];

                var selected = selectCandidate(candidates, options.Selection, chart);
                return selected is { } target ? [.. candidates.Where(c => c.Shared), target] : [..candidates.Where(c => c.Shared)];
            }), descriptor);
    }

    private static InputCandidate? selectCandidate(ImmutableArray<InputCandidate> candidates, BmsSelection selection, Chart chart)
    {
        InputCandidate? selected = null;

        foreach (var candidate in candidates.Where(c => !c.Shared).OrderBy(c => c.StartTime).ThenBy(c => c.NoteIndex))
        {
            if (candidate.Priority == 0)
                return candidate;

            if (selected is not { } current)
            {
                selected = candidate;
                continue;
            }

            // A playable note takes precedence over an empty POOR target, including a previously judged note.
            if (candidate.Priority == empty_poor_priority)
            {
                if (current.Priority == empty_poor_priority && Math.Abs(candidate.Offset) < Math.Abs(current.Offset))
                    selected = candidate;

                continue;
            }

            if (current.Priority == empty_poor_priority)
            {
                selected = candidate;
                continue;
            }

            if (selection == BmsSelection.Nearest)
            {
                if (Math.Abs(candidate.Offset) < Math.Abs(current.Offset))
                    selected = candidate;

                continue;
            }

            if (selection is not (BmsSelection.Combo or BmsSelection.Score))
                continue;

            var boundary = selection == BmsSelection.Combo ? 2 : 1;
            var currentWindow = headWindows(current).Entries[boundary];
            var candidateWindow = headWindows(candidate).Entries[boundary];
            // The original policy walks notes in time order and can replace one late BAD with another.
            if (current.Offset > currentWindow.Late && candidate.Offset >= candidateWindow.Early)
                selected = candidate;
        }

        return selected;

        TimingWindows headWindows(InputCandidate candidate)
        {
            var rule = chart.Notes[candidate.NoteIndex].Rule;
            return rule is TapRule tap ? tap.Configuration.Windows : ((HoldRule)rule).Configuration.HeadWindows;
        }
    }

    private static INoteRule createRule(KeyNote note, BmsRank rank, bool lr2)
    {
        var windows = createWindows(rank, note.Directional, false, lr2);
        var tailWindows = createWindows(rank, note.Directional, true, lr2);
        var empty = windows.Entries[^1];
        var bad = windows.Entries[^2];
        var ghosts = new TimingWindows([empty]);
        var priority = new Func<string, int>(result => result == "empty-poor" ? empty_poor_priority : 1);

        if (note.Kind == KeyNoteKind.Mine)
            return new MineRule(0, 0, "mine-hit", "mine-avoided", TimerOrder.AfterInputState);

        if (note.Kind == KeyNoteKind.Tap)
        {
            return new TapRule(new TapConfiguration(windows, bad.Late, "poor", 2,
                NonResolvingResults: ImmutableHashSet.Create("empty-poor"), ResolvedWindows: lr2 ? null : ghosts, CandidatePriority: priority,
                ShareInput: lr2 ? (result, offset) => result == "bad" && offset > 0 : null));
        }

        if (note.Kind is not (KeyNoteKind.Hold or KeyNoteKind.ReleaseHold or KeyNoteKind.SustainHold) || (lr2 && note.Kind != KeyNoteKind.Hold))
            throw ProfileSupport.Unsupported(lr2 ? "LR2" : "beatoraja", note.Kind);

        var aggregate = note.Kind == KeyNoteKind.Hold;
        var config = new HoldConfiguration(windows, tailWindows, bad.Late, tailWindows.Entries[^1].Late,
            "poor", "pgreat", aggregate ? "bad" : "poor", aggregate ? 0 : 2, 2,
            aggregate ? HoldEndMode.ReleaseOrAutomatic : HoldEndMode.Release, HoldDropMode.Resolve,
            evaluation =>
            {
                var result = evaluation.TailResult;
                var offset = evaluation.TailOffset;
                if (evaluation.Resolution is HoldResolution.Timeout or HoldResolution.MissedHead)
                    result = "poor";
                else if (aggregate)
                {
                    result = evaluation.Resolution is HoldResolution.Automatic or HoldResolution.Head ? evaluation.HeadResult
                        : evaluation.Resolution == HoldResolution.Drop ? "bad"
                        : windows.RankOf(evaluation.HeadResult) >= tailWindows.RankOf(evaluation.TailResult) ? evaluation.HeadResult : evaluation.TailResult;
                    if (evaluation.Resolution == HoldResolution.Automatic || Math.Abs(evaluation.HeadOffset ?? 0) > Math.Abs(offset ?? 0))
                        offset = evaluation.HeadOffset;
                }

                return new NoteEmission("tail", result, aggregate ? evaluation.Note.StartTime : evaluation.Note.EndTime, offset);
            })
        {
            NonResolvingHeadResults = ImmutableHashSet.Create("empty-poor"),
            TerminalHeadResults = lr2 ? ImmutableHashSet.Create("bad") : ImmutableHashSet<string>.Empty,
            TailOnOppositeControl = note.Directional && !aggregate,
            ReleaseByOwner = note.Directional,
            CandidatePriority = priority,
            ResolvedHeadWindows = lr2 ? null : ghosts,
            AutomaticEndOffset = 1,
            Balance = note.Kind == KeyNoteKind.SustainHold
                ? new HoldBalance(Time.FromMilliseconds(200), "sustain-gain", "sustain-loss", ImmutableHashSet.Create("pgreat", "great", "good"))
                : null,
        };
        return new HoldRule(config);
    }

    private static TimingWindows createWindows(BmsRank rank, bool directional, bool tail, bool lr2)
    {
        if (lr2)
        {
            var bounds = rank switch
            {
                BmsRank.VeryHard => new[] { 8.0, 27, 40, 200, 600 },
                BmsRank.Hard => [15.0, 32, 60, 200, 600],
                BmsRank.Normal => [18.0, 40, 96, 200, 650],
                BmsRank.Easy => [21.0, 60, 116, 200, 800],
                _ => throw new NotSupportedException("The LR2 reference has no VeryEasy timing table."),
            };
            var regular = new[] { ("pgreat", bounds[0]), ("great", bounds[1]), ("good", bounds[2]), ("bad", bounds[3]) };
            var lr2Entries = regular.Select(w => new TimingWindow(w.Item1, -Time.FromMilliseconds(w.Item2), Time.FromMilliseconds(w.Item2) - 1)).ToList();
            if (!tail)
                lr2Entries.Add(new TimingWindow("empty-poor", -Time.FromMilliseconds(bounds[4]) + 1, -Time.FromMilliseconds(bounds[3]) - 1));

            return new TimingWindows(lr2Entries);
        }

        var rate = ((int)rank + 1) * 0.25;
        var pgreat = (tail ? directional ? 130 : 120 : directional ? 30 : 20) * rate;
        var great = (tail ? directional ? 170 : 160 : directional ? 70 : 60) * rate;
        var good = (tail ? directional ? 210 : 200 : directional ? 160 : 150) * rate;
        var badEarly = (directional ? 230 : 220) * rate;
        var badLate = (directional ? 290 : 280) * rate;
        var entries = new List<TimingWindow>
        {
            new("pgreat", -Time.FromMilliseconds(pgreat), Time.FromMilliseconds(pgreat)),
            new("great", -Time.FromMilliseconds(great), Time.FromMilliseconds(great)),
            new("good", -Time.FromMilliseconds(good), Time.FromMilliseconds(good)),
            new("bad", -Time.FromMilliseconds(badEarly), Time.FromMilliseconds(badLate)),
        };
        if (!tail)
            entries.Add(new TimingWindow("empty-poor", -Time.FromMilliseconds(500), Time.FromMilliseconds(directional ? 160 : 150), IncludeEarly: false));

        return new TimingWindows(entries);
    }

    private static HealthConfiguration createHealth(KeyChart source, ScorePlan plan, BmsOptions options, bool lr2)
    {
        var count = plan.Opportunities.Length;
        var recovery = count == 0 ? 0 : options.Total / count;
        var normal = options.Gauge is BmsGauge.Normal or BmsGauge.Easy or BmsGauge.AssistEasy;
        var deltas = options.Gauge switch
        {
            BmsGauge.AssistEasy => lr2 ? [1.2, 1.2, 0.6, -3.2, -4.8, -1.6] : new[] { 1.0, 1, 0.5, -1.5, -3, -0.5 },
            BmsGauge.Easy => lr2 ? [1.2, 1.2, 0.6, -3.2, -4.8, -1.6] : [1.0, 1, 0.5, -1.5, -4.5, -1],
            BmsGauge.Normal => lr2 ? [1.0, 1, 0.5, -4, -6, -2] : [1.0, 1, 0.5, -3, -6, -2],
            BmsGauge.Hard => lr2 ? [0.1, 0.1, 0.05, -6, -10, -2] : [0.15, 0.12, 0.03, -5, -10, -5],
            BmsGauge.ExHard => lr2 ? [0.1, 0.1, 0.05, -12, -20, -2] : [0.15, 0.06, 0, -8, -16, -8],
            BmsGauge.Hazard => [0.15, 0.06, 0, -100, -100, -10],
            BmsGauge.Grade => lr2 ? [0.1, 0.1, 0.05, -2, -3, -2] : [0.15, 0.12, 0.06, -1.5, -3, -1.5],
            BmsGauge.GradeHard => lr2 ? [0.1, 0.1, 0.05, -6, -10, -2] : [0.15, 0.12, 0.03, -3, -6, -3],
            _ => lr2 ? [0.1, 0.1, 0.05, -12, -20, -2] : [0.15, 0.06, 0, -5, -10, -5],
        };
        if (!lr2)
        {
            // GrooveGauge freezes float coefficients after applying its chart-wide modifier.
            for (var i = 0; i < deltas.Length; i++)
            {
                var increment = (float)deltas[i];
                if (increment > 0)
                {
                    if (normal)
                        increment = count == 0 ? 0 : (float)(increment * options.Total / count);
                    else if (options.Gauge is BmsGauge.Hard or BmsGauge.ExHard)
                    {
                        var pgreat = (float)Math.Clamp(count == 0 ? 0 : (2 * options.Total - 320) / count, 0, 0.15f);
                        increment *= pgreat / 0.15f;
                    }
                }

                deltas[i] = increment;
            }
        }

        var mineDamage = source.Notes.Where(n => n.Kind == KeyNoteKind.Mine).ToImmutableDictionary(n => n.Id, n => n.MineDamage);
        var scoringParts = plan.Opportunities.Select(o => (o.NoteId, o.Part)).ToImmutableHashSet();
        var damageMultiplier = lr2 && options.Gauge is BmsGauge.Hard or BmsGauge.ExHard ? lr2DamageMultiplier(options.Total, count) : 1;
        return HealthRules.LatchedGauge(normal ? 2 : 0, 100, normal ? 20 : 100, context =>
        {
            if (context.Health.HasFailed)
                return 0;

            var result = context.Event.Result;
            if (result == "mine-hit")
                return lr2
                    ? -mineDamage[context.Event.NoteId] * 0.5
                    : (float)context.Health.Value - (float)mineDamage[context.Event.NoteId] - context.Health.Value;
            if (result == "mine-avoided" || (context.Event.Part != "empty" && context.Event.Part != "body"
                                                                           && !scoringParts.Contains((context.Event.NoteId, context.Event.Part))))
                return 0;

            var index = result switch
            {
                "pgreat" => 0, "great" => 1, "good" => 2, "bad" => 3, "poor" => 4,
                "empty-poor" => 5, "sustain-gain" => 1, "sustain-loss" => 3,
                _ => throw new InvalidOperationException($"Undefined gauge result {result}."),
            };
            var delta = deltas[index];
            if (!lr2)
                delta = (float)delta * (context.Event.Part == "body" ? 0.5f : 1);
            else if (delta < 0)
                delta *= damageMultiplier;
            if (lr2 && delta > 0)
                delta *= normal ? recovery : 1;

            if (delta < 0 && (options.Gauge is BmsGauge.Hard or BmsGauge.Grade || (lr2 && options.Gauge == BmsGauge.GradeHard)))
            {
                var health = context.Health.Value;
                var guts = lr2 ? health < 30 ? 0.6 : 1
                    : options.Gauge == BmsGauge.Hard ? health < 10 ? 0.4 : health < 20 ? 0.5 : health < 30 ? 0.6 : health < 40 ? 0.7 : health < 50 ? 0.8 : 1
                    : health < 5 ? 0.4
                    : health < 10 ? 0.5
                    : health < 15 ? 0.6
                    : health < 20 ? 0.7
                    : health < 25 ? 0.8 : 1;
                delta = lr2 ? delta * guts : (float)delta * (float)guts;
            }

            return lr2
                ? context.Event.Part == "body" ? delta * 0.5 : delta
                : (float)context.Health.Value + (float)delta - context.Health.Value;
        }, value => !normal && value <= 0, value => normal ? value >= (options.Gauge == BmsGauge.AssistEasy ? 60 : 80) : value > 0);
    }

    private static double lr2DamageMultiplier(double total, int noteCount)
    {
        double[] thresholds = [240, 230, 210, 200, 180, 160, 150, 130, 120, 0];
        double[] multipliers = [1, 1.11, 1.25, 1.5, 1.666, 2, 2.5, 3.333, 5, 10];
        var index = Array.FindIndex(thresholds, threshold => total >= threshold);
        var density = 1.0;
        var coefficient = 0.002;

        for (var notes = 1000; notes > noteCount || notes > 1; notes /= 2)
        {
            density += coefficient * (notes - Math.Max(noteCount, notes / 2));
            coefficient *= 2;
        }

        return Math.Max(multipliers[index], density);
    }
}
