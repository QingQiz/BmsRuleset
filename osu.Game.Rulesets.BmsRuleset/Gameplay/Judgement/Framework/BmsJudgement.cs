using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;

/// <summary>The framework grade range; score opportunities and result semantics belong to the selected profile.</summary>
public class BmsJudgement : Rulesets.Judgements.Judgement
{
    public override HitResult MaxResult => HitResult.Perfect;

    public override HitResult MinResult => HitResult.Miss;
}
