using RhythmEngine.Core;

namespace RhythmEngine.Profiles.Etterna;

/// <summary>Result identities emitted and consumed by the Etterna profiles; ordering and scoring remain configured.</summary>
public static class EtternaResults
{
    public static readonly ResultId W1 = new("w1");
    public static readonly ResultId W2 = new("w2");
    public static readonly ResultId W3 = new("w3");
    public static readonly ResultId W4 = new("w4");
    public static readonly ResultId W5 = new("w5");
    public static readonly ResultId MISS = new("miss");
    public static readonly ResultId HELD = new("held");
    public static readonly ResultId LET_GO = new("let-go");
    public static readonly ResultId MISSED_HOLD = new("missed-hold");
    public static readonly ResultId MINE_HIT = new("mine-hit");
    public static readonly ResultId MINE_AVOIDED = new("mine-avoided");
}
