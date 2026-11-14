using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Bms;
using JudgementResult = osu.Game.Rulesets.Judgements.JudgementResult;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;

public partial class BmsScoreProcessor() : ScoreProcessor(new BmsRuleset())
{
    private static readonly ResultId[] counted_results =
    [
        BmsResults.PGREAT, BmsResults.GREAT, BmsResults.GOOD, BmsResults.BAD,
        BmsResults.POOR, BmsResults.EMPTY_POOR, BmsResults.MINE_HIT,
    ];

    private static readonly Action<JudgementResult, int> set_combo_after = typeof(JudgementResult)
        .GetProperty(nameof(JudgementResult.ComboAfterJudgement))!.GetSetMethod(true)!
        .CreateDelegate<Action<JudgementResult, int>>();

    private static readonly Action<JudgementResult, int> set_highest_combo_after = typeof(JudgementResult)
        .GetProperty(nameof(JudgementResult.HighestComboAfterJudgement))!.GetSetMethod(true)!
        .CreateDelegate<Action<JudgementResult, int>>();

    private double latestEndTime = double.MaxValue;
    private int maximumScoringJudgementCount;
    private readonly List<BmsJudgementEvent> judgementEvents = [];
    private readonly Dictionary<JudgementResult, BmsJudgementEvent> eventsByResult = new();
    private readonly List<TimingHitEventEntry> timingHitEventEntries = [];
    private readonly List<HitEvent> timingHitEvents = [];

    public IReadOnlyList<BmsJudgementEvent> JudgementEvents => judgementEvents;

    public int ScoringJudgementEventCount { get; private set; }

    /// <summary>
    ///     Changes whenever the result-count dictionary changes.
    /// </summary>
    public long StatisticsVersion { get; private set; }

    internal bool IsRebuildingReplay { get; set; }

    public bool? EmptyPoorBreaksCombo { get; set; }

    internal void ResetReplayState()
    {
        // The framework deliberately locks failed ranks until a new play is created.
        ((Bindable<ScoreRank>)Rank).Value = ScoreRank.X;
        Reset(false);
        ((BindableBool)HasCompleted).Value = false;
    }

    public event Action<BmsTimingObservation>? EmptyPoorRegistered;

    public event Action<BmsTimingObservation>? NonConsumingJudgementRegistered;

