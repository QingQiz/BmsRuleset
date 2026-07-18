using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Objects;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

public sealed record BmsParseResult(
    string? Title,
    string? Artist,
    float? PlayLevel,
    int Rank,
    double Total,
    int TickResolution,
    BmsTimingMap TimingMap,
    BmsLayoutVariant LayoutVariant,
    int TotalColumns,
    IReadOnlyDictionary<ushort, string> SampleDefinitions,
    IReadOnlyList<BmsSampleEvent> BackgroundSampleEvents,
    IReadOnlyList<BmsSampleEvent> LongNoteTailSampleEvents,
    IReadOnlyList<BmsParsedHitObject> HitObjects,
    IReadOnlyList<BmsBranchDecision> BranchDecisions,
    BmsTextEvents TextEvents,
    BmsBgaTimeline Bga,
    string? PreviewFile = null,
    string? Genre = null,
    string? Subtitle = null,
    string? SubArtist = null,
    string? Maker = null,
    string? Url = null,
    string? Email = null,
    string? Comment = null,
    BmsLongNoteMode LockedLongNoteMode = BmsLongNoteMode.Undefined,
    string? StageFile = null,
    string? BackBmp = null,
    string? Banner = null,
    double? DefaultExRank = null)
{
    internal BmsParseResult ShiftedBy(double offset)
    {
        if (offset == 0)
            return this;

        return this with
        {
            TimingMap = TimingMap.ShiftedBy(offset),
            BackgroundSampleEvents = BackgroundSampleEvents.Select(e => e with { Time = e.Time + offset }).ToArray(),
            LongNoteTailSampleEvents = LongNoteTailSampleEvents.Select(e => e with { Time = e.Time + offset }).ToArray(),
            HitObjects = HitObjects.Select(h => h with { StartTime = h.StartTime + offset }).ToArray(),
            TextEvents = TextEvents with
            {
                TextEvents = TextEvents.TextEvents.Select(e => e with { Time = e.Time + offset }).ToArray(),
            },
            Bga = Bga with
            {
                Events = Bga.Events.Select(e => e with { Time = e.Time + offset }).ToArray(),
                OpacityEvents = Bga.OpacityEvents.Select(e => e with { Time = e.Time + offset }).ToArray(),
            },
        };
    }
}

public readonly record struct BmsBranchDecision(int MaxValue, int SelectedValue);

public readonly record struct BmsParsedHitObject(
    long Tick,
    long EndTick,
    double StartTime,
    double Duration,
    int Column,
    ushort SourceChannel,
    ushort SampleKey,
    bool IsLongNote,
    bool IsMine,
    double LandmineDamagePercent,
    ushort? TailSampleKey,
    int SampleVolume = 100,
    int TailSampleVolume = 100,
    double JudgementRate = 0.75);

public sealed record BmsSampleEvent(double Time, long Tick, ushort SampleKey, int Volume);

// ReSharper disable once NotAccessedPositionalProperty.Global
public sealed record BmsTextEvent(double Time, long Tick, string Text);

public sealed record BmsTextEvents(string? MistakeText, BmsTextEvent[] TextEvents);

public sealed record BmsBgaTimeline(
    IReadOnlyDictionary<ushort, string> BitmapDefinitions,
    IReadOnlyDictionary<ushort, BmsBgaDefinition> BgaDefinitions,
    IReadOnlyList<BmsBgaEvent> Events,
    IReadOnlyList<BmsBgaOpacityEvent> OpacityEvents,
    BmsPoorBgaMode PoorMode);

public readonly record struct BmsBgaDefinition(
    ushort BitmapKey,
    int SourceX,
    int SourceY,
    int SourceWidth,
    int SourceHeight,
    int DestinationX,
    int DestinationY);

public sealed record BmsBgaEvent(double Time, long Tick, ushort DefinitionKey, BmsBgaLayer Layer, int Sequence);

public sealed record BmsBgaOpacityEvent(double Time, long Tick, BmsBgaLayer Layer, float Opacity, int Sequence);

public enum BmsBgaLayer
{
    Base,
    Poor,
    Layer1,
    Layer2,
}

public enum BmsPoorBgaMode
{
    Replace = 0,
    Add = 1,
    Off = 2,
}

public readonly record struct BmsScrollEvent(long Tick, double Factor, int Sequence);

public readonly record struct BmsSpeedEvent(long Tick, double Factor, int Sequence);
