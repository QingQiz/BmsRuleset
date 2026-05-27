using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring;

/// <inheritdoc />
/// <summary>
///     Native BMS judgement definition.
/// </summary>
/// <remarks>
///     BMS six-tier judgement is mapped to osu! <see cref="T:osu.Game.Rulesets.Scoring.HitResult">HitResult</see> values:
///     PGREAT → <see cref="F:osu.Game.Rulesets.Scoring.HitResult.Perfect">HitResult.Perfect</see>,
///     GREAT  → <see cref="F:osu.Game.Rulesets.Scoring.HitResult.Great">HitResult.Great</see>,
///     GOOD   → <see cref="F:osu.Game.Rulesets.Scoring.HitResult.Good">HitResult.Good</see>,
///     BAD    → <see cref="F:osu.Game.Rulesets.Scoring.HitResult.Ok">HitResult.Ok</see>,
///     POOR (normal) → <see cref="F:osu.Game.Rulesets.Scoring.HitResult.Meh">HitResult.Meh</see>,
///     EARLY POOR (excess input) → <see cref="F:osu.Game.Rulesets.Scoring.HitResult.Miss">HitResult.Miss</see>.
/// </remarks>
public class BmsJudgement : Judgement
{
    public override HitResult MaxResult => HitResult.Perfect;
}
