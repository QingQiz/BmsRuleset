using System;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps.Conversion;

internal static class Mania7KBeatmapConverter
{
    private const int ruleset_online_id = 3;
    private const int key_count = 7;
    private const int normal_rank = 2;

    public static bool CanConvert(IBeatmapInfo beatmapInfo) =>
        beatmapInfo.Ruleset.OnlineID == ruleset_online_id
        && (int)Math.Round(beatmapInfo.Difficulty.CircleSize) == key_count;

    public static BmsDifficultyInfo GetConvertedDifficultyInfo(IBeatmapInfo beatmapInfo) =>
        createDifficultyInfo(beatmapInfo.TotalObjectCount);

    public static bool CanConvert(IBeatmap beatmap) =>
        CanConvert(beatmap.BeatmapInfo) && beatmap.HitObjects.All(isConvertibleHitObject);

    public static void Convert(IBeatmap source, BmsBeatmap target, CancellationToken cancellationToken)
    {
        // BME 7K reserves column zero for scratch, while mania's seven lanes map to columns 1-7.
        var difficulty = createDifficultyInfo(source.HitObjects.Count);

        target.TotalColumns = difficulty.KeyCount;
        target.LayoutVariant = difficulty.LayoutVariant;
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
        LayoutVariant = BmsLayoutVariant.Bme7K,
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
