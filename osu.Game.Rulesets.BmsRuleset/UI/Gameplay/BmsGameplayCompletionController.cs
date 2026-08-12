using System;
using System.Linq;
using osu.Framework.Bindables;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal sealed class BmsGameplayCompletionController : IDisposable
{
    private readonly ScoreProcessor scoreProcessor;
    private readonly HealthProcessor healthProcessor;
    private readonly GameplayState gameplayState;
    private readonly Score? replayScore;
    private readonly BmsRulesetConfigManager? config;

    internal BmsGameplayCompletionController(
        ScoreProcessor scoreProcessor,
        HealthProcessor healthProcessor,
        GameplayState gameplayState,
        Score? replayScore,
        BmsRulesetConfigManager? config)
    {
        this.scoreProcessor = scoreProcessor;
        this.healthProcessor = healthProcessor;
        this.gameplayState = gameplayState;
        this.replayScore = replayScore;
        this.config = config;
        scoreProcessor.HasCompleted.BindValueChanged(onPlayCompleted);
    }

    public void Dispose() => scoreProcessor.HasCompleted.ValueChanged -= onPlayCompleted;

    internal static double AddVisualOffsetSuggestion(BmsRulesetConfigManager config, double medianHitError)
    {
        var suggestion = BmsRulesetRuntime.VisualOffsetSuggestions.Add(
            medianHitError,
            config.Get<double>(BmsRulesetSetting.VisualOffset));

        if (config.Get<bool>(BmsRulesetSetting.AutomaticallyAdjustVisualOffset))
            config.SetValue(BmsRulesetSetting.VisualOffset, suggestion);

        return suggestion;
    }

    private void onPlayCompleted(ValueChangedEvent<bool> _)
    {
        if (healthProcessor is BmsHealthProcessor bmsHealthProcessor)
        {
            var passed = bmsHealthProcessor.HasPassedAtEnd();
            scoreProcessor.PopulateScore(gameplayState.Score.ScoreInfo);

            if (bmsHealthProcessor.GaugeHistory.Count > 0)
                BmsScoreGaugeHistoryStore.Set(gameplayState.Score.ScoreInfo, bmsHealthProcessor.GaugeHistory);

            if (!passed)
                scoreProcessor.FailScore(gameplayState.Score.ScoreInfo);

            recordVisualOffsetSuggestion();
            return;
        }

        if (healthProcessor.Health.Value < 0.8)
            scoreProcessor.FailScore(gameplayState.Score.ScoreInfo);

        recordVisualOffsetSuggestion();
    }

    private void recordVisualOffsetSuggestion()
    {
        if (replayScore != null || config == null || gameplayState.Mods.Any(mod => !mod.UserPlayable))
            return;

        var hitEvents = gameplayState.Score.ScoreInfo.HitEvents;

        if (hitEvents.Count(HitEventExtensions.AffectsUnstableRate) < 50
            || hitEvents.CalculateMedianHitError() is not double medianHitError)
            return;

        AddVisualOffsetSuggestion(config, medianHitError);
    }
}
