namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

public readonly record struct BmsChartMetadata(
    string SetTitle,
    string Artist,
    string DifficultyName,
    int KeyCount,
    string RawTitle,
    int Rank,
    double Total
);
