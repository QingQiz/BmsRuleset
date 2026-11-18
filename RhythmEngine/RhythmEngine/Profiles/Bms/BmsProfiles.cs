using System.Collections.Immutable;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using RhythmEngine.Profiles.Patterns;

namespace RhythmEngine.Profiles.Bms;

public static partial class BmsProfiles
{
    private const int empty_poor_priority = 2;

    private static readonly ImmutableDictionary<ResultId, ResultValue> values = ProfileSupport.Values(
        (BmsResults.PGREAT, 2, 2, ComboChange.Increment), (BmsResults.GREAT, 1, 1, ComboChange.Increment),
        (BmsResults.GOOD, 0, 0, ComboChange.Increment), (BmsResults.BAD, 0, 0, ComboChange.Reset), (BmsResults.POOR, 0, 0, ComboChange.Reset));

    private static readonly ImmutableHashSet<ResultId> empty_poor_results = [BmsResults.EMPTY_POOR];
    private static readonly ImmutableHashSet<ResultId> non_consuming_results = [BmsResults.EMPTY_POOR, BmsResults.BAD];
    private static readonly ImmutableHashSet<ResultId> bad_results = [BmsResults.BAD];
    private static readonly ImmutableHashSet<ResultId> recovery_results = [BmsResults.PGREAT, BmsResults.GREAT, BmsResults.GOOD];

    public static CompiledProfile CreateBeatoraja(KeyChart source, BmsOptions? options = null) => compile(source, options ?? new BmsOptions(), false);

    public static CompiledProfile CreateLr2(KeyChart source, BmsOptions? options = null) => compile(source, options ?? new BmsOptions(), true);

