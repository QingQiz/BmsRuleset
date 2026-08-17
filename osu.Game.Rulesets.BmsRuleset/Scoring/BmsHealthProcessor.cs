using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring;

public partial class BmsHealthProcessor : HealthProcessor
{

    public Bindable<BmsGaugeDisplayProfile> DisplayProfile { get; } =
        new(BmsGaugeProfileFactory.Create(BmsGaugeType.Normal).Display);

    /// <summary>
    /// The gauge type that ultimately determined pass/fail at song end.
    /// </summary>
    public BmsGaugeType WorstGaugeType =>
        gaugeStates.Count > 0 && endResultIndex < gaugeStates.Count
            ? gaugeStates[endResultIndex].GaugeType
            : BmsGaugeType.Normal;

    /// <summary>
    /// Whether HP ever dropped to 0 during this play.
    /// to determine gauge-failed rank even when NF mod prevents mid-song failure.
    /// </summary>
    public bool HasEverFailed { get; private set; }

    public IReadOnlyList<BmsGaugeHistoryEvent> GaugeHistory => gaugeHistory;

    public BmsGaugeType GaugeType { get; private set; } = BmsGaugeType.Normal;

    public BmsGaugeProfile GaugeProfile { get; private set; } = BmsGaugeProfileFactory.Create(BmsGaugeType.Normal);

    public IReadOnlyList<BmsGaugeStateSnapshot> CurrentGaugeStates => gaugeStates
        .Select(state => new BmsGaugeStateSnapshot(state.GaugeType, state.CurrentHp, state.IsHpFailed))
        .ToArray();

    private readonly List<GaugeState> gaugeStates = [];
    private readonly List<BmsGaugeHistoryEvent> gaugeHistory = [];

    private const double max_landmine_damage_percent = (36 * 36 - 1) / 2d;
    private int activeGaugeIndex;
    private int endResultIndex;

    private IBeatmap? beatmap;
    private bool initialized;
    private BmsGaugeProfileFamily layoutProfileFamily = BmsGaugeProfileFamily.SevenKeys;
    private BmsGaugeProfileFamily? profileFamilyOverride;

    public override void ApplyBeatmap(IBeatmap beatmap)
    {
        base.ApplyBeatmap(beatmap);
        this.beatmap = beatmap;

        if (beatmap is BmsBeatmap bmsBeatmap)
        {
            layoutProfileFamily = BmsGaugeProfileFamilyProvider.FromLayout(bmsBeatmap.LayoutVariant);
            refreshGaugeProfiles();
        }
    }

    /// <summary>
    ///     Applies an Empty POOR gauge penalty directly — no note is consumed.
    /// </summary>
    public void RegisterEmptyPoor(double? eventTime = null)
    {
        ensureInitialized();
        syncActiveStateFromHealth();

        for (var i = 0; i < gaugeStates.Count; i++)
        {
            var state = gaugeStates[i];
            if (state.IsHpFailed) continue;

            var delta = state.Calculator!.GetDeltaFor(HitResult.Miss, state.CurrentHp);
            state.CurrentHp = state.Calculator.ApplyDelta(state.CurrentHp, delta);

            if (state.CurrentHp <= 0)
                state.IsHpFailed = true;
        }

        resolveActiveState();
        markEverFailedIfEmpty();
        recordGaugeHistory(eventTime ?? currentTime);
    }

    /// <summary>
    ///     Applies a health change from a synthetic long-note endpoint
    ///     (CN/HCN tail), using the same judgement/health pipeline as normal results.
    /// </summary>
    public void ApplySyntheticLongNoteEndpoint(JudgementResult result)
    {
        ApplyResult(result);
    }

    /// <summary>
    ///     Applies a HellChargeNote body tick at ~200 ms cadence.
    ///     The default scale applies half-GREAT gauge recovery or half-BAD gauge damage.
    ///     Does not add judgement count, combo, or score.
    /// </summary>
    public void ApplyHellChargeTick(bool holding, double scale = 0.5, double? eventTime = null)
    {
        ensureInitialized();
        syncActiveStateFromHealth();

        var type = holding ? HitResult.Great : HitResult.Ok;

        for (var i = 0; i < gaugeStates.Count; i++)
        {
            var state = gaugeStates[i];
            if (state.IsHpFailed) continue;

            var delta = state.Calculator!.GetDeltaFor(type, state.CurrentHp) * scale;
            state.CurrentHp = state.Calculator.ApplyDelta(state.CurrentHp, delta);

            if (state.CurrentHp <= 0)
                state.IsHpFailed = true;
        }

        resolveActiveState();
        markEverFailedIfEmpty();
        recordGaugeHistory(eventTime ?? currentTime);
    }