    public override void ApplyBeatmap(IBeatmap beatmap)
    {
        using var windows = BmsJudgementProfileProvider.UseWindowMods(Mods.Value.OfType<IApplicableToJudgement>().ToArray());
        base.ApplyBeatmap(beatmap);

        if (beatmap.HitObjects.Count == 0)
            latestEndTime = 0;
        else
        {
            // Latest possible judgement time = max object end time + worst-case slow window.
            var maxEndTime = beatmap.HitObjects.Max(h => h.GetEndTime());
            var slowWindow = beatmap.HitObjects.OfType<BmsHitObject>()
                .Select(note => BmsJudgementProfileProvider.GetTable(note, false).SlowWindowFor(HitResult.Ok))
                .DefaultIfEmpty(BmsHitWindows.FALLBACK_BAD_WINDOW).Max();

            latestEndTime = maxEndTime + slowWindow;
        }

        // A selected interval includes its trailing silence, while complete holds and slow judgements may extend beyond it.
        if (beatmap is BmsBeatmap { PracticeEndTime: { } practiceEndTime })
            latestEndTime = Math.Max(latestEndTime, practiceEndTime);
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

    public void RegisterNonConsumingJudgement(HitResult result, double eventTime, double expectedTime, int column, ScoreState engineScore)
    {
        applyEngineScore(engineScore);
        StatisticsVersion++;
        var source = new BmsJudgementSource(eventTime, column,
            result == HitResult.Ok ? BmsJudgementSourceKind.NonConsumingBad : BmsJudgementSourceKind.EmptyPoor);
        var observation = new BmsTimingObservation(BmsTimingObservationKind.Note, expectedTime, eventTime, 1, result);
        var judgementEvent = new BmsJudgementEvent(source, result, [observation]);
        addJudgementEvent(judgementEvent);

        if (!IsRebuildingReplay)
        {
            NonConsumingJudgementRegistered?.Invoke(observation);
            if (result == HitResult.Miss)
                EmptyPoorRegistered?.Invoke(observation);
        }
    }

    /// <summary>
    ///     Applies a separate CN/HCN endpoint without requiring the source drawable to complete.
    /// </summary>
    public BmsLongNoteJudgementResult ApplySyntheticLongNoteEndpoint(BmsLongNoteEndpointResult endpointResult, bool suppressPenalty,
                                                                     RhythmEngine.Core.JudgementResult engineResult)
    {
        var endpoint = endpointResult.Source.CreateSyntheticEndpoint(endpointResult.ExpectedTime);
        var result = new BmsLongNoteJudgementResult(endpoint, endpoint.CreateJudgement(), [endpointResult])
        {
            Type = endpointResult.Result,
            SuppressPenalty = suppressPenalty,
            EngineResult = engineResult,
        };
        result.SetTiming(endpointResult.ApplicationTime ?? endpointResult.EventTime, endpointResult.GameplayRate);
        ApplyResult(result);
        return result;
    }

    public override void PopulateScore(ScoreInfo score)
    {
        base.PopulateScore(score);
        score.HitEvents = timingHitEvents;

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
        TotalScoreWithoutMods.Value = 0;
        judgementEvents.Clear();
        eventsByResult.Clear();
        timingHitEventEntries.Clear();
        timingHitEvents.Clear();
        ScoringJudgementEventCount = 0;
        StatisticsVersion++;
    }

    /// <summary>
    ///     Scaled EX-score.  Accuracy = EXScore / MaxEXScore (0–1).
    ///     Total score = accuracy × 1 000 000; bonus portion carries over from base.
    /// </summary>
    protected override double ComputeTotalScore(double comboProgress, double accuracyProgress, double bonusPortion)
        => 1_000_000 * Accuracy.Value * accuracyProgress;

    // EX-score has no combo multiplier — every PGREAT is always worth exactly 2.
    protected override double GetComboScoreChange(JudgementResult result) => 0;

    protected override void ApplyScoreChange(JudgementResult result)
    {
        StatisticsVersion++;
        // Count independently of hit-event recording, which can be disabled by the host.
        if (result.HitObject is BmsHitObject and not BmsLandmine)
            ScoringJudgementEventCount++;

        if (IsSimulating)
            return;

        if (result is not BmsJudgementResult { EngineResult: { } engine })
            throw new InvalidOperationException("Gameplay scoring requires an engine judgement.");

        applyEngineScore(engine.Score);
        set_combo_after(result, engine.Score.Combo);
        set_highest_combo_after(result, engine.Score.MaximumCombo);
    }

    private void applyEngineScore(ScoreState state)
    {
        Combo.Value = state.Combo;
        HighestCombo.Value = state.MaximumCombo;
        SetScoreProcessorStatistics(new ScoreProcessorStatistics
        {
            BaseScore = state.Score,
            MaximumBaseScore = state.AccuracyMaximum,
            // Stored ruleset scores include detonated mines in osu!'s progress count, with zero EX potential.
            AccuracyJudgementCount = (int)(state.AccuracyMaximum / 2 + state.Counters.GetValueOrDefault(BmsResults.MINE_HIT.Value)),
        });
        ScoreResultCounts.Clear();

        foreach (var id in counted_results)
        {
            if (!state.Counters.TryGetValue(id.Value, out var count))
                continue;

            var result = BmsEngineChart.HitResultFor(id);
            ScoreResultCounts[result] = ScoreResultCounts.GetValueOrDefault(result) + (int)count;
        }
    }

    protected override void RemoveScoreChange(JudgementResult result)
    {
        StatisticsVersion++;
        base.RemoveScoreChange(result);
        if (result is BmsJudgementResult { SuppressPenalty: true })
            ScoreResultCounts[result.Type]++;

        if (result.HitObject is BmsHitObject and not BmsLandmine)
            ScoringJudgementEventCount--;

        if (eventsByResult.Remove(result, out var judgementEvent))
            removeJudgementEvent(judgementEvent);
    }

    protected override HitEvent CreateHitEvent(JudgementResult result)
    {
        var frameworkEvent = base.CreateHitEvent(result);
        var judgementEvent = createJudgementEvent(result);
        var timingHitEvent = addJudgementEvent(judgementEvent, result.HitObject);
        eventsByResult.Add(result, judgementEvent);

        if (judgementEvent.SuppressPenalty)
            return frameworkEvent;

        return BmsJudgementEventProjection.CreateTimingHitEvent(
            judgementEvent.Source,
            judgementEvent.TimingObservations[^1],
            timingHitEvent!.Value.HitObject,
            frameworkEvent.LastHitObject);
    }

    protected override IEnumerable<HitObject> EnumerateHitObjects(IBeatmap beatmap)
    {
        foreach (var hitObject in base.EnumerateHitObjects(beatmap).Order(JudgementOrderComparer.DEFAULT))
        {
            yield return hitObject;

            if (hitObject is BmsLongNote longNote && longNote.EffectiveMode is BmsLongNoteMode.ChargeNote or BmsLongNoteMode.HellChargeNote)
                yield return longNote.CreateSyntheticEndpoint(longNote.EndTime);
        }
    }

    protected override HitResult GetSimulatedHitResult(Judgements.Judgement judgement) => judgement is BmsJudgement { MaxResult: HitResult.Meh }
        ? HitResult.IgnoreMiss
        : base.GetSimulatedHitResult(judgement);

    private HitEvent? addJudgementEvent(BmsJudgementEvent judgementEvent, HitObject? sourceHitObject = null)
    {
        judgementEvents.Add(judgementEvent);
        return addTimingHitEvents(judgementEvent, sourceHitObject);
    }

    private void removeJudgementEvent(BmsJudgementEvent judgementEvent)
    {
        // Framework reverts usually target the newest event, so search from the end.
        for (var i = judgementEvents.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(judgementEvents[i], judgementEvent))
                continue;

            judgementEvents.RemoveAt(i);
            break;
        }

        removeTimingHitEvents(judgementEvent);
    }

