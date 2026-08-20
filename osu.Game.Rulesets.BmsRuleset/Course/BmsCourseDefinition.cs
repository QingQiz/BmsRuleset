using System;
using System.Collections.Generic;
using System.Linq;

namespace osu.Game.Rulesets.BmsRuleset.Course;

public sealed record BmsCourseDefinition(
    string Id,
    string TableName,
    string Name,
    IReadOnlyList<BmsCourseStage> Stages,
    string Gauge,
    IReadOnlyList<string> Constraints,
    int Order = int.MaxValue,
    string TableMark = "")
{
    public bool Matches(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return true;

        const StringComparison comparison = StringComparison.OrdinalIgnoreCase;

        return Name.Contains(searchTerm, comparison)
               || TableName.Contains(searchTerm, comparison)
               || TableMark.Contains(searchTerm, comparison)
               || Gauge.Contains(searchTerm, comparison)
               || Constraints.Any(constraint => constraint.Contains(searchTerm, comparison))
               || Stages.Any(stage => stage.Title.Contains(searchTerm, comparison)
                                      || stage.Difficulty.Contains(searchTerm, comparison)
                                      || stage.Artist?.Contains(searchTerm, comparison) == true);
    }
}

public sealed record BmsCourseStage(
    string Title,
    string Difficulty,
    bool IsAvailable = true,
    string? BeatmapHash = null,
    string? Artist = null);
