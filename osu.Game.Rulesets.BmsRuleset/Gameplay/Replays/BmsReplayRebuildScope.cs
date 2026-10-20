using System;
using System.Runtime.CompilerServices;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;

internal sealed class BmsReplayRebuildScope : IDisposable
{
    private readonly BmsScoreProcessor score;
    private readonly BmsHealthProcessor health;
    private readonly Action<JudgementResult>? scoreNotifications;
    private readonly Action<JudgementResult>? healthNotifications;
    private readonly Func<bool>? failure;

    internal BmsReplayRebuildScope(BmsScoreProcessor score, BmsHealthProcessor health, Func<bool>? allowFailure)
    {
        this.score = score;
        this.health = health;
        scoreNotifications = notifications(score);
        healthNotifications = notifications(health);
        failure = failed(health);
        // Player and HUD subscribers must only observe the completed reconstruction.
        notifications(score) = null;
        notifications(health) = null;
        failed(health) = allowFailure;
        score.IsRebuildingReplay = true;
    }

    public void Dispose()
    {
        score.IsRebuildingReplay = false;
        notifications(score) = scoreNotifications;
        notifications(health) = healthNotifications;
        failed(health) = failure;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "NewJudgement")]
    private static extern ref Action<JudgementResult>? notifications(JudgementProcessor processor);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "Failed")]
    private static extern ref Func<bool>? failed(HealthProcessor processor);
}