    public void SetGaugeType(BmsGaugeType gaugeType, BmsGaugeProfileFamily? profileFamilyOverride = null)
    {
        // In multi-gauge (auto-gauge) mode, a duplicate type means replay dedup — skip.
        if (gaugeStates.Count > 1 && gaugeStates.Any(s => s.GaugeType == gaugeType))
            return;

        // Single-gauge mode: replace the entire chain so that switching from
        // Hard back to Normal (and similar transitions) works correctly.
        gaugeStates.Clear();
        SetGaugeTypes([gaugeType], profileFamilyOverride: profileFamilyOverride);
    }

    /// <summary>
    /// Sets multiple gauge types to track in parallel, sorted by difficulty descending.
    /// Types already present in the chain are skipped (dedup).
    /// </summary>
    public void SetGaugeTypes(IEnumerable<BmsGaugeType> types, bool replaceExisting = false, BmsGaugeProfileFamily? profileFamilyOverride = null)
    {
        if (replaceExisting)
            gaugeStates.Clear();

        if (replaceExisting || gaugeStates.Count == 0)
            this.profileFamilyOverride = profileFamilyOverride;

        var unique = new HashSet<BmsGaugeType>();
        var newStates = new List<GaugeState>();

        foreach (var type in types)
        {
            if (!unique.Add(type))
                continue;

            // Skip if already in the chain (dedup during replay).
            if (gaugeStates.Any(s => s.GaugeType == type))
                continue;

            var profile = BmsGaugeProfileFactory.Create(type, effectiveProfileFamily);
            newStates.Add(new GaugeState
            {
                GaugeType = type,
                Profile = profile,
                CurrentHp = profile.InitialHealth,
            });
        }

        if (newStates.Count == 0)
            return;

        gaugeStates.AddRange(newStates);
        gaugeStates.Sort((a, b) => ((int)b.GaugeType).CompareTo((int)a.GaugeType));

        // Sync to active state (first = hardest).
        activeGaugeIndex = 0;
        endResultIndex = 0;
        var active = gaugeStates[0];
        GaugeType = active.GaugeType;
        GaugeProfile = active.Profile;
        DisplayProfile.Value = active.Profile.Display;
        Health.MaxValue = active.Profile.MaxHealth;
        Health.Value = active.CurrentHp;

        initialized = false;

    }

    public void RestoreGaugeStates(IReadOnlyList<BmsGaugeStateSnapshot> states)
    {
        ensureInitialized();

        foreach (var state in gaugeStates)
        {
            var restored = states.FirstOrDefault(snapshot => snapshot.GaugeType == state.GaugeType);
            if (restored is null)
                continue;

            state.CurrentHp = Math.Clamp(restored.Health, 0, state.Profile.MaxHealth);
            state.IsHpFailed = restored.Failed || state.CurrentHp <= 0;
        }

        activeGaugeIndex = 0;
        HasEverFailed = false;
        resolveActiveState();
    }

    public bool HasPassedAtEnd()
    {
        if (HasEverFailed)
            return false;

        for (var i = activeGaugeIndex; i < gaugeStates.Count; i++)
        {
            var state = gaugeStates[i];
            if (state.IsHpFailed)
                continue;

            if (state.Profile.ClearThreshold <= 0 || state.CurrentHp >= state.Profile.ClearThreshold)
            {
                endResultIndex = i;
                return true;
            }
        }

        endResultIndex = gaugeStates.Count > 0 ? gaugeStates.Count - 1 : 0;
        return false;
    }

    protected override void Reset(bool storeResults)
    {
        base.Reset(storeResults);
        initialized = false;
        Health.MaxValue = GaugeProfile.MaxHealth;
        Health.Value = GaugeProfile.InitialHealth;
        HasEverFailed = false;
        gaugeHistory.Clear();

        // Reset all gauge states to their initial values for a fresh play.
        foreach (var state in gaugeStates)
        {
            state.CurrentHp = state.Profile.InitialHealth;
            state.IsHpFailed = false;
        }

        activeGaugeIndex = 0;
        endResultIndex = 0;
    }

    protected override void ApplyResultInternal(JudgementResult result)
    {
        base.ApplyResultInternal(result);

        if (!HasEverFailed && Health.Value <= 0)
            HasEverFailed = true;

        recordGaugeHistory(result.TimeAbsolute);
    }

    protected override HitResult GetSimulatedHitResult(Judgement judgement) => judgement.MaxResult == HitResult.Meh
        ? HitResult.IgnoreMiss
        : base.GetSimulatedHitResult(judgement);

