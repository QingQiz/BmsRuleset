using System.Collections.Immutable;

namespace RhythmEngine;

public sealed record JudgementEvent(
    long Sequence,
    int NoteId,
    int Lane,
    string Part,
    string Result,
    long Time,
    long ExpectedTime,
    long? Offset)
{
    public string OriginalResult { get; init; } = Result;

    public bool SuppressPenalty { get; init; }
}

public sealed record ScoreState
{
    public double Score { get; init; }

    public double AccuracyPoints { get; init; }

    public double AccuracyMaximum { get; init; }

    public int Combo { get; init; }

    public int MaximumCombo { get; init; }

    public double Bonus { get; init; }

    public ImmutableDictionary<string, double> Counters { get; init; } = ImmutableDictionary<string, double>.Empty;

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

public enum SessionOutcome
{
    InProgress,
    Cleared,
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
    Func<InputArbitrationContext, ImmutableArray<InputCandidate>> SelectCandidates);
