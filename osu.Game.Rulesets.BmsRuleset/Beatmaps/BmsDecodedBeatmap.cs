using System.Collections.Generic;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps;

internal class BmsDecodedBeatmap : Beatmap, IBmsBeatmap
{
    public int TickResolution { get; set; }

    public int TotalColumns { get; set; }

    public BmsLayoutVariant LayoutVariant { get; set; } = BmsLayoutVariant.Bms5K;

    public BmsTimingMap? TimingMap { get; set; }

    public IReadOnlyDictionary<string, string> SampleDefinitions { get; set; } = new Dictionary<string, string>();

    public IReadOnlyList<BmsSampleEvent> BackgroundSampleEvents { get; set; } = [];

    public IReadOnlyList<BmsSampleEvent> LongNoteTailSampleEvents { get; set; } = [];

    public void CopyFrom(BmsParseResult parseResult)
    {
        TickResolution = parseResult.TickResolution;
        TimingMap = parseResult.TimingMap;
        TotalColumns = parseResult.TotalColumns;
        LayoutVariant = parseResult.LayoutVariant;
        SampleDefinitions = parseResult.SampleDefinitions;
        BackgroundSampleEvents = parseResult.BackgroundSampleEvents;
        LongNoteTailSampleEvents = parseResult.LongNoteTailSampleEvents;
    }
}
