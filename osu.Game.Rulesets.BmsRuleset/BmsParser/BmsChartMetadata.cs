using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Objects;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

public readonly record struct BmsChartMetadata(
    string Artist,
    string DifficultyName,
    int KeyCount,
    string RawTitle,
    int Rank,
    double Total,
    float? PlayLevel,
    BmsLongNoteMode LockedLongNoteMode = BmsLongNoteMode.Undefined,
    double? ExRank = null
);
