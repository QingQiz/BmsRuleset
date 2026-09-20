using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring;

public partial class BmsScoreProcessor() : ScoreProcessor(new BmsRuleset())
{
    private static readonly Action<JudgementResult, int> set_combo_after = createComboAfterSetter();

    private double latestEndTime = double.MaxValue;
    private int maximumScoringJudgementCount;
    private readonly List<BmsJudgementEvent> judgementEvents = [];
    private readonly Dictionary<JudgementResult, BmsJudgementEvent> eventsByResult = new();
    private readonly List<TimingHitEventEntry> timingHitEventEntries = [];
    private readonly List<HitEvent> timingHitEvents = [];
    private readonly List<BmsJudgementEvent> emptyPoorEvents = [];

    public IReadOnlyList<BmsJudgementEvent> JudgementEvents => judgementEvents;

    public int ScoringJudgementEventCount { get; private set; }

    public event Action<BmsTimingObservation>? EmptyPoorRegistered;

    public override void ApplyBeatmap(IBeatmap beatmap)
    {
        base.ApplyBeatmap(beatmap);

        if (beatmap.HitObjects.Count == 0)
        {
            latestEndTime = 0;
            return;
        }

        // Latest possible judgement time = max object end time + worst-case slow window.
        var maxEndTime = beatmap.HitObjects.Max(h => h.GetEndTime());
        var slowWindow = beatmap.HitObjects[0].HitWindows?.WindowFor(HitResult.Ok)
                         ?? BmsHitWindows.FALLBACK_BAD_WINDOW;

        latestEndTime = maxEndTime + slowWindow;
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
            _ => BmsExScore.RankFromAccuracy(accuracy, allowX: false),
        };

