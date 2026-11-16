using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

public sealed record BmsParseResult(
    string? Title,
    string? Artist,
    float? PlayLevel,
    int Rank,
    double? Total,
    BmsTimingMap TimingMap,
    BmsLayoutVariant LayoutVariant,
    int TotalColumns,
    IReadOnlyDictionary<int, BmsSampleDefinition> SampleDefinitions,
    IReadOnlyList<BmsSampleEvent> BackgroundSampleEvents,
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
    public IReadOnlyList<BmsParsedHitObject> InvisibleNotes { get; init; } = [];
    /// <summary>Source-provided chart label; set-relative inference and display fallbacks belong to loading.</summary>
    public string? ExplicitDifficultyName { get; init; }
}

public readonly record struct BmsBranchDecision(int MaxValue, int SelectedValue);

public readonly record struct BmsParsedHitObject(
    long Tick,
    double StartTime,
    double Duration,
    int Column,
    int SampleKey,
    bool IsLongNote,
    bool IsMine,
    double LandmineDamagePercent,
    int? TailSampleKey,
    int SampleVolume = 100,
    int TailSampleVolume = 100,
    double? ExRank = null)
{
    public IReadOnlyList<int> AdditionalSampleKeys { get; init; } = [];
    public IReadOnlyList<int> AdditionalTailSampleKeys { get; init; } = [];
    public BmsLongNoteMode LongNoteMode { get; init; }
}

public sealed record BmsSampleEvent(double Time, int SampleKey, int Volume);

public sealed record BmsTextEvent(double Time, string Text);

public sealed record BmsTextEvents(string? MistakeText, BmsTextEvent[] TextEvents);

public sealed record BmsBgaTimeline(
    IReadOnlyDictionary<int, string> BitmapDefinitions,
    IReadOnlyDictionary<int, BmsBgaDefinition> BgaDefinitions,
    IReadOnlyList<BmsBgaEvent> Events,
    IReadOnlyList<BmsBgaOpacityEvent> OpacityEvents,
    BmsPoorBgaMode PoorMode);

public readonly record struct BmsBgaDefinition(
    int BitmapKey,
    int SourceX,
    int SourceY,
    int SourceWidth,
    int SourceHeight,
    int DestinationX,
    int DestinationY);

public sealed record BmsBgaEvent(double Time, long Tick, int DefinitionKey, BmsBgaLayer Layer, int Sequence)
{
    public double EndTime { get; init; } = double.PositiveInfinity;

    public BmsBgaAnimation? Animation { get; init; }
}

public sealed record BmsBgaAnimation(IReadOnlyList<BmsBgaFrame[]> Variants, BmsBgaTrigger Trigger, int Interval);

public readonly record struct BmsBgaFrame(double Offset, int? DefinitionKey);

public enum BmsBgaTrigger
{
    Always,
    Play,
    Miss,
}

public sealed record BmsBgaOpacityEvent(double Time, long Tick, BmsBgaLayer Layer, float Opacity, int Sequence);

public enum BmsBgaLayer
{
    Base,
    Poor,
    Layer1,
    Layer2,
    Background,
    Title,
}

public enum BmsPoorBgaMode
{
    Replace = 0,
    Add = 1,
    Off = 2,
}

public readonly record struct BmsScrollEvent(long Tick, double Factor, int Sequence);

public readonly record struct BmsSpeedEvent(long Tick, double Factor, int Sequence);
