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
    public double Total { get; set; } = 0;

    public BmsTimingMap? TimingMap { get; set; }

    public IReadOnlyDictionary<string, string> SampleDefinitions { get; set; } = new Dictionary<string, string>();

    public IReadOnlyList<BmsSampleEvent> BackgroundSampleEvents { get; set; } = [];

    public IReadOnlyList<BmsSampleEvent> LongNoteTailSampleEvents { get; set; } = [];

    public override IEnumerable<BeatmapStatistic> GetStatistics()
    {
        var notes = HitObjects.Count;
        var holdNotes = HitObjects.Count(h => h.IsLongNote);

        return
        [
            new BeatmapStatistic
            {
                Name = "Notes",
                CreateIcon = () => new BeatmapStatisticIcon(BeatmapStatisticsIconType.Circles),
                Content = notes.ToString(),
            },
            new BeatmapStatistic
            {
                Name = "Hold Notes",
                CreateIcon = () => new BeatmapStatisticIcon(BeatmapStatisticsIconType.Sliders),
                Content = holdNotes.ToString(),
            },
        ];
    }
}
