using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.Objects;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

public interface IBmsLnScoring
{
    /// <summary>Registers a separate CN/HCN endpoint through the score and health processors.</summary>
    void ApplySyntheticLongNoteEndpoint(DrawableBmsHitObject drawable, BmsLongNoteEndpointResult endpoint);

    /// <summary>Applies a HellChargeNote body gauge tick for the currently pressed column.</summary>
    void ApplyHellChargeTick(bool holding, double scale);
}
