using System.Collections.Generic;

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
    string? PreviewFile = null,
    string? Genre = null,
    string? Subtitle = null,
    string? SubArtist = null,
    string? Maker = null,
    string? Url = null,
    string? Email = null,
    string? Comment = null);

public readonly record struct BmsBranchDecision(int MaxValue, int SelectedValue);

public readonly record struct BmsParsedHitObject(
    long Tick,
    long EndTick,
    double StartTime,
    double Duration,
    int Column,
    ushort SourceChannel,
    ushort SampleKey,
    string SamplePath,
    bool IsLongNote,
    bool IsMine,
    double LandmineDamagePercent,
    string LandmineExplosionSamplePath,
    ushort TailSampleKey,
    string TailSamplePath);

public sealed record BmsSampleEvent(double Time, long Tick, ushort SampleKey);

// ReSharper disable once NotAccessedPositionalProperty.Global
public sealed record BmsTextEvent(double Time, long Tick, string Text);

public sealed record BmsTextEvents(string? MistakeText, BmsTextEvent[] TextEvents);

public readonly record struct BmsScrollEvent(long Tick, double Factor, int Sequence);

public readonly record struct BmsSpeedEvent(long Tick, double Factor, int Sequence);
