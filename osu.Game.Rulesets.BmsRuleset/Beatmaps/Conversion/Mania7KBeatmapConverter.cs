using System;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps.Conversion;

internal sealed class Mania7KBeatmapConverter : IBmsForeignBeatmapConverter
{
    private const int ruleset_online_id = 3;
    private const int key_count = 7;
    private const int normal_rank = 2;

    private static readonly ushort[] source_channels =
    [
        BmsChartParser.Enc("11"),
        BmsChartParser.Enc("12"),
        BmsChartParser.Enc("13"),
        BmsChartParser.Enc("14"),
        BmsChartParser.Enc("15"),
        BmsChartParser.Enc("18"),
        BmsChartParser.Enc("19"),
    ];

    public bool CanConvert(IBeatmapInfo beatmapInfo) =>
        beatmapInfo.Ruleset.OnlineID == ruleset_online_id
        && (int)Math.Round(beatmapInfo.Difficulty.CircleSize) == key_count;

    public BmsDifficultyInfo GetConvertedDifficultyInfo(IBeatmapInfo beatmapInfo) =>
        createDifficultyInfo(beatmapInfo.TotalObjectCount);

    public bool CanConvert(IBeatmap beatmap) =>
        CanConvert(beatmap.BeatmapInfo) && beatmap.HitObjects.All(isConvertibleHitObject);

    public void Convert(IBeatmap source, BmsBeatmap target, CancellationToken cancellationToken)
    {
        // BME 7K reserves column zero for scratch, while mania's seven lanes map to columns 1-7.
        var difficulty = createDifficultyInfo(source.HitObjects.Count);

        target.TotalColumns = difficulty.KeyCount;
        target.LayoutVariant = BmsLayout.VariantFromTotalColumns(difficulty.KeyCount);
        target.Rank = difficulty.Rank;
        target.ExRank = difficulty.ExRank;
        target.Total = difficulty.Total;
        target.LockedLongNoteMode = difficulty.LockedLongNoteMode;
        target.TimingMap = ManiaTimingMapConverter.Create(source);

        foreach (var original in source.HitObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            target.HitObjects.Add(convertHitObject(original));
        }
    }

    private static BmsDifficultyInfo createDifficultyInfo(int noteCount) => new()
    {
        Rank = normal_rank,
        Total = BmsGaugeCalculator.CalculateDefaultTotal(noteCount),
        KeyCount = BmsLayout.BME7_KEY_COLUMNS,
        LockedLongNoteMode = BmsLongNoteMode.ChargeNote,
    };

    private static BmsHitObject convertHitObject(HitObject original)
    {
        var maniaColumn = getColumn(original);

        if (maniaColumn is < 0 or >= key_count)
            throw new InvalidOperationException($"Invalid mania 7K column for {original.GetType().FullName}.");

        BmsHitObject converted = original is IHasDuration hasDuration
            ? new BmsLongNote { Duration = hasDuration.Duration }
            : new BmsNote();

        converted.StartTime = original.StartTime;
        converted.Column = maniaColumn + 1;
        converted.SourceChannel = source_channels[maniaColumn];
        converted.Samples = original.Samples.ToList();
        return converted;
    }

    private static int getColumn(HitObject hitObject) => hitObject switch
    {
        IHasColumn hasColumn => hasColumn.Column,
        IHasXPosition hasXPosition => Math.Clamp((int)MathF.Floor(hasXPosition.X / (512f / key_count)), 0, key_count - 1),
        _ => -1,
    };

    private static bool isConvertibleHitObject(HitObject hitObject) => hitObject switch
    {
        IHasColumn hasColumn => hasColumn.Column is >= 0 and < key_count,
        IHasXPosition => true,
        _ => false,
    };
}
