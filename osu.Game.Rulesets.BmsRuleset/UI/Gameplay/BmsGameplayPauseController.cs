using System;
using System.Reflection;
using HarmonyLib;
using osu.Framework.Bindables;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal sealed class BmsGameplayPauseController : IDisposable
{
    private static readonly MethodInfo? frame_stable_playback_setter = AccessTools.PropertySetter(typeof(osu.Game.Rulesets.UI.DrawableRuleset), "FrameStablePlayback");
    private static readonly FieldInfo? player_last_pause_action_time_field = AccessTools.Field(typeof(Player), "lastPauseActionTime");

    private readonly BmsDrawableRuleset drawableRuleset;
    private readonly BmsPlayfield playfield;
    private readonly GameplayClockContainer gameplayClock;
    private readonly GameplayState? gameplayState;
    private readonly Player? player;
    private readonly Action stopPreviewForGameplay;
    private readonly Action<Action> scheduleAfterChildren;

    private double? pendingResumeRewindFrom;

    internal BmsGameplayPauseController(
        BmsDrawableRuleset drawableRuleset,
        BmsPlayfield playfield,
        GameplayClockContainer gameplayClock,
        GameplayState? gameplayState,
        Player? player,
        Action stopPreviewForGameplay,
        Action<Action> scheduleAfterChildren)
    {
        this.drawableRuleset = drawableRuleset;
        this.playfield = playfield;
        this.gameplayClock = gameplayClock;
        this.gameplayState = gameplayState;
        this.player = player;
        this.stopPreviewForGameplay = stopPreviewForGameplay;
        this.scheduleAfterChildren = scheduleAfterChildren;
        gameplayClock.IsPaused.ValueChanged += onGameplayPausedChanged;
    }

    public void Dispose() => gameplayClock.IsPaused.ValueChanged -= onGameplayPausedChanged;

    private void onGameplayPausedChanged(ValueChangedEvent<bool> paused)
    {
        if (paused.NewValue)
        {
            pendingResumeRewindFrom = gameplayClock.CurrentTime;
            return;
        }

        stopPreviewForGameplay();

        if (pendingResumeRewindFrom == null || gameplayState == null)
            return;

        var recordedPauseTime = (int)Math.Round(pendingResumeRewindFrom.Value);

        if (gameplayState.Score.ScoreInfo.Pauses.Contains(recordedPauseTime))
        {
            BmsModPaused.ApplyToScore(gameplayState.Score.ScoreInfo);
            var rewindTarget = playfield.BeginResumeRewind(pendingResumeRewindFrom.Value, gameplayClock.StartTime);
            clearPauseCooldownForResumeRewind();
            seekImmediatelyForResume(rewindTarget);
        }

        pendingResumeRewindFrom = null;
    }

    private void clearPauseCooldownForResumeRewind()
    {
        if (player == null || player_last_pause_action_time_field == null)
            return;

        try
        {
            // Gameplay time rewinds, so the base cooldown must not retain a timestamp from the future.
            player_last_pause_action_time_field.SetValue(player, null);
        }
        catch (Exception exception)
        {
            BmsLogger.Error(exception, "Failed to clear the pause cooldown for a BMS resume rewind.");
        }
    }

    private void seekImmediatelyForResume(double rewindTarget)
    {
        if (!trySetFrameStablePlayback(false))
        {
            gameplayClock.Seek(rewindTarget);
            return;
        }

        // Replaying every intermediate frame turns the resume lead-in into a visible stall.
        gameplayClock.Seek(rewindTarget);
        scheduleAfterChildren(() => trySetFrameStablePlayback(true));
    }

    private bool trySetFrameStablePlayback(bool enabled)
    {
        if (frame_stable_playback_setter == null)
            return false;

        try
        {
            frame_stable_playback_setter.Invoke(drawableRuleset, [enabled]);
            return true;
        }
        catch (Exception exception)
        {
            BmsLogger.Error(exception, $"Failed to {(enabled ? "restore" : "disable")} frame-stable BMS playback for a resume rewind.");
            return false;
        }
    }
}