    protected override double GetHealthIncreaseFor(JudgementResult result)
    {
        ensureInitialized();
        syncActiveStateFromHealth();

        // The framework captures Health.Value BEFORE calling this method,
        // then does Health.Value = capturedOldValue + this_return_value.
        // We must return the net delta so the framework's arithmetic arrives
        // at the correct value.
        var oldHealth = Health.Value;

        if (result.HitObject is BmsLandmine mine)
        {
            if (result.Type != HitResult.Meh)
                return 0;

            // z.z landmine: instant-kill all layers.
            if (mine.LandmineDamagePercent >= max_landmine_damage_percent)
            {
                foreach (var state in gaugeStates)
                {
                    state.CurrentHp = 0;
                    state.IsHpFailed = true;
                }

                resolveActiveState();
                return Health.Value - oldHealth;
            }

            // Regular landmine: apply damage fraction to all non-failed states.
            var damageFraction = mine.LandmineDamagePercent / 100.0;
            foreach (var state in gaugeStates)
            {
                if (state.IsHpFailed) continue;

                state.CurrentHp = Math.Max(0, state.CurrentHp - damageFraction);
                if (state.CurrentHp <= 0)
                    state.IsHpFailed = true;
            }

            resolveActiveState();
            return Health.Value - oldHealth;
        }

        // Normal note: each non-failed state computes its own delta independently.
        for (var i = 0; i < gaugeStates.Count; i++)
        {
            var state = gaugeStates[i];
            if (state.IsHpFailed) continue;

            var delta = state.Calculator!.GetDeltaFor(result.Type, state.CurrentHp);
            state.CurrentHp = state.Calculator.ApplyDelta(state.CurrentHp, delta);

            if (state.CurrentHp <= 0)
                state.IsHpFailed = true;
        }

        resolveActiveState();
        return Health.Value - oldHealth;
    }

    private void markEverFailedIfEmpty()
    {
        if (Health.Value > 0)
            return;

        HasEverFailed = true;
        TriggerFailure();
    }

    private void syncActiveStateFromHealth()
    {
        if (activeGaugeIndex < gaugeStates.Count)
        {
            var active = gaugeStates[activeGaugeIndex];
            if (!active.IsHpFailed)
                active.CurrentHp = Health.Value;
        }
    }

    private void ensureInitialized()
    {
        if (initialized) return;

        initialized = true;

        // When no gauge types have been set explicitly, default to the current
        // GaugeType (Normal) so that health calculations always have a state.
        if (gaugeStates.Count == 0)
        {
            gaugeStates.Add(new GaugeState
            {
                GaugeType = GaugeType,
                Profile = GaugeProfile,
                CurrentHp = GaugeProfile.InitialHealth,
            });
        }

        var noteCount = beatmap?.HitObjects.Count(h => h is not BmsLandmine) ?? 0;
        if (noteCount == 0) noteCount = 1;

        double total = 0;
        if (beatmap is BmsBeatmap bmsBeatmap)
            total = bmsBeatmap.Total;

        if (total <= 0)
            total = BmsGaugeCalculator.CalculateDefaultTotal(noteCount, effectiveProfileFamily);

        foreach (var state in gaugeStates)
        {
            state.Calculator = new BmsGaugeCalculator(state.Profile, total, noteCount, effectiveProfileFamily);
        }
    }

    private BmsGaugeProfileFamily effectiveProfileFamily => profileFamilyOverride ?? layoutProfileFamily;

    private void refreshGaugeProfiles()
    {
        if (gaugeStates.Count == 0)
            return;

        foreach (var state in gaugeStates)
        {
            state.Profile = BmsGaugeProfileFactory.Create(state.GaugeType, effectiveProfileFamily);
            state.Calculator = null;
            state.CurrentHp = state.Profile.InitialHealth;
            state.IsHpFailed = false;
        }

        activeGaugeIndex = 0;
        endResultIndex = 0;
        initialized = false;
        resolveActiveState();
    }

    private void resolveActiveState()
    {
        while (activeGaugeIndex < gaugeStates.Count && gaugeStates[activeGaugeIndex].IsHpFailed)
            activeGaugeIndex++;

        if (activeGaugeIndex >= gaugeStates.Count)
        {
            Health.Value = 0;
            return;
        }

        var active = gaugeStates[activeGaugeIndex];
        GaugeType = active.GaugeType;
        GaugeProfile = active.Profile;
        DisplayProfile.Value = active.Profile.Display;
        Health.MaxValue = active.Profile.MaxHealth;
        Health.Value = active.CurrentHp;
    }

    private void recordGaugeHistory(double eventTime)
    {
        if (gaugeStates.Count == 0)
            return;

        var activeGaugeType = activeGaugeIndex < gaugeStates.Count
            ? gaugeStates[activeGaugeIndex].GaugeType
            : gaugeStates[^1].GaugeType;

        gaugeHistory.Add(new BmsGaugeHistoryEvent(
            eventTime,
            activeGaugeType,
            gaugeStates.Select(state => new BmsGaugeStateSnapshot(
                state.GaugeType,
                state.CurrentHp,
                state.IsHpFailed)).ToArray()));
    }

    private double currentTime => Clock?.CurrentTime ?? 0;

    private sealed class GaugeState
    {
        public BmsGaugeType GaugeType;
        public BmsGaugeProfile Profile = null!;
        public BmsGaugeCalculator? Calculator;
        public double CurrentHp;
        public bool IsHpFailed;
    }
}
