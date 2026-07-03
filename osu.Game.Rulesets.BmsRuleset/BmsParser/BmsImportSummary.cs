using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Difficulty;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

internal sealed record BmsImportSummary(
    BmsChartMetadata Metadata,
    double Bpm,
    double Length,
    int TotalObjectCount,
    int EndTimeObjectCount,
    IReadOnlyList<BmsNoteTiming> StarRatingNoteTimings);
