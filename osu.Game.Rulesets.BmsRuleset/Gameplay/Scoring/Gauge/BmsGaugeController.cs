using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Bms;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;

internal sealed class BmsGaugeController : GameplayHealth
{
    private HealthState healthState = new(0, false);

    internal BmsGaugeController(BindableDouble health, BmsGameplayProfile profile)
        : base(health)
    {
        gameplayProfile = profile;
        GaugeProfile = createProfile(GaugeType);
        DisplayProfile.Value = BmsGaugeProfileFactory.CreateDisplay(GaugeType, GaugeProfile);
    }

    internal override HealthConfiguration CreateConfiguration(BmsEngineChart compiled)
    {
        ensureGaugeStates();
        captureInitialGaugeStates();
        var noteCount = compiled.Chart.ScorePlan.Opportunities.Length;
        var total = (beatmap as BmsBeatmap)?.Total;
        var fullNoteCount = (beatmap as BmsBeatmap)?.PracticeFullScoringOpportunities ?? noteCount;
        var calculators = gaugeStates.Select(state => new BmsGaugeCalculator(state.Profile, total, noteCount, effectiveProfileFamily, fullNoteCount))
            .ToImmutableArray();
        return BmsEngineGauge.Create(compiled.Objects, calculators,
            healthState.Layers);
    }

    internal override void Apply(HealthState health)
    {
        healthState = health;
        resolveActiveState();
    }

    /// <summary>
    /// The gauge type that ultimately determined pass/fail at song end.
    /// </summary>
    public BmsGaugeType WorstGaugeType =>
        gaugeStates.Count > 0 && endResultIndex < gaugeStates.Count
            ? gaugeStates[endResultIndex].GaugeType
            : BmsGaugeType.Normal;

    public BmsGaugeType GaugeType { get; private set; } = BmsGaugeType.Normal;

    public BmsGaugeProfile GaugeProfile { get; private set; }

    public IReadOnlyList<BmsGaugeStateSnapshot> CurrentGaugeStates => snapshotGaugeStates();

    internal override GameplayHealthHistoryEvent CaptureHistory(double time, bool cleared)
    {
        var health = CurrentState;
        var selected = BmsGaugeHealth.FindClearedLayer([.. gaugeStates.Select(state => state.Profile)], health.Layers);
        return new GameplayHealthHistoryEvent(
            time,
            health,
            [
                .. gaugeStates.Select((state, index) =>
                    new HealthHistoryLayer(
                        BmsGaugeProfileFactory.NameFor(state.GaugeType),
                        BmsGaugeProfileFactory.HistoryColourFor(state.GaugeType),
                        health.Layers[index].Value / state.Profile.MaxHealth,
                        health.Layers[index].HasFailed,
                        state.Profile.IsCleared(health.Layers[index]))),
            ],
            selected >= 0 ? selected : gaugeStates.Count - 1);
    }

    internal override HealthState CurrentState => gaugeStates.Count == 0
        ? new HealthState(Health.Value, false)
        : healthState;

    internal bool IsCourseGaugeMode { get; private set; }

    internal BmsGaugeProfileFamily? ConfiguredProfileFamily { get; private set; }

    private IReadOnlyList<BmsGaugeStateSnapshot>? replayInitialStates;

    internal override void ResetReplayState()
    {
        // Course stages and configurable initial health need the original seed, not profile defaults.
        ensureGaugeStates();
        replayInitialStates ??= initialGaugeStates ?? CurrentGaugeStates;
        Reset();
        RestoreGaugeStates(replayInitialStates);
        initialGaugeStates = replayInitialStates;
    }

    private readonly List<GaugeState> gaugeStates = [];
    private IReadOnlyList<BmsGaugeStateSnapshot>? initialGaugeStates;

    private int activeGaugeIndex;
    private int endResultIndex;

    private IBeatmap? beatmap;
    private BmsGaugeProfileFamily layoutProfileFamily = BmsGaugeProfileFamily.SevenKeys;
    private BmsGameplayProfile gameplayProfile;

    internal override void ApplyProfile(GameplayProfile profile)
    {
        if (ReferenceEquals(gameplayProfile, profile))
            return;

        gameplayProfile = (BmsGameplayProfile)profile;
        var layers = healthState.Layers.ToBuilder();

        for (var i = 0; i < gaugeStates.Count; i++)
        {
            var state = gaugeStates[i];
            var layer = healthState.Layers[i];
            var defaultInitialHealth = initialGaugeStates == null && !state.HasExplicitInitialHealth && layer.Value == state.Profile.InitialHealth;
            state.Profile = createProfile(state.GaugeType);
            if (defaultInitialHealth)
                layer = layer with { Value = state.Profile.InitialHealth };
            layers[i] = layer with { HasFailed = layer.HasFailed || state.Profile.IsFailed(layer.Value) };
        }

        if (gaugeStates.Count > 0)
        {
            healthState = BmsGaugeHealth.Combine(layers.ToImmutable());
            resolveActiveState();
        }
        else
        {
            updateGaugeProfile(createProfile(GaugeType));
        }
    }

    private BmsGaugeProfile createProfile(BmsGaugeType type) => gameplayProfile.CreateGaugeProfile(type, effectiveProfileFamily);

