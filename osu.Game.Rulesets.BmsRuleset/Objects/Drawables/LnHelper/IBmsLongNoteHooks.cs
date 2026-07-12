using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Objects.Drawables.LnHelper;

internal interface IBmsLongNoteHooks
{
    /// <summary>Pin the visual head to the judgement line and seed the hold-explosion timer (user head judgement).</summary>
    void OnUserHeadJudged();

    /// <summary>HCN head-POOR: pin head, keep drawable alive until <paramref name="lifetimeEnd"/>, and register the head scoring event.</summary>
    void OnHellChargeHeadPoor(double eventTime, double lifetimeEnd);

    /// <summary>Apply the framework result while keeping its statistics event anchored to the judged endpoint.</summary>
    void ApplyJudgementResult(double endpointTime, double eventTime, HitResult result);

    /// <summary>Record a real endpoint timing event which does not otherwise produce a framework result.</summary>
    void RegisterStatisticsEvent(double endpointTime, double eventTime, HitResult result);

    /// <summary>Remove this LN's statistics-only event after rewinding before its head.</summary>
    void RemoveStatisticsEvent();

    /// <summary>Clear the body/tail visuals when the tail result is not POOR (was <c>clearVisualIfTailWasNotPoor</c>).</summary>
    void ClearVisualIfTailWasNotPoor(HitResult result);

    /// <summary>Register a synthetic CN/HCN tail endpoint through the score/health processors.</summary>
    void ApplySyntheticTailEndpoint(double endpointTime, double eventTime, HitResult result);

    /// <summary>Apply one HCN body gauge tick.</summary>
    void ApplyHellChargeTick(bool holding, double scale);

    /// <summary>Retire the drawable: fade out and end its lifetime now.</summary>
    void Retire();
}
