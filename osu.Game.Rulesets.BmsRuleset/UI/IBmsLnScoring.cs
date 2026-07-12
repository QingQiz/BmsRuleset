using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI;

public interface IBmsLnScoring
{
    /// <summary>Anchors the drawable's next framework result to a specific LN endpoint.</summary>
    void PrepareLongNoteEndpoint(DrawableBmsHitObject drawable, double endpointTime, double eventTime);

    /// <summary>Records a real LN endpoint which does not otherwise produce a framework result.</summary>
    void RegisterLongNoteEndpoint(DrawableBmsHitObject drawable, double endpointTime, double eventTime, double gameplayRate, HitResult result);

    /// <summary>Removes a statistics-only endpoint after rewinding before its judgement.</summary>
    void RemoveLongNoteEndpoint(DrawableBmsHitObject drawable);

    /// <summary>Registers an HCN head judgement that should not end the drawable yet.</summary>
    void ApplyLongNoteHead(DrawableBmsHitObject drawable, double eventTime, HitResult result, double gameplayRate);

    /// <summary>Registers a synthetic long-note endpoint (CN/HCN tail) through the score + health processors.</summary>
    void ApplySyntheticLongNoteEndpoint(DrawableBmsHitObject drawable, double endpointTime, double eventTime, HitResult result, double gameplayRate);

    /// <summary>Applies a HellChargeNote body gauge tick for the currently pressed column.</summary>
    void ApplyHellChargeTick(bool holding, double scale);
}
