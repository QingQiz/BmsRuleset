using System;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.BmsRuleset;

/// <summary>
/// Central logger for the BMS ruleset. A named logger keeps ruleset diagnostics
/// out of osu!'s general runtime log while preserving the framework's filtering
/// and listener behaviour.
/// </summary>
internal static class BmsLogger
{
    private static readonly Logger logger = Logger.GetLogger("bms");

    public static void Log(string message, LogLevel level = LogLevel.Verbose, Exception? exception = null)
        => logger.Add(message, level, exception);

    public static void Error(Exception exception, string message)
        => logger.Add(message, LogLevel.Error, exception);

    public static void LogAudioFailure(string message, Exception? exception = null)
    {
        // Audio resources are optional and recoverable, so keep failures below osu!'s notification threshold.
        Log(message, LogLevel.Verbose, exception);
    }
}
