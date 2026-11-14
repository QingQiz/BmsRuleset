using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using RhythmEngine.Core;
using JudgementResult = osu.Game.Rulesets.Judgements.JudgementResult;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;

public partial class BmsHealthProcessor : HealthProcessor
{
    internal HealthConfiguration CreateEngineHealth(BmsHitObject[] objects)
    {
        ensureGaugeStates();
        syncActiveStateFromHealth();
        captureInitialGaugeStates();
        var noteCount = Math.Max(1, beatmap?.CountScoringNotes() ?? 0);
        var total = (beatmap as BmsBeatmap)?.Total;
        var fullNoteCount = beatmap is BmsBeatmap { PracticeFullNoteCount: { } fullCount } ? Math.Max(1, fullCount) : noteCount;
        var calculators = gaugeStates.Select(state =>
        {
            var profile = state.Profile with { GutsRules = [.. state.Profile.GutsRules] };
            var effectiveTotal = BmsGaugeCalculator.ResolveTotal(profile, total, fullNoteCount, effectiveProfileFamily);
            // Mixed Auto Gauge chains need full-chart survival settings and proportional recovery TOTAL.
            var calculatorTotal = profile.Algorithm == BmsGaugeAlgorithm.Total ? effectiveTotal * noteCount / fullNoteCount : effectiveTotal;
            var calculatorNoteCount = profile.Algorithm == BmsGaugeAlgorithm.Total ? noteCount : fullNoteCount;
            return new BmsGaugeCalculator(profile, calculatorTotal, calculatorNoteCount, effectiveProfileFamily);
        }).ToImmutableArray();
        return BmsEngineGauge.Create(objects, calculators,
            [.. gaugeStates.Select(state => new HealthState(state.CurrentHp, state.IsHpFailed))]);
    }

    internal void ApplyEngineHealth(HealthState health)
    {
        for (var i = 0; i < gaugeStates.Count; i++)
        {
            gaugeStates[i].CurrentHp = health.Layers[i].Value;
            gaugeStates[i].IsHpFailed = health.Layers[i].HasFailed;
        }

        resolveActiveState();
        HasEverFailed |= health.HasFailed;
    }

    internal void ApplyEngineTick(HealthState health)
    {
        ApplyEngineHealth(health);
        markEverFailedIfEmpty();
    }

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
    /// Retains zero-HP failure for end-of-song validation even if HP later recovers.
    /// </summary>
    public bool HasEverFailed { get; private set; }

    public BmsGaugeType GaugeType { get; private set; } = BmsGaugeType.Normal;

    public BmsGaugeProfile GaugeProfile { get; private set; } = BmsGaugeProfileFactory.Create(BmsGaugeType.Normal);

    public IReadOnlyList<BmsGaugeStateSnapshot> CurrentGaugeStates => snapshotGaugeStates();

    internal bool IsCourseGaugeMode { get; private set; }

    internal BmsGaugeProfileFamily? ConfiguredProfileFamily { get; private set; }

    private IReadOnlyList<BmsGaugeStateSnapshot>? replayInitialStates;

    internal void ResetReplayState()
    {
        // Course stages and configurable initial health need the original seed, not profile defaults.
        ensureGaugeStates();
        syncActiveStateFromHealth();
        replayInitialStates ??= initialGaugeStates ?? CurrentGaugeStates;
        Reset(false);
        RestoreGaugeStates(replayInitialStates);
        initialGaugeStates = replayInitialStates;
    }

    private readonly List<GaugeState> gaugeStates = [];
    private IReadOnlyList<BmsGaugeStateSnapshot>? initialGaugeStates;

    private int activeGaugeIndex;
    private int endResultIndex;

    private IBeatmap? beatmap;
    private BmsGaugeProfileFamily layoutProfileFamily = BmsGaugeProfileFamily.SevenKeys;
    private IApplicableToGaugeProfile[] profileMods = [];

