using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps;

/// <inheritdoc />
/// <summary>
///     Keeps BMS beatmaps in BMS-owned object space.
/// </summary>
/// <remarks>
///     This converter intentionally no longer targets osu!mania objects. The decoder already emits
///     <see cref="T:BmsHitObject">BmsHitObject</see> instances for native BMS
///     gameplay, so conversion is currently a
///     type-preserving pass-through. Later, non-BMS source beatmaps can be converted here explicitly,
///     but BMS files should never be adapted through mania as an intermediate ruleset model.
/// </remarks>
public class BmsBeatmapConverter(IBeatmap beatmap, Ruleset ruleset) : BeatmapConverter<BmsHitObject>(beatmap, ruleset)
{
    public string? BranchReplayDecisions { get; set; }

    public Func<int, int>? BranchRandomValueSelector { get; init; }

    public BmsReferenceBpmMode? ReferenceBpmMode { get; init; }

    public override bool CanConvert() =>
        Beatmap is BmsDecodedBeatmap { RawLines.Length: > 0 }
        // Song select can ask the active ruleset for statistics while its selected beatmap is stale.
        // Returning an empty BMS conversion is less disruptive than logging a conversion exception.
        || Beatmap.HitObjects.Any();

    protected override Beatmap<BmsHitObject> CreateBeatmap() => new BmsBeatmap();

    protected override Beatmap<BmsHitObject> ConvertBeatmap(IBeatmap original, CancellationToken cancellationToken)
    {
        if (tryMaterialiseDecodedBeatmap(original, out var materialised))
            original = materialised;

        var converted = convertToBmsBeatmap(original, cancellationToken);
        var hasBmsData = tryCopyBmsData(converted, original);

        converted.TimingMap ??= createFallbackTimingMap(converted);
        converted.TickResolution = converted.TimingMap.TickResolution;

        // Precompute scroll positions once per hitobject to eliminate per-frame
        // GetScrollPositionAtTime calls in the DrawableBmsHitObject hot path.
        precomputeScrollPositions(converted);

        if (converted.TotalColumns <= 0)
        {
            converted.TotalColumns = inferTotalColumns(original, converted.HitObjects);
            converted.LayoutVariant = BmsLayout.VariantFromTotalColumns(converted.TotalColumns);
        }

        new BmsDifficultyInfo
        {
            Rank = converted.Rank,
            ExRank = converted.ExRank,
            Total = converted.Total,
            KeyCount = converted.TotalColumns,
            LockedLongNoteMode = converted.LockedLongNoteMode,
        }.WriteToOsuDifficulty(converted);

        if (!hasBmsData)
            remapColumns(converted);

        stampJudgementContextOnHitObjects(converted);

        return converted;
    }

    protected override IEnumerable<BmsHitObject> ConvertHitObject(HitObject original, IBeatmap beatmap, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (original is BmsHitObject bmsObject)
            yield return bmsObject.ToTypedHitObject();
    }

    private static int inferTotalColumns(IBeatmap original, IReadOnlyList<BmsHitObject> hitObjects)
    {
        if (original is BmsBeatmap { TotalColumns: > 0 } bmsBeatmap)
            return bmsBeatmap.TotalColumns;

        var inferred = BmsLayout.InferTotalColumns(hitObjects.Select(h => h.SourceChannel), original.BeatmapInfo.Path);

        if (inferred > 0)
        {
            // Source-channel inference defaults to 6 (Bms5K) when channels are empty
            // or don't match any known layout. If notes reference columns beyond that,
            // use the max-column-based count so TotalColumns stays consistent with
            // the actual note data.
            if (hitObjects.Count > 0)
            {
                var maxColumn = hitObjects.Max(h => h.Column);
                if (maxColumn >= inferred)
                    inferred = maxColumn + 1;
            }

            // Use the inferred result only when it exceeds the explicit CS (CircleSize)
            // config. CS is the chart author's intended column count — inference from
            // visible channels should only override it when the chart uses more columns
            // than CS declares (e.g. extended-play layouts).
            var csKeyCount = BmsDifficultyInfo.GetKeyCount(original.Difficulty);
            if (inferred > csKeyCount)
                return inferred;

            return BmsLayout.IsKnownTotalColumns(csKeyCount) ? csKeyCount : inferred;
        }

        var metadataKeyCount = BmsDifficultyInfo.GetKeyCount(original.Difficulty);

        if (BmsLayout.IsKnownTotalColumns(metadataKeyCount))
            return metadataKeyCount;

        return Math.Max(BmsLayout.BMS5_KEY_COLUMNS, hitObjects.Count == 0 ? 0 : hitObjects.Max(h => h.Column) + 1);
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
        const double fallback_bpm = 130;

        var tickResolution = beatmap.TickResolution;
        var endTime = beatmap.HitObjects.Count == 0 ? 0 : beatmap.HitObjects.Max(h => h.GetEndTime());
        var endTick = (long)Math.Ceiling(Math.Max(0, endTime) * fallback_bpm * tickResolution / (60000 * 4));
        var measureCount = Math.Max(1, (int)(endTick / tickResolution) + 1);

        var measures = Enumerable.Range(0, measureCount + 1)
            .Select(i => new BmsMeasureInfo(i, (long)i * tickResolution, tickResolution, 1));

        return new BmsTimingMap(
            tickResolution,
            measures,
            [new BmsBpmEvent(0, fallback_bpm, 0)],
            [],
            [],
            []);
    }