    internal override void ApplyBeatmap(IBeatmap beatmap)
    {
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

        var states = gaugeStates.Select((state, index) => (Configuration: state, Health: healthState.Layers[index])).ToList();
        var existingCount = states.Count;

        foreach (var type in types)
        {
            // Skip if already in the chain (dedup during replay).
            if (states.Any(state => state.Configuration.GaugeType == type))
                continue;

            var profile = createProfile(type);
            var initialState = initialStates?.FirstOrDefault(state => state.GaugeType == type);
            var initialHealth = Math.Clamp(initialState?.Health ?? profile.InitialHealth, 0, profile.MaxHealth);
            states.Add((new GaugeState
            {
                GaugeType = type,
                Profile = profile,
                HasExplicitInitialHealth = initialState != null,
            }, new HealthState(initialHealth, initialState?.Failed == true || profile.IsFailed(initialHealth))));
        }

        if (states.Count == existingCount)
            return;

        replayInitialStates = null;
        states.Sort((a, b) => ((int)b.Configuration.GaugeType).CompareTo((int)a.Configuration.GaugeType));
        gaugeStates.Clear();
        gaugeStates.AddRange(states.Select(state => state.Configuration));
        healthState = BmsGaugeHealth.Combine([.. states.Select(state => state.Health)]);

        // Sync to active state (first = hardest).
        activeGaugeIndex = 0;
        endResultIndex = 0;
        resolveActiveState();
    }

    public void RestoreGaugeStates(IReadOnlyList<BmsGaugeStateSnapshot> states)
    {
        ensureGaugeStates();
        var layers = healthState.Layers.ToBuilder();

        for (var i = 0; i < gaugeStates.Count; i++)
        {
            var state = gaugeStates[i];
            var restored = states.FirstOrDefault(snapshot => snapshot.GaugeType == state.GaugeType);
            if (restored is null)
                continue;

            var health = Math.Clamp(restored.Health, 0, state.Profile.MaxHealth);
            state.HasExplicitInitialHealth = true;
            layers[i] = new HealthState(health, restored.Failed || state.Profile.IsFailed(health));
        }

        healthState = BmsGaugeHealth.Combine(layers.ToImmutable());
        activeGaugeIndex = 0;
        resolveActiveState();
    }

    internal override void ResolveEndResult()
    {
        var cleared = BmsGaugeHealth.FindClearedLayer([.. gaugeStates.Select(state => state.Profile)], healthState.Layers);
        endResultIndex = cleared >= 0 ? cleared : Math.Max(0, gaugeStates.Count - 1);
    }

    internal override void Reset()
    {
        Health.MaxValue = GaugeProfile.MaxHealth;
        Health.Value = GaugeProfile.InitialHealth;
        initialGaugeStates = null;

        var layers = ImmutableArray.CreateBuilder<HealthState>(gaugeStates.Count);

        foreach (var state in gaugeStates)
        {
            state.HasExplicitInitialHealth = false;
            layers.Add(new HealthState(state.Profile.InitialHealth, false));
        }

        healthState = BmsGaugeHealth.Combine(layers.ToImmutable());
        activeGaugeIndex = 0;
        endResultIndex = 0;

        if (gaugeStates.Count > 0)
        {
            GaugeType = gaugeStates[0].GaugeType;
            updateGaugeProfile(gaugeStates[0].Profile);
            Health.Value = healthState.Layers[0].Value;
        }
    }

    private void captureInitialGaugeStates() => initialGaugeStates ??= CurrentGaugeStates;

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
            });
            healthState = BmsGaugeHealth.Combine([new HealthState(GaugeProfile.InitialHealth, false)]);
        }
    }

    private BmsGaugeProfileFamily effectiveProfileFamily => ConfiguredProfileFamily ?? layoutProfileFamily;

    private void refreshGaugeProfiles()
    {
        if (gaugeStates.Count == 0)
        {
            updateGaugeProfile(createProfile(GaugeType));
            Health.Value = GaugeProfile.InitialHealth;
            return;
        }

        var layers = ImmutableArray.CreateBuilder<HealthState>(gaugeStates.Count);

        foreach (var state in gaugeStates)
        {
            state.Profile = createProfile(state.GaugeType);
            state.HasExplicitInitialHealth = false;
            layers.Add(new HealthState(state.Profile.InitialHealth, false));
        }

        healthState = BmsGaugeHealth.Combine(layers.ToImmutable());
        activeGaugeIndex = 0;
        endResultIndex = 0;
        resolveActiveState();
    }

    private void resolveActiveState()
    {
        while (activeGaugeIndex < gaugeStates.Count && healthState.Layers[activeGaugeIndex].HasFailed)
            activeGaugeIndex++;

        if (activeGaugeIndex >= gaugeStates.Count)
        {
            Health.Value = 0;
            return;
        }

        var active = gaugeStates[activeGaugeIndex];
        GaugeType = active.GaugeType;
        updateGaugeProfile(active.Profile);
        Health.Value = healthState.Layers[activeGaugeIndex].Value;
    }

    private void updateGaugeProfile(BmsGaugeProfile profile)
    {
        GaugeProfile = profile;
        DisplayProfile.Value = BmsGaugeProfileFactory.CreateDisplay(GaugeType, profile);
        Health.MaxValue = profile.MaxHealth;
    }

    private BmsGaugeStateSnapshot[] snapshotGaugeStates()
    {
        var snapshots = new BmsGaugeStateSnapshot[gaugeStates.Count];

        for (var i = 0; i < gaugeStates.Count; i++)
        {
            var state = gaugeStates[i];
            var layer = healthState.Layers[i];
            snapshots[i] = new BmsGaugeStateSnapshot(state.GaugeType, layer.Value, layer.HasFailed);
        }

        return snapshots;
    }

    private sealed class GaugeState
    {
        public BmsGaugeType GaugeType;
        public BmsGaugeProfile Profile = null!;
        public bool HasExplicitInitialHealth;
    }
}
