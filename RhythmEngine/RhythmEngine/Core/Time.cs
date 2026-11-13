namespace RhythmEngine.Core;

/// <summary>
/// Chart, input, scheduler and output timestamps are signed microseconds on the same song clock.
/// Rate and historical clock quantisation belong to the profile compiler or input producer.
/// </summary>
public static class Time
{
    public static long FromMilliseconds(double milliseconds)
    {
        if (!double.IsFinite(milliseconds))
            throw new ArgumentOutOfRangeException(nameof(milliseconds));

        return checked((long)Math.Round(milliseconds * 1000, MidpointRounding.AwayFromZero));
    }

    public static double ToMilliseconds(long microseconds) => microseconds / 1000.0;
}
