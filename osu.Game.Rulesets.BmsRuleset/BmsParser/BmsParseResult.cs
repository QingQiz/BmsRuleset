using System.Collections.Generic;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

public sealed record BmsParseResult(
    string? Title,
    string? Artist,
    string? Source,
    float? OverallDifficulty,
    int TickResolution,
    BmsTimingMap TimingMap,
    BmsLayoutVariant LayoutVariant,
    int TotalColumns,
    IReadOnlyDictionary<string, string> SampleDefinitions,
    IReadOnlyList<BmsSampleEvent> BackgroundSampleEvents,
    IReadOnlyList<BmsParsedHitObject> HitObjects);

public readonly record struct BmsParsedHitObject(
    long Tick,
    long EndTick,
    double StartTime,
    double Duration,
    int Column,
    string SourceChannel,
    string SampleKey,
    string SamplePath,
    bool IsLongNote);

public sealed record BmsSampleEvent(double Time, long Tick, string SampleKey);
