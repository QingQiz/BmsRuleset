using RhythmEngine.Core;

namespace RhythmEngine.Profiles.Bms;

/// <summary>Result identities emitted and consumed by the Bms profiles; ordering and scoring remain configured.</summary>
public static class BmsResults
{
    public static readonly ResultId PGREAT = new("pgreat");
    public static readonly ResultId GREAT = new("great");
    public static readonly ResultId GOOD = new("good");
    public static readonly ResultId BAD = new("bad");
    public static readonly ResultId POOR = new("poor");
    public static readonly ResultId EMPTY_POOR = new("empty-poor");
    public static readonly ResultId MINE_HIT = new("mine-hit");
    public static readonly ResultId MINE_AVOIDED = new("mine-avoided");
    public static readonly ResultId SUSTAIN_GAIN = new("sustain-gain");
    public static readonly ResultId SUSTAIN_LOSS = new("sustain-loss");
}