    private HitEvent? addTimingHitEvents(BmsJudgementEvent judgementEvent, HitObject? sourceHitObject)
    {
        if (judgementEvent.SuppressPenalty)
            return null;

        HitEvent? lastTimingHitEvent = null;

        foreach (var observation in judgementEvent.TimingObservations)
        {
            var insertionIndex = findTimingInsertionIndex(observation.ActualTime);
            // Short notes already have the correct timing anchor. Only detached events
            // and long-note endpoints need a projected object for the timing timeline.
            var hitObject = sourceHitObject is BmsNote or BmsLandmine
                ? sourceHitObject
                : BmsJudgementEventProjection.CreateTimingHitEvent(judgementEvent.Source, observation, null).HitObject;

            timingHitEventEntries.Insert(insertionIndex, new TimingHitEventEntry(judgementEvent, observation, hitObject));
            timingHitEvents.Insert(insertionIndex, createTimingHitEvent(insertionIndex));
            lastTimingHitEvent = timingHitEvents[insertionIndex];
            repairNextTimingHitEvent(insertionIndex);
        }

        return lastTimingHitEvent;
    }

    private void removeTimingHitEvents(BmsJudgementEvent judgementEvent)
    {
        if (judgementEvent.SuppressPenalty)
            return;

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
                endpoint.Result)).ToArray();

            return new BmsJudgementEvent(
                BmsJudgementSource.From(longNoteResult.EndpointResults.Count > 1 ? source : result.HitObject),
                result.Type,
                observations,
                longNoteResult.SuppressPenalty);
        }

        var expectedTime = result.HitObject.GetEndTime();
        return new BmsJudgementEvent(BmsJudgementSource.From(result.HitObject), result.Type,
        [
            new BmsTimingObservation(
                BmsTimingObservationKind.Note,
                expectedTime,
                result is BmsJudgementResult bms ? bms.EventTime : expectedTime + result.TimeOffset,
                result.GameplayRate,
                result.Type),
        ], result is BmsJudgementResult { SuppressPenalty: true });
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
