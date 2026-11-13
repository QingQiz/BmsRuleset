using System.Collections.Immutable;

namespace RhythmEngine.Profiles.Patterns;

public enum HoldEndMode
{
    Release,
    Automatic,
    ReleaseOrAutomatic,
}

public enum HoldDropMode
{
    Resolve,
    Recover,
    Grace,
}

public enum HoldResolution
{
    Release,
    Automatic,
    Timeout,
    Drop,
    MissedHead,
    Head,
}

public readonly record struct HoldEvaluation(
    ChartNote Note,
    string HeadResult,
    long? HeadOffset,
    string TailResult,
    long? TailOffset,
    bool Broken,
    HoldResolution Resolution);

/// <summary>Integrates held/released time with a signed accumulator, emitting on threshold crossings.</summary>
public sealed record HoldBalance(long Threshold, string GainResult, string LossResult, ImmutableHashSet<string> SustainingTailResults);

public sealed record HoldConfiguration(
    TimingWindows HeadWindows,
    TimingWindows TailWindows,
    long HeadDeadline,
    long TailDeadline,
    string MissResult,
    string AutomaticResult,
    string DropResult,
    double HeadAccuracy,
    double TailAccuracy,
    HoldEndMode EndMode,
    HoldDropMode DropMode,
    Func<HoldEvaluation, NoteEmission?> ResolveResult)
{
    public bool RecoverMissedHead { get; init; }

    public bool ContinueAfterMissedHead { get; init; }

    public long Grace { get; init; }

    public bool RequiresRetrigger { get; init; }

    public bool ObserveActiveInput { get; init; }

    public bool ReleaseByOwner { get; init; }

    public long? LatestRecoveryOffset { get; init; }

    public string? BreakResult { get; init; }

    public bool TailOnOppositeControl { get; init; }

    public long AutomaticEndOffset { get; init; }

    public bool AutomaticTailUsesHeadOffset { get; init; }

    public TimerOrder PassiveOrder { get; init; } = TimerOrder.BeforeInput;

    public ImmutableHashSet<string> NonResolvingHeadResults { get; init; } = ImmutableHashSet<string>.Empty;

    public ImmutableHashSet<string> TerminalHeadResults { get; init; } = ImmutableHashSet<string>.Empty;

    public HoldBalance? Balance { get; init; }

    public Func<string, int>? CandidatePriority { get; init; }

    public TimingWindows? ResolvedHeadWindows { get; init; }

    public long ReleaseGrace { get; init; }

    public ImmutableHashSet<string> DeferredReleaseResults { get; init; } = ImmutableHashSet<string>.Empty;

    public bool SuppressRepeatedMistakes { get; init; }

    public ImmutableHashSet<string> RecoveringHeadResults { get; init; } = ImmutableHashSet<string>.Empty;

}

public sealed record HoldState(
    NoteTimer? Timer,
    bool Done,
    string? HeadResult,
    long? HeadOffset,
    bool Holding,
    bool Broken,
    int Control,
    long? DropDeadline,
    long Balance,
    long BalanceTime,
    bool TailResolved = false,
    bool TailSustains = false,
    long? HeadTime = null,
    long? TailTime = null,
    long? TailOffset = null,
    string? TailResult = null,
    long? PendingRelease = null,
    bool Mistake = false,
    HoldResolution? Resolution = null,
    bool Ended = false) : NoteState
{
    public override NoteTimer? NextTimer => Timer;

    public override bool Completed => Done;
}
