using System.Collections.Immutable;
using RhythmEngine.Core;

namespace RhythmEngine.Profiles.Patterns;

/// <summary>Selects normal tail settlement; interruptions are handled separately by <see cref="HoldDropMode"/>.</summary>
public enum HoldEndMode
{
    /// <summary>Judges the tail on release or a configured opposite-control input; an unresolved tail receives MissResult after its deadline.</summary>
    Release,

    /// <summary>Settles at the tail time plus AutomaticEndOffset; ordinary releases are handled by the drop policy.</summary>
    Automatic,

    /// <summary>Accepts a release within the tail window, otherwise settling automatically at the tail time plus AutomaticEndOffset.</summary>
    ReleaseOrAutomatic,
}

/// <summary>Handles releases that neither qualify for tail judgement nor enter the optional ReleaseGrace deferral.</summary>
public enum HoldDropMode
{
    /// <summary>Immediately resolves the tail with DropResult; Balance determines whether sustained body processing continues.</summary>
    Resolve,

    /// <summary>Marks the hold Broken and permits a regrab. May emit BreakResult once; later tail evaluation can cap the result using Broken.</summary>
    Recover,

    /// <summary>Allows a regrab within Grace, resolving a drop when the deadline expires. Rolls also use this deadline to require repeated presses.</summary>
    Grace,
}

/// <summary>Explains why the tail was resolved, allowing scoring and state projections to distinguish completion, timeout and interruption.</summary>
/// <remarks>This describes the tail decision only. Sustained body processing or resolved-head input windows can remain active afterwards.</remarks>
public enum HoldResolution
{
    /// <summary>A release, opposite-control input or host end settled through the tail window; ReleaseGrace may delay confirmation.</summary>
    Release,

    /// <summary>The automatic settlement time was reached; held state or an unexpired grace period is evaluated without requiring a new release.</summary>
    Automatic,

    /// <summary>The tail remained unresolved beyond its latest judgement time and is settled with MissResult.</summary>
    Timeout,

    /// <summary>An interruption or expired grace period settled the tail with DropResult, rather than waiting for the tail window to expire.</summary>
    Drop,

    /// <summary>The head expired and the configuration permits neither continuation nor recovery, so the tail is settled with it.</summary>
    MissedHead,

    /// <summary>A head result in TerminalHeadResults immediately resolved the tail, such as a terminal head BAD.</summary>
    Head,
}

public readonly record struct HoldEvaluation(
    ChartNote Note,
    ResultId HeadResult,
    long? HeadOffset,
    ResultId TailResult,
    long? TailOffset,
    bool Broken,
    HoldResolution Resolution);

/// <summary>Integrates held/released time with a signed accumulator, emitting on threshold crossings.</summary>
public sealed record HoldBalance(long Threshold, ResultId GainResult, ResultId LossResult, ImmutableHashSet<ResultId> SustainingTailResults);

public sealed record HoldConfiguration(
    TimingWindows HeadWindows,
    TimingWindows TailWindows,
    long HeadDeadline,
    long TailDeadline,
    ResultId MissResult,
    ResultId AutomaticResult,
    ResultId DropResult,
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

    public ResultId? BreakResult { get; init; }

    public bool TailOnOppositeControl { get; init; }

    public long AutomaticEndOffset { get; init; }

    public bool AutomaticTailUsesHeadOffset { get; init; }

    public TimerOrder PassiveOrder { get; init; } = TimerOrder.BeforeInput;

    public ImmutableHashSet<ResultId> NonResolvingHeadResults { get; init; } = ImmutableHashSet<ResultId>.Empty;

    public ImmutableHashSet<ResultId> TerminalHeadResults { get; init; } = ImmutableHashSet<ResultId>.Empty;

    public HoldBalance? Balance { get; init; }

    public Func<ResultId, int>? CandidatePriority { get; init; }

    public TimingWindows? ResolvedHeadWindows { get; init; }

    public long ReleaseGrace { get; init; }

    public ImmutableHashSet<ResultId> DeferredReleaseResults { get; init; } = ImmutableHashSet<ResultId>.Empty;

    public bool SuppressRepeatedMistakes { get; init; }

    public bool SuppressMissedHeadPenalty { get; init; }

    public ImmutableHashSet<ResultId> RecoveringHeadResults { get; init; } = ImmutableHashSet<ResultId>.Empty;
}

public sealed record HoldState(
    NoteTimer? Timer,
    bool Done,
    ResultId? HeadResult,
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
    ResultId? TailResult = null,
    long? PendingRelease = null,
    bool Mistake = false,
    HoldResolution? Resolution = null,
    bool Ended = false,
    bool BodyBreakLatched = false) : NoteState
{
    public override NoteTimer? NextTimer => Timer;

    public override bool Completed => Done;
}
