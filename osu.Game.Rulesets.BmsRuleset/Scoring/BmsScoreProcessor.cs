using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring;

public partial class BmsScoreProcessor() : ScoreProcessor(new BmsRuleset())
{
    private const double accuracy_cutoff_x = 1;
    private const double accuracy_cutoff_s = 8.0 / 9.0;
    private const double accuracy_cutoff_a = 7.0 / 9.0;
    private const double accuracy_cutoff_b = 6.0 / 9.0;
    private const double accuracy_cutoff_c = 5.0 / 9.0;
    private const double accuracy_cutoff_d = 0;

    private static readonly Action<JudgementResult, int> set_combo_after = createComboAfterSetter();

    private static readonly Action<BmsScoreProcessor, HitEvent> add_hit_event = createHitEventAdder();

    private double latestEndTime = double.MaxValue;
    private readonly Dictionary<BmsLongNoteJudgementResult, IReadOnlyList<HitEvent>> additionalLongNoteEvents = new();

    public override void ApplyBeatmap(IBeatmap beatmap)
    {
        base.ApplyBeatmap(beatmap);

        if (beatmap.HitObjects.Count == 0)
        {
            latestEndTime = 0;
            return;
        }

        // Latest possible judgement time = max object end time + worst-case late window.
        var maxEndTime = beatmap.HitObjects.Max(h => h.GetEndTime());
        var lateWindow = beatmap.HitObjects[0].HitWindows?.WindowFor(HitResult.Ok)
                         ?? BmsHitWindows.FALLBACK_BAD_WINDOW;

        latestEndTime = maxEndTime + lateWindow;
    }

    public override int GetBaseScoreForResult(HitResult result) => result switch
    {
        HitResult.Perfect => 2,
        HitResult.Great => 1,
        _ => 0,
    };

    public override ScoreRank RankFromScore(double accuracy, IReadOnlyDictionary<HitResult, int> results)
    {
        return accuracy switch
        {
            // All PGREATs → rainbow S (DJ LEVEL MAX / perfect full combo).
            >= 1.0 - 1e-9 when results.GetValueOrDefault(HitResult.Great) == 0 &&
                               results.GetValueOrDefault(HitResult.Good) == 0 &&
                               results.GetValueOrDefault(HitResult.Ok) == 0 &&
                               results.GetValueOrDefault(HitResult.Meh) == 0
                => ScoreRank.X,
            // Traditional BMS DJ LEVEL thresholds expressed as EX-score ratios.
            // AAA = 8/9 of max ≈ 0.889, AA = 7/9 ≈ 0.778, A = 6/9 ≈ 0.667.
            // We expose S/A/B/C/D as approximate equivalents.
            >= accuracy_cutoff_s => ScoreRank.S,
            >= accuracy_cutoff_a => ScoreRank.A,
            >= accuracy_cutoff_b => ScoreRank.B,
            >= accuracy_cutoff_c => ScoreRank.C,
            _ => ScoreRank.D,
        };

        // BMS pass/fail is determined solely by gauge at song end, not by score accuracy.
        // ScoreRank.F is never assigned here; failure is communicated through BmsHealthProcessor.
    }

    public override double AccuracyCutoffFromRank(ScoreRank rank) => rank switch
    {
        ScoreRank.X or ScoreRank.XH => accuracy_cutoff_x,
        ScoreRank.S or ScoreRank.SH => accuracy_cutoff_s,
        ScoreRank.A => accuracy_cutoff_a,
        ScoreRank.B => accuracy_cutoff_b,
        ScoreRank.C => accuracy_cutoff_c,
        ScoreRank.D => accuracy_cutoff_d,
        _ => throw new ArgumentOutOfRangeException(nameof(rank), rank, null),
    };

