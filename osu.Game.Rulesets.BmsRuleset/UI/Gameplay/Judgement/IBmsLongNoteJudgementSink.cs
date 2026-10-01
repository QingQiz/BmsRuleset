using System;
using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

/// <summary>
/// Synchronous engine outputs, resolved by chart identity rather than a retained drawable.
/// Controllers borrow this sink only for the duration of a judgement operation.
/// </summary>
internal interface IBmsLongNoteJudgementSink
{
    void OnUserHeadJudged(BmsLongNote note);
    void OnHellChargeHeadPoor(BmsLongNote note, double eventTime, double lifetimeEnd);
    void ApplyJudgementResult(BmsLongNote note, HitResult result, IReadOnlyList<BmsLongNoteEndpointResult> endpoints);
    void ApplySyntheticEndpoint(BmsLongNoteEndpointResult endpoint);
    void ClearVisualIfTailWasNotPoor(BmsLongNote note, HitResult result);
    void Retire(BmsLongNote note);

    // Reusing the column callback avoids allocating a wrapper for each HCN body update.
    Action<bool, double> HellChargeTick { get; }
}