        // BMS pass/fail is determined solely by gauge at song end, not by score accuracy.
        // ScoreRank.F is never assigned here; failure is communicated through BmsHealthProcessor.
    }

    public override double AccuracyCutoffFromRank(ScoreRank rank) => BmsExScore.AccuracyCutoffFromRank(rank);

    /// <summary>
    ///     Records an Empty POOR: a keypress that found no note to consume.
    ///     Increments the Empty POOR counter stored under
    ///     <see cref="HitResult.Miss"/> in the score statistics so it
    ///     appears in the results-screen statistics and the live HUD judgement counter.
    ///     Empty POORs do not affect EX-score, accuracy, or combo.
    /// </summary>
    public void RegisterEmptyPoor()
    {
        // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
        RegisterEmptyPoor(Clock?.CurrentTime ?? 0);
    }

    public void RegisterEmptyPoor(double eventTime)
        => RegisterEmptyPoor(eventTime, eventTime, 0);

    public void RegisterEmptyPoor(double eventTime, double expectedTime, int column)
    {
        ScoreResultCounts[HitResult.Miss] = ScoreResultCounts.GetValueOrDefault(HitResult.Miss) + 1;

        var source = new BmsJudgementSource(eventTime, column, BmsJudgementSourceKind.EmptyPoor);
        var observation = new BmsTimingObservation(BmsTimingObservationKind.Note, expectedTime, eventTime, 1, HitResult.Miss);
        var judgementEvent = new BmsJudgementEvent(source, HitResult.Miss, [observation]);
        addJudgementEvent(judgementEvent);
        emptyPoorEvents.Add(judgementEvent);
        EmptyPoorRegistered?.Invoke(observation);
    }

    internal void RewindEmptyPoors(double time)
    {
        while (emptyPoorEvents.Count > 0 && emptyPoorEvents[^1].Source.StartTime > time)
        {
            removeJudgementEvent(emptyPoorEvents[^1]);
            emptyPoorEvents.RemoveAt(emptyPoorEvents.Count - 1);
            ScoreResultCounts[HitResult.Miss]--;
        }
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
        score.HitEvents = timingHitEvents;
        BmsJudgementEventStore.SetView(score, judgementEvents);

        // Attribution (e.g. which gauge an Auto Gauge run resolved to) is owned by the mods
        // that introduce the behaviour, so the score processor stays free of gauge-specific logic.
        foreach (var mod in Mods.Value.OfType<IApplicableToScorePopulation>())
            mod.ApplyToScore(score);

        BmsModPaused.ApplyToScore(score);
    }

    protected override void Update()
    {
        // The clock can reach the end before the final passive POOR or CN/HCN tail is processed.
        // Wait for every scoring judgement so pass/fail and Auto Gauge use the final HP.
        // Mines may expire without a result, so they must not contribute to this count.
        // This must also clear completion after a rewind because looping players wait for
        // that transition before they stop seeking back to the start of the beatmap.
        if (HasCompleted is BindableBool bb)
            bb.Value = Time.Current >= latestEndTime && ScoringJudgementEventCount == maximumScoringJudgementCount;
    }

    protected override void Reset(bool storeResults)
    {
        if (storeResults)
            maximumScoringJudgementCount = ScoringJudgementEventCount;

        base.Reset(storeResults);
        judgementEvents.Clear();
        emptyPoorEvents.Clear();
        eventsByResult.Clear();
        timingHitEventEntries.Clear();
        timingHitEvents.Clear();
        ScoringJudgementEventCount = 0;
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
        // Count independently of hit-event recording, which can be disabled by the host.
        if (result.HitObject is BmsHitObject and not BmsLandmine)
            ScoringJudgementEventCount++;

        if (result.Type is HitResult.Ok or HitResult.Meh)
        {
            Combo.Value = 0;
            set_combo_after(result, 0);
        }
    }

    protected override void RemoveScoreChange(JudgementResult result)
    {
        base.RemoveScoreChange(result);

        if (result.HitObject is BmsHitObject and not BmsLandmine)
            ScoringJudgementEventCount--;

        if (eventsByResult.Remove(result, out var judgementEvent))
            removeJudgementEvent(judgementEvent);
    }

    protected override HitEvent CreateHitEvent(JudgementResult result)
    {
        var frameworkEvent = base.CreateHitEvent(result);
        var judgementEvent = createJudgementEvent(result);
        addJudgementEvent(judgementEvent);
        eventsByResult.Add(result, judgementEvent);

        return BmsJudgementEventProjection.CreateTimingHitEvent(
            judgementEvent.Source,
            judgementEvent.TimingObservations[^1],
            frameworkEvent.LastHitObject);
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

    private void addJudgementEvent(BmsJudgementEvent judgementEvent)
    {
        judgementEvents.Add(judgementEvent);
        addTimingHitEvents(judgementEvent);
    }

    private void removeJudgementEvent(BmsJudgementEvent judgementEvent)
    {
        // Rewinds normally remove the newest event. Searching from the end avoids
        // visiting the entire historical ledger for every reverted judgement.
        for (var i = judgementEvents.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(judgementEvents[i], judgementEvent))
                continue;

            judgementEvents.RemoveAt(i);
            break;
        }

        removeTimingHitEvents(judgementEvent);
    }

    private void addTimingHitEvents(BmsJudgementEvent judgementEvent)
    {
        foreach (var observation in judgementEvent.TimingObservations)
        {
            var insertionIndex = findTimingInsertionIndex(observation.ActualTime);
            var hitObject = BmsJudgementEventProjection.CreateTimingHitEvent(judgementEvent.Source, observation, null).HitObject;

            timingHitEventEntries.Insert(insertionIndex, new TimingHitEventEntry(judgementEvent, observation, hitObject));
            timingHitEvents.Insert(insertionIndex, createTimingHitEvent(insertionIndex));
            repairNextTimingHitEvent(insertionIndex);
        }
    }

    private void removeTimingHitEvents(BmsJudgementEvent judgementEvent)
    {
        for (var observationIndex = judgementEvent.TimingObservations.Count - 1; observationIndex >= 0; observationIndex--)
        {
            var time = judgementEvent.TimingObservations[observationIndex].ActualTime;
            // Normal LN observations can surround other notes. Locate each endpoint
            // by time and identity rather than scanning all remaining hit events.
            for (var i = findTimingInsertionIndex(time) - 1; i >= 0 && timingHitEventEntries[i].Observation.ActualTime == time; i--)
            {
                if (!ReferenceEquals(timingHitEventEntries[i].JudgementEvent, judgementEvent))
                    continue;

                timingHitEventEntries.RemoveAt(i);
                timingHitEvents.RemoveAt(i);
                repairNextTimingHitEvent(i - 1);
                break;
            }
        }
    }

    private int findTimingInsertionIndex(double actualTime)
    {
        if (timingHitEventEntries.Count == 0 || timingHitEventEntries[^1].Observation.ActualTime <= actualTime)
            return timingHitEventEntries.Count;

        var low = 0;
        var high = timingHitEventEntries.Count;

        // Match OrderBy's stable ordering by placing equal-time observations after existing entries.
        while (low < high)
        {
            var middle = low + (high - low) / 2;

            if (timingHitEventEntries[middle].Observation.ActualTime <= actualTime)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private HitEvent createTimingHitEvent(int index)
    {
        var entry = timingHitEventEntries[index];
        return new HitEvent(
            entry.Observation.TimeOffset,
            entry.Observation.GameplayRate,
            entry.Observation.Result,
            entry.HitObject,
            index == 0 ? null : timingHitEvents[index - 1].HitObject,
            null);
    }

    private void repairNextTimingHitEvent(int index)
    {
        var nextIndex = index + 1;

        if (nextIndex < timingHitEvents.Count)
            timingHitEvents[nextIndex] = createTimingHitEvent(nextIndex);
    }

    private static Action<JudgementResult, int> createComboAfterSetter()
    {
        try
        {
            var field = typeof(JudgementResult).GetField("<ComboAfterJudgement>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (field == null)
            {
                BmsLogger.Log(
                    "BMS ScoreProcessor: Could not find JudgementResult.ComboAfterJudgement backing field. "
                    + "The osu! framework may have changed; BAD/POOR combo-break revert will not function correctly.",
                    level: LogLevel.Error);
                return (_, _) => { };
            }

            return (r, v) => field.SetValue(r, v);
        }
        catch (Exception ex)
        {
            BmsLogger.Error(ex, "BMS ScoreProcessor: Failed to bind ComboAfterJudgement setter via reflection. "
                                + "BAD/POOR combo-break revert will not function correctly.");
            return (_, _) => { };
        }
    }

    private static BmsJudgementEvent createJudgementEvent(JudgementResult result)
    {
        if (result is BmsLongNoteJudgementResult longNoteResult)
        {
            var source = longNoteResult.EndpointResults[0].Source;
            var observations = longNoteResult.EndpointResults.Select(endpoint => new BmsTimingObservation(
                endpoint.Kind == BmsLongNoteEndpointKind.Head
                    ? BmsTimingObservationKind.LongNoteHead
                    : BmsTimingObservationKind.LongNoteTail,
                endpoint.ExpectedTime,
                endpoint.EventTime,
                endpoint.GameplayRate,
                endpoint.Result));

            return new BmsJudgementEvent(
                BmsJudgementSource.From(longNoteResult.EndpointResults.Count > 1 ? source : result.HitObject),
                result.Type,
                observations);
        }

        var expectedTime = result.HitObject.GetEndTime();
        return new BmsJudgementEvent(BmsJudgementSource.From(result.HitObject), result.Type,
        [
            new BmsTimingObservation(
                BmsTimingObservationKind.Note,
                expectedTime,
                expectedTime + result.TimeOffset,
                result.GameplayRate,
                result.Type),
        ]);
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

    private readonly record struct TimingHitEventEntry(
        BmsJudgementEvent JudgementEvent,
        BmsTimingObservation Observation,
        HitObject HitObject);
}