    private static CompiledProfile compile(KeyChart source, BmsOptions options, bool lr2)
    {
        ProfileSupport.RequireRange(options.Total, double.Epsilon, double.MaxValue, nameof(options.Total));
        if (!Enum.IsDefined(options.Rank) || !Enum.IsDefined(options.Gauge) || !Enum.IsDefined(options.Selection))
            throw new ArgumentOutOfRangeException(nameof(options));

        if (lr2 && options.Rank == BmsRank.VeryEasy)
            throw new NotSupportedException("The LR2 reference has no VeryEasy timing table.");

        if (options.Selection == BmsSelection.Default)
            options = options with { Selection = lr2 ? BmsSelection.Oldest : BmsSelection.Combo };

        var chart = compileChart(source, options.Rank, lr2);
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
                    "https://github.com/exch-bms2/beatoraja/blob/9cddf911113e74da87458344bb85a4ea45431d0b/src/bms/player/beatoraja/play/GaugeProperty.java",
                ]
                : ["https://github.com/exch-bms2/beatoraja/tree/9cddf911113e74da87458344bb85a4ea45431d0b/src/bms/player/beatoraja/play"],
            lr2
                ? ["LR2 is closed source; this profile is a versioned compatibility configuration, not binary-verified parity."]
                : ["Seven-key judgement and gauge rules; five-key and PMS tables are separate modes and are not selected implicitly."]);

        return new CompiledProfile(chart, new SessionConfiguration(id, score, health,
            context => SelectCandidates(context, chart, options.Selection))
        {
            KeySounds = lr2 ? KeySoundPolicies.Lr2 : KeySoundPolicies.Beatoraja(chart),
        }, descriptor);
    }

    // Compilation lookups must not be retained by the runtime arbitration closure.
    private static Chart compileChart(KeyChart source, BmsRank rank, bool lr2)
    {
        var rules = new Dictionary<(KeyNoteKind Kind, bool Directional), INoteRule>();
        return source.Compile(note =>
        {
            var key = (note.Kind, note.Directional);
            if (!rules.TryGetValue(key, out var rule))
                rules.Add(key, rule = createRule(note, rank, lr2));
            return rule;
        }, note => CreateScoringOpportunities(note.Id, note.Kind));
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

            var boundary = selection == BmsSelection.Combo ? BmsResults.GOOD : BmsResults.GREAT;
            var currentWindow = headWindows(current).Entries.FirstOrDefault(w => w.Result == boundary);
            var candidateWindow = headWindows(candidate).Entries.FirstOrDefault(w => w.Result == boundary);
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
        if (lr2 && note.Kind is KeyNoteKind.ReleaseHold or KeyNoteKind.SustainHold)
            throw ProfileSupport.Unsupported("LR2", note.Kind);

        var windows = note.Kind == KeyNoteKind.Mine ? null : createWindows(rank, note.Directional, false, lr2);
        var tailWindows = note.Kind is KeyNoteKind.Hold or KeyNoteKind.ReleaseHold or KeyNoteKind.SustainHold
            ? createWindows(rank, note.Directional, true, lr2)
            : null;
        return CreateRule(note, windows, tailWindows, new BmsRuleOptions(
            RetainResolvedHeads: !lr2, ImmediateBadHead: lr2, ShareLateBad: lr2));
    }

    public static INoteRule CreateRule(KeyNote note, TimingWindows? windows, TimingWindows? tailWindows, BmsRuleOptions options)
    {
        if (note.Kind == KeyNoteKind.Mine)
            return new MineRule(0, 0, BmsResults.MINE_HIT, BmsResults.MINE_AVOIDED, options.MineOrder, acceptsInput: false);

        ArgumentNullException.ThrowIfNull(windows);
        var empty = windows.Entries.FirstOrDefault(w => w.Result == BmsResults.EMPTY_POOR);
        var headDeadline = options.HeadDeadline ?? windows.Entries.Single(w => w.Result == BmsResults.BAD).Late;
        var ghosts = options.RetainResolvedHeads && !options.NonConsumingBad && empty.Result != null ? new TimingWindows([empty]) : null;
        var nonResolving = options.NonConsumingBad ? non_consuming_results : empty_poor_results;

        if (note.Kind == KeyNoteKind.Tap)
        {
            return new TapRule(new TapConfiguration(windows, headDeadline, BmsResults.POOR,
                NonResolvingResults: nonResolving, ResolvedWindows: ghosts, CandidatePriority: candidatePriority,
                ShareInput: options.ShareLateBad ? (result, offset) => result == BmsResults.BAD && offset > 0 : null,
                SuppressRepeatedMistakes: options.NonConsumingBad, RecoveringResults: recovery_results, PassiveOrder: options.PassiveOrder));
        }

        if (note.Kind is not (KeyNoteKind.Hold or KeyNoteKind.ReleaseHold or KeyNoteKind.SustainHold))
            throw ProfileSupport.Unsupported("BMS", note.Kind);

        ArgumentNullException.ThrowIfNull(tailWindows);
        var aggregate = note.Kind == KeyNoteKind.Hold;
        var config = new HoldConfiguration(windows, tailWindows, headDeadline, options.TailDeadline ?? tailWindows.Entries[^1].Late,
            BmsResults.POOR, BmsResults.PGREAT, aggregate ? BmsResults.BAD : BmsResults.POOR,
            aggregate ? HoldEndMode.ReleaseOrAutomatic : HoldEndMode.Release, HoldDropMode.Resolve,
            evaluation =>
            {
                var result = evaluation.TailResult;
                var offset = evaluation.TailOffset;

                if (evaluation.Resolution is HoldResolution.Timeout or HoldResolution.MissedHead)
                    result = BmsResults.POOR;
                else if (aggregate)
                {
                    result = evaluation.Resolution is HoldResolution.Automatic or HoldResolution.Head ? evaluation.HeadResult
                        : evaluation.Resolution == HoldResolution.Drop ? BmsResults.BAD
                        : windows.RankOf(evaluation.HeadResult) >= tailWindows.RankOf(evaluation.TailResult) ? evaluation.HeadResult : evaluation.TailResult;
                    if (evaluation.Resolution == HoldResolution.Automatic || Math.Abs(evaluation.HeadOffset ?? 0) > Math.Abs(offset ?? 0))
                        offset = evaluation.HeadOffset;
                }

                return new NoteEmission(NoteParts.TAIL, result, aggregate ? evaluation.Note.StartTime : evaluation.Note.EndTime, offset);
            })
        {
            NonResolvingHeadResults = nonResolving,
            TerminalHeadResults = options.ImmediateBadHead ? bad_results : ImmutableHashSet<ResultId>.Empty,
            TailOnOppositeControl = note.Directional && !aggregate,
            ReleaseByOwner = note.Directional,
            CandidatePriority = candidatePriority,
            ResolvedHeadWindows = ghosts,
            ReleaseGrace = options.ReleaseGrace,
            DeferredReleaseResults = bad_results,
            SuppressRepeatedMistakes = options.NonConsumingBad,
            SuppressMissedHeadPenalty = aggregate,
            RecoveringHeadResults = recovery_results,
            AutomaticEndOffset = 1,
            AutomaticTailUsesHeadOffset = aggregate,
            PassiveOrder = options.PassiveOrder,
            Balance = note.Kind == KeyNoteKind.SustainHold
                ? new HoldBalance(Time.FromMilliseconds(200), BmsResults.SUSTAIN_GAIN, BmsResults.SUSTAIN_LOSS, recovery_results)
                : null,
        };
        return new HoldRule(config);
    }

    public static ImmutableArray<ScoringOpportunity> CreateScoringOpportunities(int noteId, KeyNoteKind kind) => kind switch
    {
        KeyNoteKind.Mine => [],
        KeyNoteKind.Tap => [new ScoringOpportunity(noteId, NoteParts.TAP, 2)],
        KeyNoteKind.Hold => [new ScoringOpportunity(noteId, NoteParts.TAIL, 2)],
        KeyNoteKind.ReleaseHold or KeyNoteKind.SustainHold =>
            [new ScoringOpportunity(noteId, NoteParts.HEAD, 2), new ScoringOpportunity(noteId, NoteParts.TAIL, 2)],
        _ => throw ProfileSupport.Unsupported("BMS scoring", kind),
    };

    private static int candidatePriority(ResultId result) => result == BmsResults.EMPTY_POOR ? empty_poor_priority : 1;

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
            var regular = new[] { (BmsResults.PGREAT, bounds[0]), (BmsResults.GREAT, bounds[1]), (BmsResults.GOOD, bounds[2]), (BmsResults.BAD, bounds[3]) };
            var lr2Entries = regular.Select(w => new TimingWindow(w.Item1, -Time.FromMilliseconds(w.Item2), Time.FromMilliseconds(w.Item2) - 1)).ToList();
            if (!tail)
                lr2Entries.Add(new TimingWindow(BmsResults.EMPTY_POOR, -Time.FromMilliseconds(bounds[4]) + 1, -Time.FromMilliseconds(bounds[3]) - 1));

            return new TimingWindows(lr2Entries);
        }

        var rules = BmsJudgementRules.For(BmsJudgementMode.SevenKey);
        return rules.GetProfile(rules.RateForRank((int)rank)).GetTiming(directional, tail).Windows;
    }

    private static HealthConfiguration createHealth(KeyChart source, ScorePlan plan, BmsOptions options, bool lr2)
    {
        var count = plan.Opportunities.Length;
        var recovery = count == 0 ? 0 : options.Total / count;
        var profile = BmsGaugeProfiles.Create(options.Gauge, lr2 ? BmsGaugeProfileFamily.Lr2 : BmsGaugeProfileFamily.SevenKeys);
        var normal = profile.Algorithm == BmsGaugeAlgorithm.Total;
        var deltas = new[] { profile.PerfectGain, profile.GreatGain, profile.GoodGain, profile.BadDelta, profile.PoorDelta, profile.EmptyPoorDelta }
            .Select(value => normal && value > 0 ? value : value * 100).ToArray();

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
        var damageMultiplier = lr2 && options.Gauge is BmsGauge.Hard or BmsGauge.ExHard ? BmsGaugeCalculator.CalculateDamageScale(options.Total, count) : 1;
        return HealthRules.LatchedGauge(profile.MinHealth * 100, profile.MaxHealth * 100, profile.InitialHealth * 100, context =>
        {
            if (context.Health.HasFailed)
                return 0;

            var result = context.Event.Result;

            if (result == BmsResults.MINE_HIT)
            {
                return lr2
                    ? -mineDamage[context.Event.NoteId] * 0.5
                    : (float)context.Health.Value - (float)mineDamage[context.Event.NoteId] - context.Health.Value;
            }

            if (result == BmsResults.MINE_AVOIDED || (context.Event.Part != NoteParts.EMPTY && context.Event.Part != NoteParts.BODY
                                                                                            && !plan.OpportunitiesByPart.ContainsKey((context.Event.NoteId, context.Event.Part))))
                return 0;

            var index = result switch
            {
                var id when id == BmsResults.PGREAT => 0,
                var id when id == BmsResults.GREAT => 1,
                var id when id == BmsResults.GOOD => 2,
                var id when id == BmsResults.BAD => 3,
                var id when id == BmsResults.POOR => 4,
                var id when id == BmsResults.EMPTY_POOR => 5,
                var id when id == BmsResults.SUSTAIN_GAIN => 1,
                var id when id == BmsResults.SUSTAIN_LOSS => 3,
                _ => throw new InvalidOperationException($"Undefined gauge result {result}."),
            };
            var delta = deltas[index];
            if (!lr2)
                delta = (float)delta * (context.Event.Part == NoteParts.BODY ? 0.5f : 1);
            else if (delta < 0)
                delta *= damageMultiplier;
            if (lr2 && delta > 0)
                delta *= normal ? recovery : 1;

            if (delta < 0)
            {
                foreach (var rule in profile.GutsRules)
                {
                    if (context.Health.Value >= rule.HealthThreshold * 100)
                        continue;

                    delta = lr2 ? delta * rule.DamageMultiplier : (float)delta * (float)rule.DamageMultiplier;
                    break;
                }
            }

            return lr2
                ? context.Event.Part == NoteParts.BODY ? delta * 0.5 : delta
                : (float)context.Health.Value + (float)delta - context.Health.Value;
        }, value => !normal && value <= 0, value => value >= profile.ClearThreshold * 100 && value > 0);
    }
}
