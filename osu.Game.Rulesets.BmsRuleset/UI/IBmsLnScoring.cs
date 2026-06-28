using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI;

public interface IBmsLnScoring
{
    /// <summary>Registers an HCN head judgement that should not end the drawable yet.</summary>
    void ApplyLongNoteHead(DrawableBmsHitObject drawable, double eventTime, HitResult result);

    /// <summary>Registers a synthetic long-note endpoint (CN/HCN tail) through the score + health processors.</summary>
    void ApplySyntheticLongNoteEndpoint(DrawableBmsHitObject drawable, double endpointTime, double eventTime, HitResult result);

    /// <summary>Applies a HellChargeNote body gauge tick for the currently pressed column.</summary>
    void ApplyHellChargeTick(bool holding, double scale);
}
