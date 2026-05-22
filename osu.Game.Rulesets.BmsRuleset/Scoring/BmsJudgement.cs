using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring;

/// <summary>
///     Native BMS judgement definition.
/// </summary>
/// <remarks>
///     BMS commonly distinguishes PGREAT/GREAT/GOOD/BAD/POOR. osu! exposes a fixed set of
///     <see cref="HitResult" /> values, so the first native implementation maps PGREAT to
///     <see cref="HitResult.Perfect" /> and POOR to <see cref="HitResult.Miss" />.
/// </remarks>
public class BmsJudgement : Judgement
{
    public override HitResult MaxResult => HitResult.Perfect;
}
