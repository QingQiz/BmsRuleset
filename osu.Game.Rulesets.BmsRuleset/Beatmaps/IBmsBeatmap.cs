using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps;

public interface IBmsBeatmap
{
    int TickResolution { get; set; }

    int TotalColumns { get; set; }

    BmsLayoutVariant LayoutVariant { get; set; }

    /// <summary>BMS #RANK value: 0=Very Hard, 1=Hard, 2=Normal (default), 3=Easy, 4=Very Easy.</summary>
    int Rank { get; set; }

    /// <summary>
    ///     BMS #TOTAL value: gauge recovery coefficient.
    ///     Zero means the default formula <c>7.605 × N / (0.01 × N + 6.5)</c> applies, where N is the total note count.
    /// </summary>
    double Total { get; set; }

    BmsTimingMap? TimingMap { get; set; }

    IReadOnlyDictionary<ushort, string> SampleDefinitions { get; set; }

    IReadOnlyList<BmsSampleEvent> BackgroundSampleEvents { get; set; }

    IReadOnlyList<BmsSampleEvent> LongNoteTailSampleEvents { get; set; }

    IReadOnlyList<BmsBranchDecision> BranchDecisions { get; set; }

    BmsTextEvents TextEvents { get; set; }

    BmsLongNoteMode LockedLongNoteMode { get; set; }

    string? PreviewFile { get; set; }
}

internal static class BmsBeatmapExtensions
{
    public static void CopyBmsDataFrom(this IBmsBeatmap target, IBmsBeatmap source)
    {
        target.TickResolution = source.TickResolution;
        target.TotalColumns = source.TotalColumns;
        target.LayoutVariant = source.LayoutVariant;
        target.Rank = source.Rank;
        target.Total = source.Total;
        target.TimingMap = source.TimingMap;
        target.SampleDefinitions = source.SampleDefinitions;
        target.BackgroundSampleEvents = source.BackgroundSampleEvents;
        target.LongNoteTailSampleEvents = source.LongNoteTailSampleEvents;
        target.BranchDecisions = source.BranchDecisions;
        target.TextEvents = source.TextEvents;
        target.LockedLongNoteMode = source.LockedLongNoteMode;
        target.PreviewFile = source.PreviewFile;
    }
}
