using System.Collections.Immutable;
using RhythmEngine.Core;

namespace RhythmEngine.Profiles.Bms;

/// <summary>BMS RANK timing difficulty, ordered from strictest to most lenient; each client profile supplies its own window tables.</summary>
public enum BmsRank
{
    /// <summary>The strictest rank, using the profile's narrowest timing windows.</summary>
    VeryHard,

    /// <summary>A strict rank with narrower timing windows than Normal.</summary>
    Hard,

    /// <summary>The standard rank and the default in BmsOptions.</summary>
    Normal,

    /// <summary>A lenient rank with wider timing windows than Normal.</summary>
    Easy,

    /// <summary>The most lenient rank; supported by beatoraja, but rejected by both LR2 profiles.</summary>
    VeryEasy,
}

/// <summary>Selects BMS initial health, judgement deltas, failure conditions and the final clear requirement.</summary>
/// <remarks>Health uses a 0..100 scale. Recovery gauges check final health; other gauges fail at zero. Delta tables depend on the client profile.</remarks>
public enum BmsGauge
{
    /// <summary>An assisted recovery gauge starting at 20, with a floor of 2 and a final clear threshold of 60.</summary>
    AssistEasy,

    /// <summary>A lenient recovery gauge starting at 20, with a floor of 2 and a clear threshold of 80; deltas are more forgiving than Normal.</summary>
    Easy,

    /// <summary>The standard recovery gauge, starting at 20 with a floor of 2 and a final clear threshold of 80.</summary>
    Normal,

    /// <summary>A survival gauge starting at 100 and failing at zero, with reduced damage at low health; recovery depends on the client and chart.</summary>
    Hard,

    /// <summary>A stricter survival gauge starting at 100, with larger penalties and no Hard-style damage reduction at low health.</summary>
    ExHard,

    /// <summary>A BAD or POOR removes 100 health and fails the gauge; empty POOR uses a separate damage value.</summary>
    Hazard,

    /// <summary>A grade/course survival gauge starting at 100, with its own deltas and reduced damage at low health; fails at zero.</summary>
    Grade,

    /// <summary>A harder grade gauge with stricter deltas than Grade; low-health damage reduction depends on the client profile.</summary>
    GradeHard,

    /// <summary>A grade gauge with still larger penalties, no low-health damage reduction and failure at zero.</summary>
    GradeExHard,
}

/// <summary>Selects a target when one input can judge multiple notes, without changing their individual timing windows.</summary>
/// <remarks>Active hold interactions take priority. Playable targets precede empty POOR targets, and shared-input candidates are dispatched separately.</remarks>
public enum BmsSelection
{
    /// <summary>Resolved by profile creation: beatoraja uses Combo, while the LR2 compatibility profile uses Oldest.</summary>
    Default,

    /// <summary>Scans in time order, replacing a candidate past its late GOOD boundary when a later candidate has reached its early GOOD boundary.</summary>
    Combo,

    /// <summary>Selects the smallest absolute timing error; ties retain the earlier note, then the lower chart index.</summary>
    Nearest,

    /// <summary>Keeps the earliest playable candidate, breaking time ties by chart index, without skipping ahead for a better judgement.</summary>
    Oldest,

    /// <summary>Scans like Combo but uses GREAT boundaries to decide whether a later note should receive the input.</summary>
    Score,
}

public sealed record BmsOptions(
    BmsRank Rank = BmsRank.Normal,
    BmsGauge Gauge = BmsGauge.Normal,
    double Total = 260,
    BmsSelection Selection = BmsSelection.Default);

public enum BmsJudgementMode
{
    FiveKey,
    SevenKey,
    Pms,
    Keyboard,
}

[Flags]
public enum BmsJudgementModifiers
{
    None = 0,
    Lr2oraja = 1,
    NoBad = 2,
    NoGood = 4,
    NoGreat = 8,
}

/// <summary>A timing definition shared by execution, previews and framework adapters. All times are microseconds.</summary>
public sealed class BmsTimingDefinition(TimingWindows windows, long passiveDeadline, long? longNoteHeadDeadline = null)
{
    public TimingWindows Windows { get; } = windows;