    private static void stampJudgementContextOnHitObjects(BmsBeatmap beatmap)
    {
        foreach (var hitObject in beatmap.HitObjects)
            hitObject.Beatmap = beatmap;
    }

    /// <summary>
    ///     Precomputes <see cref="BmsHitObject.ScrollPositionAtStartTime"/> and
    ///     <see cref="BmsLongNote.ScrollPositionAtEndTime"/> for every hitobject.
    ///     This runs once during beatmap loading, eliminating the per-frame
    ///     <see cref="BmsTimingMap.GetScrollPositionAtTime"/> calls in the drawable hot path
    ///     (which would otherwise traverse the timing-point array for every hitobject, every frame).
    /// </summary>
    private static void precomputeScrollPositions(BmsBeatmap beatmap)
    {
        var timingMap = beatmap.TimingMap;
        if (timingMap == null) return;

        foreach (var hitObject in beatmap.HitObjects)
        {
            hitObject.ScrollPositionAtStartTime = timingMap.GetScrollPositionAtTime(hitObject.StartTime);
            if (hitObject is BmsLongNote ln)
                ln.ScrollPositionAtEndTime = timingMap.GetScrollPositionAtTime(hitObject.GetEndTime());
        }
    }

    private static BmsBeatmap convertToBmsBeatmap(IBeatmap original, CancellationToken cancellationToken)
    {
        var converted = new BmsBeatmap
        {
            BeatmapInfo = original.BeatmapInfo,
            ControlPointInfo = original.ControlPointInfo,
            HitObjects = original.HitObjects.OfType<BmsHitObject>().Select(h => h.ToTypedHitObject()).OrderBy(h => h.StartTime).ToList(),
            Breaks = original.Breaks,
            AudioLeadIn = original.AudioLeadIn,
            StackLeniency = original.StackLeniency,
            SpecialStyle = original.SpecialStyle,
            LetterboxInBreaks = original.LetterboxInBreaks,
            WidescreenStoryboard = original.WidescreenStoryboard,
            EpilepsyWarning = original.EpilepsyWarning,
            SamplesMatchPlaybackRate = original.SamplesMatchPlaybackRate,
            DistanceSpacing = original.DistanceSpacing,
            GridSize = original.GridSize,
            TimelineZoom = original.TimelineZoom,
            Countdown = original.Countdown,
            CountdownOffset = original.CountdownOffset,
            Bookmarks = original.Bookmarks,
            BeatmapVersion = original.BeatmapVersion,
        };

        cancellationToken.ThrowIfCancellationRequested();
        return converted;
    }

    private static bool tryCopyBmsData(BmsBeatmap converted, IBeatmap source)
    {
        if (source is not IBmsBeatmap bmsSource)
            return false;

        converted.CopyBmsDataFrom(bmsSource);
        return true;
    }

    private bool tryMaterialiseDecodedBeatmap(IBeatmap original, out IBeatmap materialised)
    {
        materialised = original;

        if (original is not BmsDecodedBeatmap { RawLines.Length: > 0 } decoded)
            return false;

        var selector = BranchRandomValueSelector;

        if (!string.IsNullOrWhiteSpace(BranchReplayDecisions))
            selector = BmsChartParser.CreateReplayDecisionSelector(BmsChartParser.DeserialiseBranchDecisions(BranchReplayDecisions));

        var parseResult = BmsTimelineLeadIn.Apply(
            BmsChartParser.Parse(decoded.RawLines, decoded.BeatmapInfo.Path, selector, ReferenceBpmMode ?? BmsRulesetRuntime.CurrentReferenceBpmMode));
        var beatmap = new BmsDecodedBeatmap
        {
            BeatmapInfo = decoded.BeatmapInfo,
            ControlPointInfo = new ControlPointInfo(),
            Breaks = decoded.Breaks,
            AudioLeadIn = decoded.AudioLeadIn,
            StackLeniency = decoded.StackLeniency,
            SpecialStyle = decoded.SpecialStyle,
            LetterboxInBreaks = decoded.LetterboxInBreaks,
            WidescreenStoryboard = decoded.WidescreenStoryboard,
            EpilepsyWarning = decoded.EpilepsyWarning,
            SamplesMatchPlaybackRate = decoded.SamplesMatchPlaybackRate,
            DistanceSpacing = decoded.DistanceSpacing,
            GridSize = decoded.GridSize,
            TimelineZoom = decoded.TimelineZoom,
            Countdown = decoded.Countdown,
            CountdownOffset = decoded.CountdownOffset,
            Bookmarks = decoded.Bookmarks,
            BeatmapVersion = decoded.BeatmapVersion,
            RawLines = decoded.RawLines,
        };

        beatmap.CopyFrom(parseResult);
        BmsBeatmapDecoder.PopulateTiming(beatmap, parseResult.TimingMap.BpmEvents);

        foreach (var parsedObject in parseResult.HitObjects)
            beatmap.HitObjects.Add(BmsBeatmapDecoder.CreateHitObject(parsedObject, beatmap));

        materialised = beatmap;
        return true;
    }
}
