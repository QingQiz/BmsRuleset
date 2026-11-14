using RhythmEngine.Core;

namespace RhythmEngine.Profiles.O2Jam;

/// <summary>Result identities emitted and consumed by the O2Jam profiles; ordering and scoring remain configured.</summary>
public static class O2JamResults
{
    public static readonly ResultId COOL = new("cool");
    public static readonly ResultId GOOD = new("good");
    public static readonly ResultId BAD = new("bad");
    public static readonly ResultId MISS = new("miss");
}
