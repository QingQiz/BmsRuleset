using System.Threading;
using osu.Game.Beatmaps;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps.Conversion;

/// <summary>
///     Converts a supported foreign ruleset beatmap into BMS-owned beatmap and hit object types.
/// </summary>
internal interface IBmsForeignBeatmapConverter
{
    /// <summary>
    ///     Whether this converter supports the supplied source metadata.
    /// </summary>
    bool CanConvert(IBeatmapInfo source);

    /// <summary>
    ///     Returns the BMS difficulty metadata produced from the supplied source metadata.
    /// </summary>
    BmsDifficultyInfo GetConvertedDifficultyInfo(IBeatmapInfo source);

    /// <summary>
    ///     Whether this converter supports the complete supplied source beatmap.
    /// </summary>
    /// <remarks>
    ///     This method is called while browsing beatmaps and must be side-effect free. It should validate
    ///     both source metadata and every hit object required by <see cref="Convert"/>.
    /// </remarks>
    bool CanConvert(IBeatmap source);

    /// <summary>
    ///     Populates BMS-specific layout, source-specific metadata, and hit objects on an already-created target beatmap.
    /// </summary>
    /// <remarks>
    ///     Shared metadata has already been copied. Sorting, fallback timing, difficulty stamping, column remapping,
    ///     and judgement context are applied by <see cref="BmsBeatmapConverter"/> after this method returns.
    /// </remarks>
    void Convert(IBeatmap source, BmsBeatmap target, CancellationToken cancellationToken);
}
