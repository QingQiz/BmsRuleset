using System.Collections.Immutable;

namespace RhythmEngine.Core;

public sealed record JudgementEvent(
    long Sequence,
    int NoteId,
    int Lane,
    PartId Part,
    ResultId Result,
    long Time,
    long ExpectedTime,
    long? Offset)
{
    public ResultId OriginalResult { get; init; } = Result;

    public bool SuppressPenalty { get; init; }
}

public sealed record ScoreState
{
    public double Score { get; init; }

    public double AccuracyPoints { get; init; }

    public double AccuracyMaximum { get; init; }

    public int Combo { get; init; }

    public int MaximumCombo { get; init; }

    public int JudgedOpportunities { get; init; }

    public double Bonus { get; init; }

    public ImmutableDictionary<string, double> Counters { get; init; } = ImmutableDictionary<string, double>.Empty;

    public ImmutableDictionary<ResultId, int> ResultCounts { get; init; } = ImmutableDictionary<ResultId, int>.Empty;

    public double Accuracy => AccuracyMaximum == 0 ? 1 : AccuracyPoints / AccuracyMaximum;
}

public readonly record struct ScoreContext(JudgementEvent Event, ScoringOpportunity? Opportunity, ScorePlan Plan);

public sealed record ScoreConfiguration(
    ScoreState InitialState,
    Func<ScoreState, ScoreContext, ScoreState> Reduce,
    Func<ScorePlan, double> MaximumScore,
    Func<ScoreState, JudgementEvent, JudgementEvent>? TransformResult = null);

public readonly record struct HealthState(double Value, bool HasFailed)
{
    public ImmutableArray<HealthState> Layers { get; init; } = [];
}

public readonly record struct HealthContext(JudgementEvent Event, HealthState Health, ScoreState Score, ScorePlan Plan);

/// <summary>The current play outcome, evaluated by the health configuration from completion, score and health.</summary>
/// <remarks>An outcome does not stop the session. The configuration decides whether failure is permanent; the host decides when to leave play.</remarks>
public enum SessionOutcome
{
    /// <summary>The configured clear or failure conditions have not been met; play is still in progress.</summary>
    InProgress,

    /// <summary>The configured clear conditions have been met; this does not require every note to have been hit.</summary>
    Cleared,

    /// <summary>A failure condition was triggered, or completion did not meet the clear requirement. Recovery depends on the configuration.</summary>
    Failed,
}

public readonly record struct SessionOutcomeContext(bool Completed, HealthState Health, ScoreState Score, ScorePlan Plan);

/// <summary>
/// Owns the whole health transition and outcome, including bounds, recovery and failure policy.
/// Core does not latch failure, clamp health or assume that health alone determines a clear.
/// </summary>
public sealed record HealthConfiguration(
    HealthState InitialState,
    Func<HealthContext, HealthState> Reduce,
    Func<SessionOutcomeContext, SessionOutcome> EvaluateOutcome);

public sealed record SessionConfiguration(
    string ProfileId,
    ScoreConfiguration Score,
    HealthConfiguration Health,
    Func<InputArbitrationContext, ImmutableArray<InputCandidate>> SelectCandidates,
    Func<SessionCompletionContext, bool>? IsComplete = null);

public readonly record struct SessionCompletionContext(long? Time, bool NotesRetired, ScoreState Score, ScorePlan Plan);
