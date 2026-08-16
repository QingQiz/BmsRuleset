using System;
using System.Collections.Generic;
using System.Linq;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

public sealed record BmsCourseDefinition(
    string Id,
    string TableName,
    string Name,
    IReadOnlyList<BmsCourseStage> Stages,
    string Gauge,
    IReadOnlyList<string> Constraints)
{
    public bool Matches(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return true;

        const StringComparison comparison = StringComparison.OrdinalIgnoreCase;

        return Name.Contains(searchTerm, comparison)
               || TableName.Contains(searchTerm, comparison)
               || Gauge.Contains(searchTerm, comparison)
               || Constraints.Any(constraint => constraint.Contains(searchTerm, comparison))
               || Stages.Any(stage => stage.Title.Contains(searchTerm, comparison)
                                      || stage.Difficulty.Contains(searchTerm, comparison));
    }
}

public sealed record BmsCourseStage(string Title, string Difficulty, bool IsAvailable = true);
