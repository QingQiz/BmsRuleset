using RhythmEngine.Core;

namespace RhythmEngine.Profiles.Mania;

/// <summary>Result identities emitted and consumed by the Mania profiles; ordering and scoring remain configured.</summary>
public static class ManiaResults
{
    public static readonly ResultId PERFECT = new("perfect");
    public static readonly ResultId GREAT = new("great");
    public static readonly ResultId GOOD = new("good");
    public static readonly ResultId OK = new("ok");
    public static readonly ResultId MEH = new("meh");
    public static readonly ResultId MISS = new("miss");
    public static readonly ResultId HOLD_TICK = new("hold-tick");
    public static readonly ResultId HOLD_BREAK = new("hold-break");
    public static readonly ResultId COMBO_BREAK = new("combo-break");
}
