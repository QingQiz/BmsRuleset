using System;
using System.Text.Json.Serialization;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;

public sealed record BmsPracticeSettings
{
    private const double boundary_epsilon = 1e-7;

    public double StartTime { get; init; }

    public double EndTime { get; init; }

    public bool SkipResults { get; init; }

    public int CountIn { get; init; } = 3;

    // Grid projection and note offsets can round the same musical position differently.
    // Resolve that noise consistently at both boundaries while keeping the end exclusive.
    public bool Contains(double time, bool millisecondPrecision = false)
    {
        // Integer timestamps can lose the grid's fractional millisecond.
        // Compare them in the same millisecond cells while preserving fractional authoring.
        if (millisecondPrecision && time == Math.Truncate(time))
            return time >= Math.Floor(StartTime + boundary_epsilon) && time < Math.Floor(EndTime + boundary_epsilon);

        return time >= StartTime - boundary_epsilon && time < EndTime - boundary_epsilon;
    }

    [JsonIgnore]
    public bool IsValid => double.IsFinite(StartTime) && double.IsFinite(EndTime)
                           && StartTime < EndTime && CountIn is >= 0 and <= 5;

    public bool IsValidFor(BmsBeatmap beatmap)
    {
        var bounds = BmsPracticeBeatmap.GetTimeBounds(beatmap);
        return IsValid && StartTime >= bounds.StartTime && EndTime <= bounds.EndTime
                       && BmsPracticeBeatmap.SelectedCount(beatmap, this) > 0;
    }

    public static BmsPracticeSettings CreateDefault(BmsBeatmap beatmap)
    {
        var bounds = BmsPracticeBeatmap.GetTimeBounds(beatmap);
        return new BmsPracticeSettings { StartTime = bounds.StartTime, EndTime = bounds.EndTime };
    }
}
