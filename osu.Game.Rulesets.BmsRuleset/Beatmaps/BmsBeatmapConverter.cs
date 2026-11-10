using System;
using System.IO;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Conversion;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps;

/// <inheritdoc />
/// <summary>
///     Keeps native BMS beatmaps in BMS-owned object space and delegates supported foreign conversions.
/// </summary>
/// <remarks>
///     Native BMS decoding emits <see cref="BmsHitObject"/> instances directly. Foreign ruleset
///     conversion details are kept outside this converter.
/// </remarks>
public class BmsBeatmapConverter(IBeatmap beatmap, Ruleset ruleset) : BeatmapConverter<BmsHitObject>(beatmap, ruleset)
{
    public string? BranchReplayDecisions { get; set; }

    public Func<int, int>? BranchRandomValueSelector { get; init; }

    public BmsReferenceBpmMode? ReferenceBpmMode { get; init; }

    public Func<BmsLayoutVariant, int, double?, double> JudgementRateCalculator { get; set; } = BmsJudgementProfileProvider.RateForDifficulty;

    public override bool CanConvert()
    {
        if (Beatmap is IBmsBeatmap)
            return true;

        return Mania7KBeatmapConverter.CanConvert(Beatmap);
    }

    internal static bool AllowsGameplay(IBeatmapInfo source, RulesetInfo targetRuleset, bool allowConversion)
    {
        if (source.AllowGameplayWithRuleset(targetRuleset, allowConversion))
            return true;

        return allowConversion
               && targetRuleset.ShortName == Constant.SHORT_NAME
               && Mania7KBeatmapConverter.CanConvert(source);
    }

    protected override Beatmap<BmsHitObject> CreateBeatmap() => new BmsBeatmap();

    protected override Beatmap<BmsHitObject> ConvertBeatmap(IBeatmap original, CancellationToken cancellationToken)
    {
        var converted = convertToBmsBeatmap(original, cancellationToken);
        if (converted.TimingMap is not { TickResolution: > 0, BpmEvents.Count: > 0 }
            || converted.TotalColumns != BmsLayout.GetTotalColumns(converted.LayoutVariant))
            throw new InvalidDataException("BMS conversion requires a complete timing map and layout.");

        converted.TimingMap = converted.TimingMap.WithReferenceBpm(ReferenceBpmMode ?? BmsRulesetRuntime.CurrentReferenceBpmMode);

        // Precompute scroll positions once per hitobject to eliminate per-frame
        // GetScrollPositionAtTime calls in the DrawableBmsHitObject hot path.
        precomputeScrollPositions(converted);

        var difficulty = new BmsDifficultyInfo
        {
            Rank = converted.Rank,
            ExRank = converted.ExRank,
            Total = converted.Total,
            LayoutVariant = converted.LayoutVariant,
            LockedLongNoteMode = converted.LockedLongNoteMode,
        };
        difficulty.WriteToOsuDifficulty(converted.Difficulty);
        difficulty.WriteToOsuDifficulty(converted.BeatmapInfo.Difficulty);

        foreach (var note in converted.HitObjects)
        {
            note.Beatmap = converted;
            if (double.IsNaN(note.JudgementRate))
                note.JudgementRate = JudgementRateCalculator(converted.LayoutVariant, converted.Rank, converted.ExRank);
        }

        return converted;
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
        var timingMap = beatmap.TimingMap!;

        foreach (var hitObject in beatmap.HitObjects.Concat(beatmap.InvisibleNotes))
        {
            hitObject.ScrollPositionAtStartTime = timingMap.GetScrollPositionAtTime(hitObject.StartTime);
            if (hitObject is BmsLongNote ln)
                ln.ScrollPositionAtEndTime = timingMap.GetScrollPositionAtTime(hitObject.GetEndTime());
        }
    }

    private BmsBeatmap convertToBmsBeatmap(IBeatmap original, CancellationToken cancellationToken)
    {
        var converted = new BmsBeatmap
        {
            BeatmapInfo = original.BeatmapInfo.Clone(),
            ControlPointInfo = original.ControlPointInfo,
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

        if (original is BmsDecodedBeatmap { RawLines.Length: > 0 } decoded)
        {
            materialiseDecodedBeatmap(decoded, converted, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return converted;
        }

        if (Mania7KBeatmapConverter.CanConvert(original))
            Mania7KBeatmapConverter.Convert(original, converted, cancellationToken);
        else if (original is IBmsBeatmap bmsSource)
        {
            converted.CopyBmsDataFrom(bmsSource);
            foreach (var note in original.HitObjects.Cast<BmsHitObject>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                // The source can also back previews and later attempts, so only modify clones.
                var clone = note.CloneForConversion();
                converted.HitObjects.Add(clone);
            }
        }
        else if (original.HitObjects.Count == 0)
        {
            // Framework placeholders bypass CanConvert(); use the native parser's empty-chart defaults.
            converted.CopyFrom(BmsChartParser.Parse([], original.BeatmapInfo.Path));
        }
        else
            throw new InvalidDataException("Only native BMS beatmaps and supported mania 7K beatmaps can be converted.");

        converted.HitObjects = converted.HitObjects.OrderBy(h => h.StartTime).ToList();
        cancellationToken.ThrowIfCancellationRequested();
        return converted;
    }

    private void materialiseDecodedBeatmap(BmsDecodedBeatmap decoded, BmsBeatmap converted, CancellationToken cancellationToken)
    {
        var selector = BranchRandomValueSelector;

        if (!string.IsNullOrWhiteSpace(BranchReplayDecisions))
            selector = BmsChartParser.CreateReplayDecisionSelector(BmsChartParser.DeserialiseBranchDecisions(BranchReplayDecisions));

        var parseResult = BmsChartParser.Parse(decoded.RawLines, decoded.BeatmapInfo.Path, selector, JudgementRateCalculator);
        converted.CopyFrom(parseResult);
        converted.ControlPointInfo = new ControlPointInfo();
        BmsBeatmapDecoder.PopulateTiming(converted, parseResult.TimingMap.BpmEvents);

        foreach (var parsedObject in parseResult.HitObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            converted.HitObjects.Add(BmsBeatmapDecoder.CreateHitObject(parsedObject, converted));
        }
    }
}
