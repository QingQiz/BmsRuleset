using System;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio;

internal static class BmsAudioLogger
{
    public static void LogLoadFailure(string message, Exception? exception = null)
    {
        // Invalid chart audio is recoverable, so keep the diagnostics without osu! promoting the entry to an error notification.
        Logger.GetLogger(LoggingTarget.Runtime).Add(message, LogLevel.Verbose, exception);
    }
}
