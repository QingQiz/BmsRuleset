using System;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;

internal sealed class BmsPracticeCountdown
{
    internal bool IsReady { get; private set; }
    internal bool IsPaused { get; private set; }
    internal double RemainingMilliseconds { get; private set; }
    internal int SecondsRemaining => (int)Math.Ceiling(RemainingMilliseconds / 1000);
    internal bool HasFinished => IsReady && !IsPaused && RemainingMilliseconds <= 0;

    internal void Ready(int seconds, bool paused)
    {
        RemainingMilliseconds = Math.Clamp(seconds, 0, 5) * 1000;
        IsReady = true;
        IsPaused = paused;
    }

    internal void Advance(double elapsedMilliseconds)
    {
        if (!IsReady || IsPaused || !double.IsFinite(elapsedMilliseconds) || elapsedMilliseconds <= 0)
            return;

        RemainingMilliseconds = Math.Max(0, RemainingMilliseconds - elapsedMilliseconds);
    }

    internal void Pause() => IsPaused = true;

    internal void Resume() => IsPaused = false;

    internal void Skip()
    {
        if (IsReady && !IsPaused)
            RemainingMilliseconds = 0;
    }

    internal void Cancel()
    {
        IsReady = false;
        RemainingMilliseconds = 0;
    }
}
