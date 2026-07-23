namespace osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;

public sealed class BmsTickInfo
{
    /// <summary>
    ///     Tick position in the native BMS timeline.
    /// </summary>
    public long Tick { get; set; }

    /// <summary>
    ///     End tick for long notes. For normal notes this remains equal to <see cref="Tick" />.
    /// </summary>
    public long EndTick { get; set; }
}
