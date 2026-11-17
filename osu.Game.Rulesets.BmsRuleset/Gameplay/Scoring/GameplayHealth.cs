using RhythmEngine.Core;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;

/// <summary>Projects profile health into the HUD and retains the seed used when rebuilding an attempt.</summary>
internal class GameplayHealth(BindableDouble health)
{
    protected BindableDouble Health { get; } = health;

    internal Bindable<BmsGaugeDisplayProfile> DisplayProfile { get; } = new(new BmsGaugeDisplayProfile(null, false, BmsGaugeColourMode.Fixed, Color4.White));

    private HealthState initialState;
    private HealthState state;

    internal virtual HealthState CurrentState => state;

    internal virtual GameplayHealthHistoryEvent CaptureHistory(double time, bool cleared)
        => new(time, state, [new(BmsStrings.Health, DisplayProfile.Value.FillColour, state.Value, state.HasFailed, cleared)], 0);

    internal virtual HealthConfiguration CreateConfiguration(BmsEngineChart compiled)
    {
        initialState = compiled.Configuration.Health.InitialState;
        return compiled.Configuration.Health;
    }

    internal virtual void Apply(HealthState state)
    {
        this.state = state;
        Health.Value = state.Value;
    }

    internal virtual void Reset() => Apply(initialState);

    internal virtual void ResetReplayState() => Reset();

    internal virtual void ApplyBeatmap(IBeatmap beatmap)
    {
    }

    internal virtual void ApplyProfile(GameplayProfile profile)
    {
    }

    internal virtual void ResolveEndResult()
    {
    }
}
