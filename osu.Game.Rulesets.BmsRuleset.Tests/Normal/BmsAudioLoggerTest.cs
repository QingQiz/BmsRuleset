using System;
using NUnit.Framework;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal;

[TestFixture]
[NonParallelizable]
public class BmsAudioLoggerTest
{
    [Test]
    public void LoadFailureRemainsBelowNotificationThreshold()
    {
        const string message = "BMS audio load failure test";
        var exception = new InvalidOperationException("test exception");
        LogEntry capturedEntry = null!;
        var previousEnabled = Logger.Enabled;
        var previousLevel = Logger.Level;

        Logger.Enabled = true;
        Logger.Level = LogLevel.Verbose;
        Logger.NewEntry += captureEntry;

        try
        {
            BmsLogger.LogAudioFailure(message, exception);
        }
        finally
        {
            Logger.NewEntry -= captureEntry;
            Logger.Enabled = previousEnabled;
            Logger.Level = previousLevel;
        }

        Assert.That(capturedEntry, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(capturedEntry.Level, Is.EqualTo(LogLevel.Verbose));
            Assert.That(capturedEntry.Level, Is.LessThan(LogLevel.Important));
            Assert.That(capturedEntry.Target, Is.Null);
            Assert.That(capturedEntry.LoggerName, Is.EqualTo("bms"));
            Assert.That(capturedEntry.Message, Is.EqualTo(message));
            Assert.That(capturedEntry.Exception, Is.SameAs(exception));
        });

        void captureEntry(LogEntry entry)
        {
            if (entry.Message == message)
                capturedEntry = entry;
        }
    }
}
