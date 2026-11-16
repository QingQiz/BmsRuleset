using System;
using System.Collections.Generic;
using RhythmEngine.Core;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using JudgementResult = osu.Game.Rulesets.Judgements.JudgementResult;
using EngineResult = RhythmEngine.Core.JudgementResult;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;

public partial class BmsHealthProcessor : HealthProcessor
{
    private GameplayProfile profile = BmsGameplayProfile.INSTANCE;
    private HealthConfiguration? configuration;
    private ScorePlan plan = null!;
    private ScoreState score = new();

    internal IBeatmap? Beatmap { get; private set; }
    internal GameplayHealth Model { get; private set; }
    internal SessionOutcome Outcome { get; private set; }
    internal HealthState CurrentHealth => Model.CurrentState;

    internal GameplayHealthHistoryEvent? CaptureHistory(double time)
    {
        // The session is created lazily after gameplay and replay settings have been applied.
        if (configuration == null)
            return null;
        var health = CurrentHealth;
        var cleared = configuration.EvaluateOutcome(new SessionOutcomeContext(true, health, score, plan)) == SessionOutcome.Cleared;
        return Model.CaptureHistory(time, cleared);
    }

    public Bindable<BmsGaugeDisplayProfile> DisplayProfile { get; } = new();

    public BmsHealthProcessor()
    {
        Model = profile.CreateHealthModel(Health);
        DisplayProfile.BindTo(Model.DisplayProfile);
    }

    public IReadOnlyList<Mod> Mods
    {
        get;
        set
        {
            field = value;
            configuration = null;
            var selected = GameplayProfile.FromMods(value);
            // Mod options reconfigure the current model; only a different profile family changes its lifecycle.
            if (profile.GetType() != selected.GetType())
                replaceModel(selected);
            profile = selected;
            Model.ApplyProfile(selected);
        }
    } = [];

    internal void ApplyReplayMods(IReadOnlyList<Mod> mods)
    {
        // A replacement replay owns a new seed, including when it selects the same gauge types.
        replaceModel(GameplayProfile.FromMods(mods));
        Mods = mods;
    }

    private void replaceModel(GameplayProfile selected)
    {
        profile = selected;
        DisplayProfile.UnbindFrom(Model.DisplayProfile);
        Model = profile.CreateHealthModel(Health);
        DisplayProfile.BindTo(Model.DisplayProfile);
        if (Beatmap != null)
            Model.ApplyBeatmap(Beatmap);
    }

    public override void ApplyBeatmap(IBeatmap beatmap)
    {
        configuration = null;
        Beatmap = beatmap;
        base.ApplyBeatmap(beatmap);
        Model.ApplyBeatmap(beatmap);
    }

    internal HealthConfiguration CreateEngineHealth(BmsEngineChart compiled)
    {
        plan = compiled.Chart.ScorePlan;
        configuration = Model.CreateConfiguration(compiled);
        Model.Apply(configuration.InitialState);
        return configuration;
    }

    internal void ApplyEngineState(JudgementSession session)
    {
        apply(session.Health, session.Score);
        Outcome = session.Outcome;
        if (!session.Completed && Outcome == SessionOutcome.Failed)
            TriggerFailure();
    }

    internal void ApplyEngineTick(EngineResult result)
    {
        apply(result.Health, result.Score);
        if (Outcome == SessionOutcome.Failed)
            TriggerFailure();
    }

    private void apply(HealthState health, ScoreState engineScore)
    {
        score = engineScore;
        Model.Apply(health);
        Outcome = configuration!.EvaluateOutcome(new SessionOutcomeContext(false, health, score, plan));
    }

    public bool HasPassedAtEnd()
    {
        Model.ResolveEndResult();
        Outcome = configuration!.EvaluateOutcome(new SessionOutcomeContext(true, CurrentHealth, score, plan));
        return Outcome == SessionOutcome.Cleared;
    }

    internal void ResetReplayState()
    {
        base.Reset(false);
        Model.ResetReplayState();
        Outcome = SessionOutcome.InProgress;
    }

    protected override void Reset(bool storeResults)
    {
        base.Reset(storeResults);
        Model.Reset();
        Outcome = SessionOutcome.InProgress;
    }

    protected override bool CheckDefaultFailCondition(JudgementResult result) => Outcome == SessionOutcome.Failed;

    protected override void RevertResultInternal(JudgementResult result)
        => throw new NotSupportedException("Rewind must rebuild the engine session from recorded inputs.");

    protected override double GetHealthIncreaseFor(JudgementResult result)
    {
        if (IsSimulating)
            return 0;
        if (result is not BmsJudgementResult { EngineResult: { } engine })
            throw new InvalidOperationException("Gameplay health requires an engine judgement.");
        var previous = Health.Value;
        apply(engine.Health, engine.Score);
        return Health.Value - previous;
    }
}
