using System;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Platform;
using osu.Game.Rulesets.BmsRuleset.Configuration;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal sealed class BmsGameplaySettingsController : IDisposable
{
    private readonly Bindable<bool>? unlockFrameRateLimit;
    private readonly GameHost host;
    private readonly FrameworkConfigManager frameworkConfig;

    private IDisposable? frameRateUnlockLease;

    internal BmsGameplaySettingsController(
        BmsRulesetConfigManager? config,
        BmsPlayfield playfield,
        BindableDouble bgaDim,
        double playbackRate,
        GameHost host,
        FrameworkConfigManager frameworkConfig)
    {
        this.host = host;
        this.frameworkConfig = frameworkConfig;

        if (config != null)
        {
            config.BindWith(BmsRulesetSetting.BgaDim, bgaDim);
            config.BindWith(BmsRulesetSetting.VisualOffset, playfield.VisualOffset);
            config.BindWith(BmsRulesetSetting.LongNoteTailVisualOffset, playfield.LongNoteTailVisualOffset);
            playfield.ScrollController.SetConfiguredScrollSpeed(config.Get<double>(BmsRulesetSetting.ScrollSpeed));

            unlockFrameRateLimit = config.GetBindable<bool>(BmsRulesetSetting.UnlockFrameRateLimit);
            unlockFrameRateLimit.BindValueChanged(onUnlockFrameRateLimitChanged, true);
        }

        playfield.ScrollController.SetPlaybackRate(playbackRate);
    }

    public void Dispose()
    {
        unlockFrameRateLimit?.UnbindAll();
        frameRateUnlockLease?.Dispose();
        frameRateUnlockLease = null;
    }

    private void onUnlockFrameRateLimitChanged(ValueChangedEvent<bool> unlocked)
    {
        frameRateUnlockLease?.Dispose();
        frameRateUnlockLease = unlocked.NewValue
            ? BmsFrameRateUnlock.Acquire(
                host,
                frameworkConfig.GetBindable<ExecutionMode>(FrameworkSetting.ExecutionMode),
                frameworkConfig.GetBindable<FrameSync>(FrameworkSetting.FrameSync))
            : null;
    }
}
