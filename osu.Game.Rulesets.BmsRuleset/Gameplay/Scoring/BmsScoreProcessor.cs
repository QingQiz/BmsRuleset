using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using JudgementResult = osu.Game.Rulesets.Judgements.JudgementResult;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;

public partial class BmsScoreProcessor : ScoreProcessor
{
    private GameplayProfile profile = BmsGameplayProfile.INSTANCE;

    public BmsScoreProcessor()
        : base(new BmsRuleset())
    {
        Mods.BindValueChanged(change => profile = GameplayProfile.FromMods(change.NewValue), true);
    }

    private static readonly Action<JudgementResult, int> set_combo_after = typeof(JudgementResult)
        .GetProperty(nameof(JudgementResult.ComboAfterJudgement))!.GetSetMethod(true)!
        .CreateDelegate<Action<JudgementResult, int>>();

    private static readonly Action<JudgementResult, int> set_highest_combo_after = typeof(JudgementResult)
        .GetProperty(nameof(JudgementResult.HighestComboAfterJudgement))!.GetSetMethod(true)!
        .CreateDelegate<Action<JudgementResult, int>>();

    internal ScoreState EngineScore { get; private set; } = new();
    private bool engineCompleted;

    internal void ApplyEngineState(JudgementSession session)
    {
        ApplyEngineScore(session.Score);
        engineCompleted = session.Completed;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "updateScore")]
    private static extern void updateScore(ScoreProcessor processor);

    internal BmsEngineChart EngineChart { get; private set; } = null!;
    private readonly List<BmsJudgementEvent> judgementEvents = [];
    private readonly List<TimingHitEventEntry> timingHitEventEntries = [];
    private readonly List<HitEvent> timingHitEvents = [];

    public IReadOnlyList<BmsJudgementEvent> JudgementEvents => judgementEvents;

    public int ScoringJudgementEventCount => EngineScore.JudgedOpportunities;

    /// <summary>
    ///     Changes whenever the result-count dictionary changes.
    /// </summary>
    public long StatisticsVersion { get; private set; }

    internal bool IsRebuildingReplay { get; set; }

    internal void ResetReplayState()
    {
        // The framework deliberately locks failed ranks until a new play is created.
        ((Bindable<ScoreRank>)Rank).Value = ScoreRank.X;
        Reset(false);
        ((BindableBool)HasCompleted).Value = false;
    }

    public event Action<BmsTimingObservation>? NonConsumingJudgementRegistered;

    public override void ApplyBeatmap(IBeatmap beatmap)
    {
        EngineChart = new BmsEngineChart((BmsBeatmap)beatmap, profile);
        // Only the active playable conversion publishes framework windows; rating and practice compilation are read-only.
        foreach (var note in EngineChart.Objects)
            note.HitWindows = profile.CreateHitWindows(note, EngineChart.TimingFor(note), NoteParts.HEAD);
        base.ApplyBeatmap(beatmap);
    }

    public override int GetBaseScoreForResult(HitResult result) => profile.AccuracyValue(result);

    public override ScoreRank RankFromScore(double accuracy, IReadOnlyDictionary<HitResult, int> results)
        => profile.RankFromScore(accuracy, results);

    public override double AccuracyCutoffFromRank(ScoreRank rank) => profile.AccuracyCutoffFromRank(rank);

    public void RegisterNonConsumingJudgement(HitResult result, double eventTime, double expectedTime, int column, RhythmEngine.Core.JudgementResult engineResult)
    {
        ApplyEngineScore(engineResult.Score);
        StatisticsVersion++;
        var source = new BmsJudgementSource(eventTime, column,
            profile.EventKind(engineResult.Event) == GameplayEventKind.NonConsumingHit
                ? BmsJudgementSourceKind.NonConsumingHit
                : BmsJudgementSourceKind.EmptyInput);
        var observation = new BmsTimingObservation(BmsTimingObservationKind.Note, expectedTime, eventTime, 1, result);
        var judgementEvent = new BmsJudgementEvent(source, result, [observation]);
        addJudgementEvent(judgementEvent);

        if (!IsRebuildingReplay)
        {
            NonConsumingJudgementRegistered?.Invoke(observation);
        }
    }

    /// <summary>
    ///     Applies a separate CN/HCN endpoint without requiring the source drawable to complete.
    /// </summary>
    public BmsLongNoteJudgementResult ApplySyntheticLongNoteEndpoint(BmsLongNoteEndpointResult endpointResult, bool suppressPenalty,
                                                                     RhythmEngine.Core.JudgementResult engineResult)
    {
        var endpoint = createEndpoint(endpointResult.Source, endpointResult.Kind);
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
        if (HasCompleted is BindableBool bb)
            bb.Value = engineCompleted;
    }

    protected override void Reset(bool storeResults)
    {
        if (storeResults)
        {
            EngineScore = EngineScore with { Score = EngineChart.MaximumScore };
            foreach (var (result, count) in EngineChart.Chart.Notes.SelectMany(profile.MaximumAuxiliaryResults))
            {
                ScoreResultCounts[result] = ScoreResultCounts.GetValueOrDefault(result) + count;
                if (result.AffectsCombo())
                    HighestCombo.Value += count;
            }

            var statistics = GetScoreProcessorStatistics();
            statistics.BaseScore = statistics.MaximumBaseScore = EngineChart.Chart.ScorePlan.MaximumAccuracy;
            SetScoreProcessorStatistics(statistics);
        }

        base.Reset(storeResults);
        EngineScore = EngineChart.Score.InitialState;
        engineCompleted = false;
        TotalScoreWithoutMods.Value = 0;
        judgementEvents.Clear();
        timingHitEventEntries.Clear();
        timingHitEvents.Clear();
        StatisticsVersion++;
    }

    protected override double ComputeTotalScore(double comboProgress, double accuracyProgress, double bonusPortion)
        => EngineChart is { MaximumScore: > 0 } ? 1_000_000 * EngineScore.Score / EngineChart.MaximumScore : 0;

    // The engine already applies the scoring policy's combo contribution.
    protected override double GetComboScoreChange(JudgementResult result) => 0;

    protected override void ApplyScoreChange(JudgementResult result)
    {
        StatisticsVersion++;
        if (IsSimulating)
            return;

        if (result is not BmsJudgementResult { EngineResult: { } engine })
            throw new InvalidOperationException("Gameplay scoring requires an engine judgement.");

        ApplyEngineScore(engine.Score);
        set_combo_after(result, engine.Score.Combo);
        set_highest_combo_after(result, engine.Score.MaximumCombo);
    }

    internal void ApplyEngineScore(ScoreState state)
    {
        if (ReferenceEquals(EngineScore, state))
            return;

        EngineScore = state;
        StatisticsVersion++;
        Combo.Value = state.Combo;
        HighestCombo.Value = state.MaximumCombo;
        SetScoreProcessorStatistics(new ScoreProcessorStatistics
        {
            BaseScore = state.AccuracyPoints,
            MaximumBaseScore = state.AccuracyMaximum,
            AccuracyJudgementCount = profile.AccuracyJudgementCount(state),
        });
        ScoreResultCounts.Clear();

        foreach (var (id, count) in state.ResultCounts)
        {
            var result = profile.HitResultFor(id);
            if (result != HitResult.None)
                ScoreResultCounts[result] = ScoreResultCounts.GetValueOrDefault(result) + count;
        }

        updateScore(this);
    }

    protected override void RemoveScoreChange(JudgementResult result)
        => throw new NotSupportedException("Rewind must rebuild the engine session from recorded inputs.");

    protected override HitEvent CreateHitEvent(JudgementResult result)
    {
        var frameworkEvent = base.CreateHitEvent(result);
        var judgementEvent = createJudgementEvent(result);
        var timingHitEvent = addJudgementEvent(judgementEvent, result.HitObject);

        if (judgementEvent.SuppressPenalty)
            return frameworkEvent;

        return BmsJudgementEventProjection.CreateTimingHitEvent(
            judgementEvent.Source,
            judgementEvent.TimingObservations[^1],
            timingHitEvent!.Value.HitObject,
            frameworkEvent.LastHitObject);
    }

    protected override IEnumerable<HitObject> EnumerateHitObjects(IBeatmap beatmap)
        => EngineChart.Chart.ScorePlan.Opportunities.Select(opportunity =>
        {
            var note = EngineChart.Objects[opportunity.NoteId];
            return note is BmsLongNote longNote
                ? createEndpoint(longNote, opportunity.Part == NoteParts.HEAD ? BmsLongNoteEndpointKind.Head : BmsLongNoteEndpointKind.Tail)
                : note;
        }).Order(JudgementOrderComparer.DEFAULT);

    private BmsNote createEndpoint(BmsLongNote note, BmsLongNoteEndpointKind kind)
        => new()
        {
            StartTime = kind == BmsLongNoteEndpointKind.Head ? note.StartTime : note.EndTime,
            Column = note.Column,
            Beatmap = note.Beatmap,
            HitWindows = profile.CreateHitWindows(note, EngineChart.TimingFor(note),
                kind == BmsLongNoteEndpointKind.Head ? NoteParts.HEAD : NoteParts.TAIL),
        };

    private HitEvent? addJudgementEvent(BmsJudgementEvent judgementEvent, HitObject? sourceHitObject = null)
    {
        judgementEvents.Add(judgementEvent);
        return addTimingHitEvents(judgementEvent, sourceHitObject);
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

            timingHitEventEntries.Insert(insertionIndex, new TimingHitEventEntry(observation, hitObject));
            timingHitEvents.Insert(insertionIndex, createTimingHitEvent(insertionIndex));
            lastTimingHitEvent = timingHitEvents[insertionIndex];
            repairNextTimingHitEvent(insertionIndex);
        }

        return lastTimingHitEvent;
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
        BmsTimingObservation Observation,
        HitObject HitObject);
}
