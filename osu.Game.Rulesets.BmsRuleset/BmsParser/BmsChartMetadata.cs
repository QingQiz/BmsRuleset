using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

public readonly record struct BmsChartMetadata(
    string Artist,
    string DifficultyName,
    BmsLayoutVariant LayoutVariant,
    string RawTitle,
    int Rank,
    double? Total,
    float? PlayLevel,
    BmsLongNoteMode LockedLongNoteMode = BmsLongNoteMode.Undefined,
    double? ExRank = null)
{
    public int KeyCount => BmsLayout.GetTotalColumns(LayoutVariant);
}