    /// <summary>
    ///     Records an Empty POOR: a keypress that found no note to consume.
    ///     Increments the Empty POOR counter stored under
    ///     <see cref="HitResult.Miss"/> in the score statistics so it
    ///     appears in the results-screen statistics and the live HUD judgement counter.
    ///     Empty POORs do not affect EX-score, accuracy, or combo.
    /// </summary>
    public void RegisterEmptyPoor()
    {
        ScoreResultCounts[HitResult.Miss] = ScoreResultCounts.GetValueOrDefault(HitResult.Miss) + 1;
    }

    public void RegisterEmptyPoor(double eventTime)
    {
        RegisterEmptyPoor();
        add_hit_event(this, new HitEvent(0, 1, HitResult.Miss, new HitObject { StartTime = eventTime }, null, null));
    }

    /// <summary>
    ///     Applies a separate CN/HCN endpoint without requiring the source drawable to complete.
    /// </summary>
    public BmsLongNoteJudgementResult ApplySyntheticLongNoteEndpoint(BmsLongNoteEndpointResult endpointResult)
    {
        var endpoint = endpointResult.Source.CreateSyntheticEndpoint(endpointResult.ExpectedTime);
        var result = new BmsLongNoteJudgementResult(endpoint, endpoint.CreateJudgement(), [endpointResult])
        {
            Type = endpointResult.Result,
        };
        ApplyResult(result);
        return result;
    }

    public override void PopulateScore(ScoreInfo score)
    {
        base.PopulateScore(score);

        score.HitEvents = score.HitEvents
            .Concat(additionalLongNoteEvents.Values.SelectMany(events => events))
            .OrderBy(e => e.HitObject.GetEndTime() + e.TimeOffset)
            .ToList();

        // Attribution (e.g. which gauge an Auto Gauge run resolved to) is owned by the mods
        // that introduce the behaviour, so the score processor stays free of gauge-specific logic.
        foreach (var mod in Mods.Value.OfType<IApplicableToScorePopulation>())
            mod.ApplyToScore(score);

        BmsModPaused.ApplyToScore(score);
    }

    protected override void Update()
    {
        // Don't call base — JudgementProcessor.Update() checks JudgedHits == MaxHits,
        // which never becomes true when mines expire without a result.  Replace with a
        // time-based check: play is complete when the last object's late window has passed.
        // This must also clear completion after a rewind because looping players wait for
        // that transition before they stop seeking back to the start of the beatmap.
        if (HasCompleted is BindableBool bb)
            bb.Value = Time.Current >= latestEndTime;
    }

    protected override void Reset(bool storeResults)
    {
        base.Reset(storeResults);
        additionalLongNoteEvents.Clear();
    }

    /// <summary>
    ///     Scaled EX-score.  Accuracy = EXScore / MaxEXScore (0–1).
    ///     Total score = accuracy × 1 000 000; bonus portion carries over from base.
    /// </summary>
    protected override double ComputeTotalScore(double comboProgress, double accuracyProgress, double bonusPortion)
        => 1_000_000 * Accuracy.Value * accuracyProgress;

    // EX-score has no combo multiplier — every PGREAT is always worth exactly 2.
    protected override double GetComboScoreChange(JudgementResult result) => 0;

    /// <summary>
    ///     BMS BAD (Ok) and POOR (Meh) must break combo, but osu!'s framework considers them
    ///     "hit" results (<c>HitResult.IsHit()</c> = <c>true</c>) so <c>IncreasesCombo()</c>
    ///     fires instead of <c>BreaksCombo()</c> in the sealed <c>ApplyResultInternal</c>.
    ///     We force-reset both <c>Combo.Value</c> and the already-stamped
    ///     <c>ComboAfterJudgement</c> (via reflection) so that revert arithmetic stays correct.
    /// </summary>
    protected override void ApplyScoreChange(JudgementResult result)
    {
        if (result.Type is HitResult.Ok or HitResult.Meh)
        {
            Combo.Value = 0;
            set_combo_after(result, 0);
        }
    }

