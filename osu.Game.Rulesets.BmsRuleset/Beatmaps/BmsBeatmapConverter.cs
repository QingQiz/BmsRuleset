using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps;

/// <inheritdoc />
/// <summary>
///     Keeps BMS beatmaps in BMS-owned object space.
/// </summary>
/// <remarks>
///     This converter intentionally no longer targets osu!mania objects. The decoder already emits
///     <see cref="T:osu.Game.Rulesets.BmsRuleset.Objects.BmsHitObject">BmsHitObject</see> instances for native BMS
///     gameplay, so conversion is currently a
///     type-preserving pass-through. Later, non-BMS source beatmaps can be converted here explicitly,
///     but BMS files should never be adapted through mania as an intermediate ruleset model.
/// </remarks>
public class BmsBeatmapConverter(IBeatmap beatmap, Ruleset ruleset) : BeatmapConverter<BmsHitObject>(beatmap, ruleset)
{
    public override bool CanConvert() =>
        Beatmap.HitObjects.Any() && Beatmap.HitObjects.All(h => h is BmsHitObject);

    protected override Beatmap<BmsHitObject> CreateBeatmap() => new BmsBeatmap();

    protected override Beatmap<BmsHitObject> ConvertBeatmap(IBeatmap original, CancellationToken cancellationToken)
    {
        var converted = (BmsBeatmap)base.ConvertBeatmap(original, cancellationToken);
        var hasBmsData = tryCopyBmsData(converted);

        if (!hasBmsData)
            populateFallbackSampleDefinitions(converted);

        converted.TimingMap ??= createFallbackTimingMap(converted);
        converted.TickResolution = converted.TimingMap.TickResolution;

        if (converted.TotalColumns <= 0)
        {
            converted.TotalColumns = inferTotalColumns(original, converted.HitObjects);
            converted.LayoutVariant = BmsLayout.VariantFromTotalColumns(converted.TotalColumns);
        }

        converted.Difficulty.CircleSize = converted.TotalColumns;
        converted.BeatmapInfo.Difficulty.CircleSize = converted.TotalColumns;

        if (!hasBmsData)
            remapColumns(converted);

        // Stamp the chart-level #RANK onto every hit object so CreateHitWindows() has it.
        stampRankOnHitObjects(converted);

        return converted;
    }

    protected override IEnumerable<BmsHitObject> ConvertHitObject(HitObject original, IBeatmap beatmap, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (original is BmsHitObject bmsObject)
            yield return bmsObject;
    }

    private static int inferTotalColumns(IBeatmap original, IReadOnlyList<BmsHitObject> hitObjects)
    {
        if (original is BmsBeatmap { TotalColumns: > 0 } bmsBeatmap)
            return bmsBeatmap.TotalColumns;

        var metadataKeyCount = (int)Math.Round(original.Difficulty.CircleSize);

        if (BmsLayout.IsKnownTotalColumns(metadataKeyCount))
            return metadataKeyCount;

        var inferred = BmsLayout.InferTotalColumns(hitObjects.Select(h => h.SourceChannel), original.BeatmapInfo.Path);

        if (inferred > 0)
            return inferred;

        return Math.Max(BmsLayout.BMS5_KEY_COLUMNS, hitObjects.Count == 0 ? 0 : hitObjects.Max(h => h.Column) + 1);
    }

    private static void populateFallbackSampleDefinitions(BmsBeatmap beatmap)
    {
        beatmap.SampleDefinitions = beatmap.HitObjects
            .Where(h => h.SampleKey.Length > 0 && h.SamplePath.Length > 0)
            .GroupBy(h => h.SampleKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().SamplePath, StringComparer.OrdinalIgnoreCase);
    }

    private static void remapColumns(BmsBeatmap beatmap)
    {
        foreach (var hitObject in beatmap.HitObjects)
        {
            if (BmsLayout.TryMapPlayableChannel(hitObject.SourceChannel, beatmap.TotalColumns, out var column))
                hitObject.Column = column;
        }
    }

    private static BmsTimingMap createFallbackTimingMap(BmsBeatmap beatmap)
    {
        var tickResolution = beatmap.TickResolution;
        var endTick = beatmap.HitObjects.Count == 0 ? tickResolution : beatmap.HitObjects.Max(h => Math.Max(h.TickInfo.Tick, h.TickInfo.EndTick));
        var measureCount = Math.Max(1, (int)(endTick / tickResolution) + 1);

        var measures = Enumerable.Range(0, measureCount + 1)
            .Select(i => new BmsMeasureInfo(i, (long)i * tickResolution, tickResolution, 1));

        return new BmsTimingMap(
            tickResolution,
            measures,
            [new BmsBpmEvent(0, 130, 0)],
            []);
    }

    private static void stampRankOnHitObjects(BmsBeatmap beatmap)
    {
        foreach (var hitObject in beatmap.HitObjects)
            hitObject.BmsRank = beatmap.Rank;
    }

    private bool tryCopyBmsData(BmsBeatmap converted)
    {
        if (Beatmap is not IBmsBeatmap bmsSource)
            return false;

        converted.CopyBmsDataFrom(bmsSource);
        return true;
    }
}
