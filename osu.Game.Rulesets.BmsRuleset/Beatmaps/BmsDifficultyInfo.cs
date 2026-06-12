using System;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps;

/// <summary>
/// Represents the BMS-specific difficulty parameters parsed from chart headers
/// (#RANK, #TOTAL, #PLAYLEVEL, total columns). Provides mapping helpers to and from
/// osu! <see cref="BeatmapInfo.DifficultyName"/> and <see cref="BeatmapDifficulty"/>.
/// </summary>
public readonly struct BmsDifficultyInfo
{
    /// <summary>Pre-parsed difficulty name from chart title/filename (e.g. "SP Beginner"). Takes priority over PlayLevel/Rank.</summary>
    public string? ParsedName { get; init; }

    /// <summary>#PLAYLEVEL — star rating (1–20+). Null if absent.</summary>
    public float? PlayLevel { get; init; }

    /// <summary>#RANK — gauge difficulty: 0=VeryHard, 1=Hard, 2=Normal, 3=Easy, 4=VeryEasy.</summary>
    public int Rank { get; init; }

    /// <summary>#TOTAL — gauge recovery coefficient. Zero means default formula.</summary>
    public double Total { get; init; }

    /// <summary>Playable column count (inferred from chart or CircleSize).</summary>
    public int KeyCount { get; init; }

    /// <summary>
    /// Reads BMS difficulty from a parse result.
    /// </summary>
    public static BmsDifficultyInfo FromParseResult(BmsParseResult result) => new()
    {
        PlayLevel = result.PlayLevel,
        Rank = result.Rank,
        Total = result.Total,
        KeyCount = result.TotalColumns,
    };

    public static BmsDifficultyInfo FromChartMetadata(BmsChartMetadata metadata) => new()
    {
        ParsedName = metadata.DifficultyName,
        PlayLevel = metadata.PlayLevel,
        Rank = metadata.Rank,
        Total = metadata.Total,
        KeyCount = metadata.KeyCount,
    };

    /// <summary>
    /// Converts BMS #RANK to osu! OverallDifficulty.
    /// </summary>
    public static float RankToOd(int rank) => rank switch
    {
        0 => 10f,
        1 => 8f,
        2 => 7f,
        3 => 6f,
        4 => 5f,
        _ => 7f,
    };

    /// <summary>
    /// Converts osu! OverallDifficulty back to the closest BMS #RANK.
    /// </summary>
    public static int OdToRank(float od) => od switch
    {
        >= 9.5f => 0,
        >= 7.5f => 1,
        >= 6.5f => 2,
        >= 5.5f => 3,
        _ => 4,
    };

    /// <summary>
    /// Converts the BMS difficulty to a display name string suitable for
    /// <see cref="BeatmapInfo.DifficultyName"/>.
    /// Returns the <see cref="PlayLevel"/> value when set, or a name based on <see cref="Rank"/>.
    /// </summary>
    public string ToDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(ParsedName))
            return ParsedName;

        if (PlayLevel.HasValue)
            return PlayLevel.Value.ToString("F0");

        return Rank switch
        {
            0 => "Very Hard",
            1 => "Hard",
            2 => "Normal",
            3 => "Easy",
            4 => "Very Easy",
            _ => "Normal",
        };
    }

    /// <summary>
    /// Reconstructs a <see cref="BmsDifficultyInfo"/> from an osu! difficulty object.
    /// <see cref="Total"/> is stored in <see cref="IBeatmapDifficultyInfo.ApproachRate"/>.
    /// <see cref="PlayLevel"/> cannot be recovered from this source.
    /// </summary>
    public static BmsDifficultyInfo FromOsuDifficulty(IBeatmapDifficultyInfo difficulty) => new()
    {
        Rank = OdToRank(difficulty.OverallDifficulty),
        Total = Math.Max(0, difficulty.ApproachRate),
        KeyCount = GetKeyCount(difficulty),
    };

    /// <summary>
    /// Writes BMS difficulty parameters to an osu! <see cref="IBeatmap"/>.
    /// Sets <see cref="BeatmapInfo.DifficultyName"/> and the relevant
    /// <see cref="BeatmapDifficulty"/> fields.
    /// </summary>
    public void WriteToOsuDifficulty(IBeatmap beatmap)
    {
        beatmap.BeatmapInfo.DifficultyName = ToDisplayName();

        var od = RankToOd(Rank);
        beatmap.Difficulty.OverallDifficulty = od;
        beatmap.BeatmapInfo.Difficulty.OverallDifficulty = od;

        beatmap.Difficulty.CircleSize = KeyCount;
        beatmap.BeatmapInfo.Difficulty.CircleSize = KeyCount;

        beatmap.Difficulty.ApproachRate = (float)Total;
        beatmap.BeatmapInfo.Difficulty.ApproachRate = (float)Total;
    }

    /// <summary>
    /// Writes BMS difficulty parameters to an osu! <see cref="IBeatmap"/>.
    /// Sets <see cref="BeatmapInfo.DifficultyName"/> and the relevant
    /// <see cref="BeatmapDifficulty"/> fields.
    /// </summary>
    public void WriteToOsuDifficulty(BeatmapInfo beatmap)
    {
        beatmap.DifficultyName = ToDisplayName();

        var od = RankToOd(Rank);
        beatmap.Difficulty.OverallDifficulty = od;
        beatmap.Difficulty.CircleSize = KeyCount;
        beatmap.Difficulty.ApproachRate = (float)Total;
    }

    /// <summary>
    /// Reads the total column count from an osu! difficulty object (<see cref="BeatmapDifficulty"/>
    /// or <see cref="IBeatmapDifficultyInfo"/>).
    /// </summary>
    public static int GetKeyCount(IBeatmapDifficultyInfo difficulty) =>
        Math.Max(1, (int)Math.Round(difficulty.CircleSize));

}
