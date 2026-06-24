using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps;

public class BmsBeatmap : Beatmap<BmsHitObject>, IBmsBeatmap
{
    public int TickResolution { get; set; } = 192;

    public int TotalColumns { get; set; }

    public BmsLayoutVariant LayoutVariant { get; set; } = BmsLayoutVariant.Bms5K;

    /// <summary>BMS #RANK value: 0=Very Hard, 1=Hard, 2=Normal (default), 3=Easy, 4=Very Easy.</summary>
    public int Rank { get; set; } = 2;

    /// <summary>
    ///     BMS #TOTAL value: gauge recovery coefficient.
    ///     Zero means the default formula applies.
    /// </summary>
    public double Total { get; set; }

    public BmsTimingMap? TimingMap { get; set; }

    public IReadOnlyDictionary<ushort, string> SampleDefinitions { get; set; } = new Dictionary<ushort, string>();

    public IReadOnlyList<BmsSampleEvent> BackgroundSampleEvents { get; set; } = [];

    public IReadOnlyList<BmsSampleEvent> LongNoteTailSampleEvents { get; set; } = [];

    public IReadOnlyList<BmsBranchDecision> BranchDecisions { get; set; } = [];

    public BmsTextEvents TextEvents { get; set; } = new(string.Empty, []);

    public BmsLongNoteMode LockedLongNoteMode { get; set; }

    public string? PreviewFile { get; set; }

    public override IEnumerable<BeatmapStatistic> GetStatistics()
    {
        var notes = HitObjects.Count(h => !h.IsMine && !isScratch(h));
        var holdNotes = HitObjects.Count(h => h.IsLongNote && !isScratch(h));
        var scratch = HitObjects.Count(isScratch);
        var mines = HitObjects.Count(h => h.IsMine);
        double total = notes + holdNotes + scratch + mines;
        total = Math.Max(total, 1);

        return filterStatistics([
            new BeatmapStatistic
            {
                Name = "Notes",
                CreateIcon = () => new BeatmapStatisticIcon(BeatmapStatisticsIconType.Circles),
                Content = notes.ToString(),
                BarDisplayLength = perc(notes),
            },
            new BeatmapStatistic
            {
                Name = "Hold Notes",
                CreateIcon = () => new BeatmapStatisticIcon(BeatmapStatisticsIconType.Sliders),
                Content = holdNotes.ToString(),
                BarDisplayLength = perc(holdNotes),
            },
            new BeatmapStatistic
            {
                Name = "Scratches",
                CreateIcon = () => new BeatmapStatisticIcon(BeatmapStatisticsIconType.Spinners),
                Content = scratch.ToString(),
                BarDisplayLength = perc(scratch),
            },
            new BeatmapStatistic
            {
                Name = "Mines",
                CreateIcon = () => new BeatmapStatisticIcon(BeatmapStatisticsIconType.Circles),
                Content = mines.ToString(),
                BarDisplayLength = perc(mines),
            },
        ]);

        bool isScratch(BmsHitObject h) => BmsLayout.IsScratchColumn(h.Column, LayoutVariant);
        float perc(int x) => (float)(x / total);
    }

    private IEnumerable<BeatmapStatistic> filterStatistics(IEnumerable<BeatmapStatistic> statistics)
    {
        return statistics.Where(x => !string.IsNullOrWhiteSpace(x.Content) && x.Content != "0");
    }
}
