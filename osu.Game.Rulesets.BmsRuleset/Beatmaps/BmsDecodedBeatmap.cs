using System.Collections.Generic;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps;

internal class BmsDecodedBeatmap : Beatmap, IBmsBeatmap
{
    public int TotalColumns { get; set; }

    public BmsLayoutVariant LayoutVariant { get; set; } = BmsLayoutVariant.Bms5K;

    public int Rank { get; set; } = 2;

    public double? ExRank { get; set; }

    public double? Total { get; set; }

    public BmsTimingMap? TimingMap { get; set; }

    public IReadOnlyDictionary<int, BmsSampleDefinition> SampleDefinitions { get; set; } = new Dictionary<int, BmsSampleDefinition>();

    public IReadOnlyList<BmsSampleEvent> BackgroundSampleEvents { get; set; } = [];

    public IReadOnlyList<BmsInvisibleNote> InvisibleNotes { get; set; } = [];

    public IReadOnlyList<BmsBranchDecision> BranchDecisions { get; set; } = [];

    public BmsTextEvents TextEvents { get; set; } = new(string.Empty, []);

    public BmsBgaTimeline Bga { get; set; } = new(new Dictionary<int, string>(), new Dictionary<int, BmsBgaDefinition>(), [], [], BmsPoorBgaMode.Replace);

    public BmsLongNoteMode LockedLongNoteMode { get; set; }

    public string? PreviewFile { get; set; }

    public string? StageFile { get; set; }

    public string? BackBmp { get; set; }

    public string? Banner { get; set; }

    public string[] RawLines { get; set; } = [];
}