    public IReadOnlyList<Mod> Mods
    {
        get;
        set
        {
            if (ReferenceEquals(field, value))
                return;

            syncActiveStateFromHealth();
            field = value;
            profileMods = value.OfType<IApplicableToGaugeProfile>().ToArray();

            foreach (var state in gaugeStates)
            {
                var defaultInitialHealth = initialGaugeStates == null && !state.HasExplicitInitialHealth
                                                                      && state.CurrentHp == state.Profile.InitialHealth;
                state.Profile = createProfile(state.GaugeType);
                if (defaultInitialHealth)
                    state.CurrentHp = state.Profile.InitialHealth;
                state.IsHpFailed |= state.Profile.IsFailed(state.CurrentHp);
            }

            if (gaugeStates.Count > 0)
                resolveActiveState();
            else
            {
                GaugeProfile = createProfile(GaugeType);
                DisplayProfile.Value = GaugeProfile.Display;
                Health.MaxValue = GaugeProfile.MaxHealth;
            }
        }
    } = [];

    private BmsGaugeProfile createProfile(BmsGaugeType type)
    {
        var profile = BmsGaugeProfileFactory.Create(type, effectiveProfileFamily);
        foreach (var mod in profileMods)
            profile = mod.ApplyToGaugeProfile(type, profile);
        return profile;
    }

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

    public void SetGaugeType(BmsGaugeType gaugeType, BmsGaugeProfileFamily? profileFamilyOverride = null)
    {
        if (gaugeStates.Count == 1
            && gaugeStates[0].GaugeType == gaugeType
            && ConfiguredProfileFamily == profileFamilyOverride)
            return;

        // In multi-gauge (auto-gauge) mode, a duplicate type means replay dedup — skip.
        if (gaugeStates.Count > 1 && gaugeStates.Any(s => s.GaugeType == gaugeType))
            return;

        // Single-gauge mode: replace the entire chain so that switching from
        // Hard back to Normal (and similar transitions) works correctly.
        gaugeStates.Clear();
        SetGaugeTypes([gaugeType], profileFamilyOverride: profileFamilyOverride);
    }

    internal void ConfigureGaugeContext(bool isCourseGaugeMode, BmsGaugeProfileFamily? familyOverride)
    {
        IsCourseGaugeMode = isCourseGaugeMode;
        ConfiguredProfileFamily = familyOverride;
    }

    /// <summary>
    /// Sets multiple gauge types to track in parallel, sorted by difficulty descending.
    /// Types already present in the chain are skipped (dedup).
    /// </summary>
    public void SetGaugeTypes(
        IEnumerable<BmsGaugeType> types,
        bool replaceExisting = false,
        BmsGaugeProfileFamily? profileFamilyOverride = null,
        IReadOnlyList<BmsGaugeStateSnapshot>? initialStates = null)
    {
        if (replaceExisting)
            gaugeStates.Clear();

        if (replaceExisting || gaugeStates.Count == 0)
            ConfiguredProfileFamily = profileFamilyOverride;

        var unique = new HashSet<BmsGaugeType>();
        var newStates = new List<GaugeState>();

        foreach (var type in types)
        {
            if (!unique.Add(type))
                continue;

            // Skip if already in the chain (dedup during replay).
            if (gaugeStates.Any(s => s.GaugeType == type))
                continue;

            var profile = createProfile(type);
            var initialState = initialStates?.FirstOrDefault(state => state.GaugeType == type);
            newStates.Add(new GaugeState
            {
                GaugeType = type,
                Profile = profile,
                CurrentHp = Math.Clamp(initialState?.Health ?? profile.InitialHealth, 0, profile.MaxHealth),
                HasExplicitInitialHealth = initialState != null,
                IsHpFailed = initialState?.Failed == true || profile.IsFailed(initialState?.Health ?? profile.InitialHealth),
            });
        }

        if (newStates.Count == 0)
            return;

        replayInitialStates = null;
        gaugeStates.AddRange(newStates);
        gaugeStates.Sort((a, b) => ((int)b.GaugeType).CompareTo((int)a.GaugeType));

        // Sync to active state (first = hardest).
        activeGaugeIndex = 0;
        endResultIndex = 0;
        resolveActiveState();
    }

