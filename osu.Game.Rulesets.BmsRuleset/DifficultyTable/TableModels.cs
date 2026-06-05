using System.Collections.Generic;

namespace osu.Game.Rulesets.BmsRuleset.DifficultyTable;

public enum TableSource
{
    LocalFile,
    RemoteUrl,
}

public class DifficultyTable
{
    public string Name { get; init; } = string.Empty;

    public string Symbol { get; init; } = string.Empty;

    public string[] LevelOrder { get; init; } = [];

    public TableSource Source { get; init; }

    public string? SourcePath { get; init; }

    public List<TableEntry> Entries { get; init; } = [];
}

public class TableEntry
{
    public string Level { get; init; } = string.Empty;

    public int LevelIndex { get; init; }

    public string Md5Hash { get; init; } = string.Empty;

    public string? Title { get; init; }

    public string? Artist { get; init; }
}

/// <summary>
/// Stores header data before merging into a DifficultyTable.
/// </summary>
public class RawTableData
{
    public string? Name { get; init; }

    public string? Symbol { get; init; }

    public string? DataUrl { get; init; }

    public string[]? LevelOrder { get; init; }
}

public class RawChartItem
{
    public string? Level { get; init; }

    public string? Md5 { get; init; }

    public string? Sha256 { get; init; }

    public string? Title { get; init; }

    public string? Artist { get; init; }
}
