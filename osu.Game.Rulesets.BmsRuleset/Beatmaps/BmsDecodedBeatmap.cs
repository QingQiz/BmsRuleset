using System.Collections.Generic;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps;

internal class BmsDecodedBeatmap : Beatmap, IBmsBeatmap
{
    public int TickResolution { get; set; }

    public int TotalColumns { get; set; }

    public BmsLayoutVariant LayoutVariant { get; set; } = BmsLayoutVariant.Bms5K;

    public int Rank { get; set; } = 2;

    public double Total { get; set; }

    public BmsTimingMap? TimingMap { get; set; }

    public IReadOnlyDictionary<ushort, string> SampleDefinitions { get; set; } = new Dictionary<ushort, string>();

    public IReadOnlyList<BmsSampleEvent> BackgroundSampleEvents { get; set; } = [];

    public IReadOnlyList<BmsSampleEvent> LongNoteTailSampleEvents { get; set; } = [];

    public IReadOnlyList<BmsBranchDecision> BranchDecisions { get; set; } = [];

    public BmsTextEvents TextEvents { get; set; } = new(string.Empty, []);

    public string? PreviewFile { get; set; }

    public string[] RawLines { get; set; } = [];

    public void CopyFrom(BmsParseResult parseResult)
    {
        TickResolution = parseResult.TickResolution;
        TimingMap = parseResult.TimingMap;
        TotalColumns = parseResult.TotalColumns;
        LayoutVariant = parseResult.LayoutVariant;
        Rank = parseResult.Rank;
        Total = parseResult.Total;
        SampleDefinitions = parseResult.SampleDefinitions;
        BackgroundSampleEvents = parseResult.BackgroundSampleEvents;
        LongNoteTailSampleEvents = parseResult.LongNoteTailSampleEvents;
        BranchDecisions = parseResult.BranchDecisions;
        TextEvents = parseResult.TextEvents;
        PreviewFile = parseResult.PreviewFile;
    }
}
