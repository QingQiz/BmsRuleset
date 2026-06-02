using System.Collections.Generic;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

public sealed record BmsParseResult(
    string? Title,
    string? Artist,
    string? Source,
    float? PlayLevel,
    int Rank,
    double Total,
    int TickResolution,
    BmsTimingMap TimingMap,
    BmsLayoutVariant LayoutVariant,
    int TotalColumns,
    IReadOnlyDictionary<string, string> SampleDefinitions,
    IReadOnlyList<BmsSampleEvent> BackgroundSampleEvents,
    IReadOnlyList<BmsSampleEvent> LongNoteTailSampleEvents,
    IReadOnlyList<BmsParsedHitObject> HitObjects,
    IReadOnlyList<BmsBranchDecision> BranchDecisions,
    BmsTextEvents TextEvents);

public readonly record struct BmsBranchDecision(int MaxValue, int SelectedValue);

public readonly record struct BmsParsedHitObject(
    long Tick,
    long EndTick,
    double StartTime,
    double Duration,
    int Column,
    string SourceChannel,
    string SampleKey,
    string SamplePath,
    bool IsLongNote,
    bool IsMine,
    double LandmineDamagePercent,
    string LandmineExplosionSamplePath);

public sealed record BmsSampleEvent(double Time, long Tick, string SampleKey);

public sealed record BmsTextEvent(double Time, long Tick, string Text);

public sealed record BmsTextEvents(string? MistakeText, BmsTextEvent[] TextEvents);
