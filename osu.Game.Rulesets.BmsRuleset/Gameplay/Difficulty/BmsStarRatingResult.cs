// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Difficulty;

public class BmsStarRatingResult
{
    public double StarRating { get; init; }

    public double Percentile93 { get; set; }

    public double Percentile83 { get; set; }

    public double WeightedMean { get; set; }
}
