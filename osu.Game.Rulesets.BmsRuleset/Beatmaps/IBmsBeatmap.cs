using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps;

public interface IBmsBeatmap
{
    int TickResolution { get; set; }

    int TotalColumns { get; set; }

    BmsLayoutVariant LayoutVariant { get; set; }

    BmsTimingMap? TimingMap { get; set; }

    IReadOnlyDictionary<string, string> SampleDefinitions { get; set; }

    IReadOnlyList<BmsSampleEvent> BackgroundSampleEvents { get; set; }
}

static internal class BmsBeatmapExtensions
{
    public static void CopyBmsDataFrom(this IBmsBeatmap target, IBmsBeatmap source)
    {
        target.TickResolution = source.TickResolution;
        target.TotalColumns = source.TotalColumns;
        target.LayoutVariant = source.LayoutVariant;
        target.TimingMap = source.TimingMap;
        target.SampleDefinitions = source.SampleDefinitions;
        target.BackgroundSampleEvents = source.BackgroundSampleEvents;
    }
}
