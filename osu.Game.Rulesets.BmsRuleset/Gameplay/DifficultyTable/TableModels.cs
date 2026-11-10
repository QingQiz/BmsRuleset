using System.Collections.Generic;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.DifficultyTable;

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

    public List<TableCourse> Courses { get; init; } = [];
}

public class TableEntry
{
    public string Level { get; init; } = string.Empty;

    public string Md5Hash { get; init; } = string.Empty;

    public string? Sha256Hash { get; init; }

    internal IEnumerable<string> Hashes
    {
        get
        {
            if (!string.IsNullOrEmpty(Md5Hash))
                yield return Md5Hash;
            if (!string.IsNullOrEmpty(Sha256Hash) && !string.Equals(Md5Hash, Sha256Hash, System.StringComparison.OrdinalIgnoreCase))
                yield return Sha256Hash;
        }
    }

    public string? Title { get; init; }

    public string? Artist { get; init; }

    public string? Url { get; init; }

    public string? UrlDiff { get; init; }
}

public class TableCourse
{
    public string Name { get; init; } = string.Empty;

    public string[] Hashes { get; init; } = [];

    public string[] Constraints { get; init; } = [];
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

    public List<RawCourse>? Courses { get; init; }
}

public class RawCourse
{
    public string? Name { get; init; }

    public string[] Hashes { get; init; } = [];

    public string[] Constraints { get; init; } = [];
}

public class RawChartItem
{
    public string? Level { get; init; }

    public string? Md5 { get; init; }

    public string? Sha256 { get; init; }

    public string? Title { get; init; }

    public string? Artist { get; init; }

    public string? Url { get; init; }

    public string? UrlDiff { get; init; }
}

public readonly record struct UnavailableTableEntry(TableEntry Entry, DifficultyTable Table);