    protected override void RemoveScoreChange(JudgementResult result)
    {
        base.RemoveScoreChange(result);

        if (result is BmsLongNoteJudgementResult longNoteResult)
            additionalLongNoteEvents.Remove(longNoteResult);
    }

    protected override HitEvent CreateHitEvent(JudgementResult result)
    {
        var frameworkEvent = base.CreateHitEvent(result);

        if (result is not BmsLongNoteJudgementResult longNoteResult || longNoteResult.EndpointResults.Count == 0)
            return frameworkEvent;

        var events = longNoteResult.EndpointResults
            .Select(endpoint => createLongNoteEndpointEvent(endpoint, frameworkEvent.LastHitObject))
            .ToArray();

        if (events.Length > 1)
            additionalLongNoteEvents[longNoteResult] = events[..^1];

        return events[^1];
    }

    protected override IEnumerable<HitObject> EnumerateHitObjects(IBeatmap beatmap)
    {
        foreach (var hitObject in base.EnumerateHitObjects(beatmap).Order(JudgementOrderComparer.DEFAULT))
        {
            yield return hitObject;

            if (hitObject is BmsLongNote longNote && longNote.Beatmap?.LockedLongNoteMode is BmsLongNoteMode.ChargeNote or BmsLongNoteMode.HellChargeNote)
                yield return longNote.CreateSyntheticEndpoint(longNote.EndTime);
        }
    }

    protected override HitResult GetSimulatedHitResult(Judgement judgement) => judgement is BmsJudgement { MaxResult: HitResult.Meh }
        ? HitResult.IgnoreMiss
        : base.GetSimulatedHitResult(judgement);

    private static Action<JudgementResult, int> createComboAfterSetter()
    {
        try
        {
            var field = typeof(JudgementResult).GetField("<ComboAfterJudgement>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (field == null)
            {
                Logger.Log(
                    "BMS ScoreProcessor: Could not find JudgementResult.ComboAfterJudgement backing field. "
                    + "The osu! framework may have changed; BAD/POOR combo-break revert will not function correctly.",
                    level: LogLevel.Error);
                return (_, _) => { };
            }

            return (r, v) => field.SetValue(r, v);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "BMS ScoreProcessor: Failed to bind ComboAfterJudgement setter via reflection. "
                             + "BAD/POOR combo-break revert will not function correctly.");
            return (_, _) => { };
        }
    }

    private static Action<BmsScoreProcessor, HitEvent> createHitEventAdder()
    {
        var field = typeof(ScoreProcessor).GetField("hitEvents", BindingFlags.Instance | BindingFlags.NonPublic);

        if (field == null)
        {
            Logger.Log(
                "BMS ScoreProcessor: Could not bind ScoreProcessor hitEvents. Empty POORs will be missing from result statistics.",
                level: LogLevel.Error);
            return (_, _) => { };
        }

        return (processor, hitEvent) => ((List<HitEvent>)field.GetValue(processor)!).Add(hitEvent);
    }

    private static HitEvent createLongNoteEndpointEvent(
        BmsLongNoteEndpointResult endpointResult,
        HitObject? lastHitObject)
    {
        var endpoint = endpointResult.Source.CreateSyntheticEndpoint(endpointResult.ExpectedTime);
        return new HitEvent(endpointResult.TimeOffset, endpointResult.GameplayRate, endpointResult.Result, endpoint, lastHitObject, null);
    }

    private class JudgementOrderComparer : IComparer<HitObject>
    {
        public static readonly JudgementOrderComparer DEFAULT = new();

        public int Compare(HitObject? x, HitObject? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            var result = x.GetEndTime().CompareTo(y.GetEndTime());
            if (result != 0)
                return result;

            // Native BMS should judge objects with identical end times in chart/lane order.
            if (x is BmsHitObject bx && y is BmsHitObject by)
                return bx.Column.CompareTo(by.Column);

            return 0;
        }
    }
}
