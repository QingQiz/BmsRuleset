using RhythmEngine.Core;

namespace RhythmEngine.Profiles.Malody;

/// <summary>Result identities emitted and consumed by the Malody profiles; ordering and scoring remain configured.</summary>
public static class MalodyResults
{
    public static readonly ResultId BEST = new("best");
    public static readonly ResultId COOL = new("cool");
    public static readonly ResultId GOOD = new("good");
    public static readonly ResultId MISS = new("miss");
    public static readonly ResultId HOLD_DROP = new("hold-drop");
    public static readonly ResultId HOLD_TICK = new("hold-tick");
    public static readonly ResultId CRITICAL = new(MalodyCounters.CRITICAL);
}
