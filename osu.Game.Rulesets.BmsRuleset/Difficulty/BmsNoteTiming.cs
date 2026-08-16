namespace osu.Game.Rulesets.BmsRuleset.Difficulty;

/// <summary>
/// Lightweight note data for star-rating computation.
/// Mines must be filtered out before passing to <see cref="BmsStarRatingProcessor"/>.
/// </summary>
/// <param name="Column">Playfield column index.</param>
/// <param name="StartTime">Absolute start time in milliseconds.</param>
/// <param name="EndTime">Absolute end time in milliseconds. Equal to <see cref="StartTime"/> for short notes.</param>
public readonly record struct BmsNoteTiming(int Column, double StartTime, double EndTime);
