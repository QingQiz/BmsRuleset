using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Objects.Drawables.LnHelper;

internal interface IBmsLongNoteHooks
{
    /// <summary>Pin the visual head to the judgement line and seed the hold-explosion timer (user head judgement).</summary>
    void OnUserHeadJudged();

    /// <summary>HCN head-POOR: pin head, keep drawable alive until <paramref name="lifetimeEnd"/>, and register the head scoring event.</summary>
    void OnHellChargeHeadPoor(double eventTime, double lifetimeEnd);

    /// <summary>Apply the drawable's framework result with every timing endpoint represented by it.</summary>
    void ApplyJudgementResult(HitResult result, IReadOnlyList<BmsLongNoteEndpointResult> endpoints);

    /// <summary>Apply a separate CN/HCN scoring result without completing the drawable.</summary>
    void ApplySyntheticEndpoint(HitResult result, BmsLongNoteEndpointResult endpoint);

    /// <summary>Clear the body/tail visuals when the tail result is not POOR (was <c>clearVisualIfTailWasNotPoor</c>).</summary>
    void ClearVisualIfTailWasNotPoor(HitResult result);

    /// <summary>Apply one HCN body gauge tick.</summary>
    void ApplyHellChargeTick(bool holding, double scale);

    /// <summary>Retire the drawable: fade out and end its lifetime now.</summary>
    void Retire();
}
