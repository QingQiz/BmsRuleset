using System.Collections.Generic;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps;

internal class BmsDecodedBeatmap : Beatmap, IBmsBeatmap
{
    public int TickResolution { get; set; }

    public int TotalColumns { get; set; }

    public BmsLayoutVariant LayoutVariant { get; set; } = BmsLayoutVariant.Bms5K;

    public int Rank { get; set; } = 2;

    public double? ExRank { get; set; }

    public double Total { get; set; }

    public BmsTimingMap? TimingMap { get; set; }

    public IReadOnlyDictionary<ushort, string> SampleDefinitions { get; set; } = new Dictionary<ushort, string>();

    public IReadOnlyList<BmsSampleEvent> BackgroundSampleEvents { get; set; } = [];

    public IReadOnlyList<BmsInvisibleNote> InvisibleNotes { get; set; } = [];

    public IReadOnlyList<BmsBranchDecision> BranchDecisions { get; set; } = [];

    public BmsTextEvents TextEvents { get; set; } = new(string.Empty, []);

    public BmsBgaTimeline Bga { get; set; } = new(new Dictionary<ushort, string>(), new Dictionary<ushort, BmsBgaDefinition>(), [], [], BmsPoorBgaMode.Replace);

    public BmsLongNoteMode LockedLongNoteMode { get; set; }

    public string? PreviewFile { get; set; }

    public string? StageFile { get; set; }

    public string? BackBmp { get; set; }

    public string? Banner { get; set; }

    public string[] RawLines { get; set; } = [];
}
