using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps;

public interface IBmsBeatmap
{
    int TotalColumns { get; set; }

    BmsLayoutVariant LayoutVariant { get; set; }

    /// <summary>BMS #RANK value: 0=Very Hard, 1=Hard, 2=Normal (default), 3=Easy, 4=Very Easy.</summary>
    int Rank { get; set; }

    /// <summary>BMS #DEFEXRANK / #EXRANK percentage (100 = NORMAL), or null when only #RANK is set.</summary>
    double? ExRank { get; set; }

    /// <summary>
    ///     Gauge recovery coefficient. Null uses the active profile default; zero disables TOTAL-based recovery.
    /// </summary>
    double? Total { get; set; }

    BmsTimingMap? TimingMap { get; set; }

    IReadOnlyDictionary<int, BmsSampleDefinition> SampleDefinitions { get; set; }

    IReadOnlyList<BmsSampleEvent> BackgroundSampleEvents { get; set; }

    IReadOnlyList<BmsInvisibleNote> InvisibleNotes { get; set; }

    IReadOnlyList<BmsBranchDecision> BranchDecisions { get; set; }

    BmsTextEvents TextEvents { get; set; }

    BmsBgaTimeline Bga { get; set; }

    BmsLongNoteMode LockedLongNoteMode { get; set; }

    string? PreviewFile { get; set; }

    string? StageFile { get; set; }

    string? BackBmp { get; set; }

    string? Banner { get; set; }
}

internal static class BmsBeatmapExtensions
{
    internal static int CountScoringNotes(this IBeatmap beatmap) => beatmap.HitObjects
        .Where(note => note is not BmsLandmine and not BmsInvisibleNote)
        .Sum(note => note is BmsLongNote { EffectiveMode: BmsLongNoteMode.ChargeNote or BmsLongNoteMode.HellChargeNote } ? 2 : 1);

    public static void CopyFrom(this IBmsBeatmap target, BmsParseResult parseResult)
    {
        target.TimingMap = parseResult.TimingMap;
        target.TotalColumns = parseResult.TotalColumns;
        target.LayoutVariant = parseResult.LayoutVariant;
        target.Rank = parseResult.Rank;
        target.ExRank = parseResult.DefaultExRank;
        target.Total = parseResult.Total;
        target.SampleDefinitions = parseResult.SampleDefinitions;
        target.BackgroundSampleEvents = parseResult.BackgroundSampleEvents;
        target.InvisibleNotes = parseResult.InvisibleNotes.Select(n => new BmsInvisibleNote
        {
            Beatmap = target,
            StartTime = n.StartTime,
            Column = n.Column,
            SampleKey = n.SampleKey,
            SampleVolume = n.SampleVolume,
        }).ToArray();
        target.BranchDecisions = parseResult.BranchDecisions;
        target.TextEvents = parseResult.TextEvents;
        target.Bga = parseResult.Bga;
        target.LockedLongNoteMode = parseResult.LockedLongNoteMode;
        target.PreviewFile = parseResult.PreviewFile;
        target.StageFile = parseResult.StageFile;
        target.BackBmp = parseResult.BackBmp;
        target.Banner = parseResult.Banner;
    }

    public static void CopyBmsDataFrom(this IBmsBeatmap target, IBmsBeatmap source)
    {
        target.TotalColumns = source.TotalColumns;
        target.LayoutVariant = source.LayoutVariant;
        target.Rank = source.Rank;
        target.ExRank = source.ExRank;
        target.Total = source.Total;
        target.TimingMap = source.TimingMap;
        target.SampleDefinitions = source.SampleDefinitions;
        target.BackgroundSampleEvents = source.BackgroundSampleEvents;
        target.InvisibleNotes = source.InvisibleNotes.Select(n => n.CreateKeySoundState(target)).ToArray();
        target.BranchDecisions = source.BranchDecisions;
        target.TextEvents = source.TextEvents;
        target.Bga = source.Bga;
        target.LockedLongNoteMode = source.LockedLongNoteMode;
        target.PreviewFile = source.PreviewFile;
        target.StageFile = source.StageFile;
        target.BackBmp = source.BackBmp;
        target.Banner = source.Banner;
    }

    public static IEnumerable<string> GetSongSelectBackgroundCandidates(this IBmsBeatmap beatmap)
    {
        if (!string.IsNullOrWhiteSpace(beatmap.StageFile))
            yield return beatmap.StageFile;

        if (!string.IsNullOrWhiteSpace(beatmap.BackBmp))
            yield return beatmap.BackBmp;

        if (!string.IsNullOrWhiteSpace(beatmap.Banner))
            yield return beatmap.Banner;
    }
}