    public void RestoreGaugeStates(IReadOnlyList<BmsGaugeStateSnapshot> states)
    {
        ensureGaugeStates();

        foreach (var state in gaugeStates)
        {
            var restored = states.FirstOrDefault(snapshot => snapshot.GaugeType == state.GaugeType);
            if (restored is null)
                continue;

            state.CurrentHp = Math.Clamp(restored.Health, 0, state.Profile.MaxHealth);
            state.HasExplicitInitialHealth = true;
            state.IsHpFailed = restored.Failed || state.Profile.IsFailed(state.CurrentHp);
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
        Health.MaxValue = GaugeProfile.MaxHealth;
        Health.Value = GaugeProfile.InitialHealth;
        HasEverFailed = false;
        initialGaugeStates = null;

        // Reset all gauge states to their initial values for a fresh play.
        foreach (var state in gaugeStates)
        {
            state.CurrentHp = state.Profile.InitialHealth;
            state.HasExplicitInitialHealth = false;
            state.IsHpFailed = false;
        }

        activeGaugeIndex = 0;
        endResultIndex = 0;
    }

    protected override void ApplyResultInternal(JudgementResult result)
    {
        ensureGaugeStates();
        syncActiveStateFromHealth();
        captureInitialGaugeStates();
        base.ApplyResultInternal(result);

        if (!HasEverFailed && Health.Value <= 0)
            HasEverFailed = true;
    }

    // Gauge layers deplete at exact zero; the framework's epsilon can fail AG before its active layer is replaced.
    protected override bool CheckDefaultFailCondition(JudgementResult result) => Health.Value <= 0;

    private void captureInitialGaugeStates() => initialGaugeStates ??= CurrentGaugeStates;

    protected override HitResult GetSimulatedHitResult(Judgements.Judgement judgement) => judgement.MaxResult == HitResult.Meh
        ? HitResult.IgnoreMiss
        : base.GetSimulatedHitResult(judgement);

    protected override double GetHealthIncreaseFor(JudgementResult result)
    {
        if (IsSimulating)
            return 0;

        if (result is not BmsJudgementResult { EngineResult: { } engine })
            throw new InvalidOperationException("Gameplay health requires an engine judgement.");

        var previous = Health.Value;
        ApplyEngineHealth(engine.Health);
        return Health.Value - previous;
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

    private void ensureGaugeStates()
    {
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
    }

    private BmsGaugeProfileFamily effectiveProfileFamily => ConfiguredProfileFamily ?? layoutProfileFamily;

    private void refreshGaugeProfiles()
    {
        if (gaugeStates.Count == 0)
            return;

        foreach (var state in gaugeStates)
        {
            state.Profile = createProfile(state.GaugeType);
            state.CurrentHp = state.Profile.InitialHealth;
            state.HasExplicitInitialHealth = false;
            state.IsHpFailed = false;
        }

        activeGaugeIndex = 0;
        endResultIndex = 0;
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

    private BmsGaugeStateSnapshot[] snapshotGaugeStates()
    {
        var snapshots = new BmsGaugeStateSnapshot[gaugeStates.Count];

        for (var i = 0; i < gaugeStates.Count; i++)
        {
            var state = gaugeStates[i];
            snapshots[i] = new BmsGaugeStateSnapshot(state.GaugeType, state.CurrentHp, state.IsHpFailed);
        }

        return snapshots;
    }

    private sealed class GaugeState
    {
        public BmsGaugeType GaugeType;
        public BmsGaugeProfile Profile = null!;
        public double CurrentHp;
        public bool HasExplicitInitialHealth;
        public bool IsHpFailed;
    }
}
