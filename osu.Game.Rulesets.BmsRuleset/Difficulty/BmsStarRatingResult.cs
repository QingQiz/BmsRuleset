// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace osu.Game.Rulesets.BmsRuleset.Difficulty;

public class BmsStarRatingResult
{
    public double StarRating { get; set; }

    public double Percentile93 { get; set; }

    public double Percentile83 { get; set; }

    public double WeightedMean { get; set; }

    public double[] AllCorners { get; set; } = [];

    public double[] Jbar { get; set; } = [];

    public double[] Xbar { get; set; } = [];

    public double[] Pbar { get; set; } = [];

    public double[] Abar { get; set; } = [];

    public double[] Rbar { get; set; } = [];

    public double[] DensityC { get; set; } = [];

    public double[] ActiveColumnsKs { get; set; } = [];

    public double[] DifficultyD { get; set; } = [];

    public double[] AnchorValues { get; set; } = [];

    public double[] BaseCorners { get; set; } = [];

    public double[] ACorners { get; set; } = [];
}