    // Removing a hit result does not remove its passive timeout.
    public long PassiveDeadline { get; } = passiveDeadline;

    public long? LongNoteHeadDeadline { get; } = longNoteHeadDeadline;

    public TimingWindow? WindowFor(ResultId result)
    {
        foreach (var window in Windows.Entries)
        {
            if (window.Result == result) return window;
        }

        return null;
    }
}

/// <summary>Normalized per-object policy after chart and mod configuration.</summary>
public sealed record BmsRuleOptions(
    bool RetainResolvedHeads = true,
    bool ImmediateBadHead = false,
    bool ShareLateBad = false,
    bool NonConsumingBad = false,
    long? HeadDeadline = null,
    long? TailDeadline = null,
    long ReleaseGrace = 0,
    TimerOrder MineOrder = TimerOrder.AfterInputState,
    TimerOrder PassiveOrder = TimerOrder.BeforeInput);

public enum BmsGaugeAlgorithm
{
    Total,
    LimitIncrement,
    ModifyDamage,
    Fixed,
}

public enum BmsGaugeProfileFamily
{
    FiveKeys,
    SevenKeys,
    Pms,
    Keyboard,
    Lr2,
}

public readonly record struct BmsGaugeGutsRule(
    double HealthThreshold,
    double DamageMultiplier
);

public sealed record BmsGaugeProfile(
    BmsGaugeAlgorithm Algorithm,
    double InitialHealth,
    double MaxHealth,
    double ClearThreshold,
    double PerfectGain,
    double GreatGain,
    double GoodGain,
    double BadDelta,
    double PoorDelta,
    double EmptyPoorDelta,
    ImmutableArray<BmsGaugeGutsRule> GutsRules
)
{
    public Func<int, double>? DefaultTotal { get; init; }

    public Func<double, int, double>? DamageScale { get; init; }

    public double FailureThreshold { get; init; }

    public bool IsFailed(double health) => health <= 0 || health < FailureThreshold;

    public bool IsCleared(HealthState health) => !health.HasFailed && health.Value >= ClearThreshold;

    public double MinHealth => Algorithm == BmsGaugeAlgorithm.Total ? 0.02 : 0;
}

/// <summary>The four gauges in LR2 beta3 100201, preserving its two-point health quantisation and distinct damage calculations.</summary>
public enum Lr2Beta3Gauge
{
    /// <summary>The standard recovery gauge, starting at 20 with a floor of 2 and a final clear threshold of 80.</summary>
    Normal,

    /// <summary>A survival gauge starting at 100 and failing below 2; damage depends on TOTAL and note count, with a reduction at low health.</summary>
    Hard,

    /// <summary>A BAD or POOR depletes the gauge; successful judgements do not recover health and empty POOR causes no damage.</summary>
    Hazard,

    /// <summary>A recovery gauge with Normal's initial value, floor and clear threshold, but more recovery and less damage.</summary>
    Easy,
}

public sealed record Lr2Beta3Options(BmsRank Rank = BmsRank.Normal, Lr2Beta3Gauge Gauge = Lr2Beta3Gauge.Normal, int Total = 260);

/// <summary>Result identities emitted and consumed by the Bms profiles; ordering and scoring remain configured.</summary>
public static class BmsResults
{
    public static readonly ResultId PGREAT = new("pgreat");
    public static readonly ResultId GREAT = new("great");
    public static readonly ResultId GOOD = new("good");
    public static readonly ResultId BAD = new("bad");
    public static readonly ResultId POOR = new("poor");
    public static readonly ResultId EMPTY_POOR = new("empty-poor");
    public static readonly ResultId MINE_HIT = new("mine-hit");
    public static readonly ResultId MINE_AVOIDED = new("mine-avoided");
    public static readonly ResultId SUSTAIN_GAIN = new("sustain-gain");
    public static readonly ResultId SUSTAIN_LOSS = new("sustain-loss");
}
