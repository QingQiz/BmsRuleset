using System.IO;
using System.Linq;
using System.Text;
using osu.Game.Beatmaps;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

/// <summary>
/// Factories for the synthetic and chart-derived <see cref="BmsBeatmap"/>s
/// used by the BMS visual test scenes.
/// </summary>
public static partial class BmsTestBeatmaps
{
    public const double INIT_HEALTH = 0.2;
    public const double FIRST_NOTE_TIME = 2500;
    public const double NORM_SCENARIO_START_TIME = FIRST_NOTE_TIME + 6000;
    public const double LN_SCENARIO_START_TIME = FIRST_NOTE_TIME;
    public const double LN_SCENARIO_SPACING = 700;
    public const double LN_SCENARIO_DURATION = 800;

    /// <summary>
    /// Creates a synthetic <see cref="BmsBeatmap"/> with normal notes (varied columns and timing),
    /// long-note scenarios (early/late press, mid release, etc.), and landmines.
    /// Designed so a replay with deliberate timing offsets can produce every hit result.
    /// </summary>
    public static BmsBeatmap CreateBeatmap()
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Rank = 2,
            Total = 160,
        };

        int[] pattern =
        [
            0, 2, 4, 6, 1, 3, 5, 7,
            0, 4, 2, 6, 3, 7, 1, 5,
            0, 1, 2, 3, 4, 5, 6, 7,
            7, 6, 5, 4, 3, 2, 1, 0,
            0, 2, 4, 6, 1, 3, 5, 7,
            0, 4, 2, 6, 3, 7, 1, 5,
        ];

        for (var i = 0; i < pattern.Length; i++)
        {
            beatmap.HitObjects.Add(new BmsHitObject
            {
                StartTime = NORM_SCENARIO_START_TIME + i * 125,
                Column = pattern[i],
            });
        }

        int[] lnScenarioColumns = [0, 7, 3, 5, 2, 6, 1];

        for (var i = 0; i < lnScenarioColumns.Length; i++)
        {
            beatmap.HitObjects.Add(new BmsHitObject
            {
                StartTime = LN_SCENARIO_START_TIME + i * LN_SCENARIO_SPACING,
                Column = lnScenarioColumns[i],
                IsLongNote = true,
                Duration = LN_SCENARIO_DURATION,
            });
        }

        beatmap.HitObjects.Add(new BmsHitObject { StartTime = FIRST_NOTE_TIME + 6000, Column = 6, IsMine = true, LandmineDamagePercent = 2.5 });
        beatmap.HitObjects.Add(new BmsHitObject { StartTime = FIRST_NOTE_TIME + 7000, Column = 1, IsMine = true, LandmineDamagePercent = 2.5 });
        beatmap.HitObjects.Add(new BmsHitObject { StartTime = FIRST_NOTE_TIME + 8250, Column = 4, IsMine = true, LandmineDamagePercent = 2.5 });
        beatmap.HitObjects.Add(new BmsHitObject { StartTime = FIRST_NOTE_TIME + 9500, Column = 0, IsMine = true, LandmineDamagePercent = 2.5 });
        beatmap.HitObjects.Add(new BmsHitObject { StartTime = FIRST_NOTE_TIME + 11000, Column = 7, IsMine = true, LandmineDamagePercent = 2.5 });

        return beatmap;
    }

    /// <summary>
    /// Decodes a BMS chart string into a <see cref="BmsBeatmap"/>.
    /// </summary>
    public static BmsBeatmap CreateBeatmapFromChart(string chart)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(chart));
        using var reader = new LineBufferedReader(stream);
        var decoded = new BmsBeatmapDecoder().Decode(reader);
        return (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset()).Convert();
    }

    /// <summary>
    /// Sets up the <see cref="BeatmapInfo"/> on a beatmap created by one of the factory methods.
    /// </summary>
    /// <param name="beatmap">Target beatmap.</param>
    /// <param name="ruleset">Owning ruleset info.</param>
    /// <param name="endPadding">Milliseconds added after the last hit object's end time to determine <see cref="BeatmapInfo.Length"/>.</param>
    /// <param name="bpm">Display BPM stamped onto <see cref="BeatmapInfo.BPM"/>.</param>
    /// <param name="overallDifficulty">Display overall difficulty.</param>
    /// <param name="drainRate">Display drain rate.</param>
    public static void SetupBeatmapInfo(
        BmsBeatmap beatmap,
        RulesetInfo ruleset,
        double endPadding = 2500,
        double bpm = 130,
        float overallDifficulty = 6,
        float drainRate = 5)
    {
        beatmap.BeatmapInfo.Ruleset = ruleset;
        beatmap.BeatmapInfo.Difficulty.CircleSize = beatmap.TotalColumns;
        beatmap.BeatmapInfo.Difficulty.OverallDifficulty = overallDifficulty;
        beatmap.BeatmapInfo.Difficulty.DrainRate = drainRate;
        beatmap.BeatmapInfo.BPM = bpm;
        beatmap.BeatmapInfo.Length = (int)(beatmap.HitObjects.Max(h => h.EndTime) + endPadding);
    }
}
