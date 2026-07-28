using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring;

/// <inheritdoc />
/// <summary>
///     Native BMS judgement definition.
/// </summary>
/// <remarks>
///     BMS five-tier note judgement mapped to osu! <see cref="T:osu.Game.Rulesets.Scoring.HitResult">HitResult</see> values:
///     PGREAT → <see cref="F:osu.Game.Rulesets.Scoring.HitResult.Perfect">HitResult.Perfect</see>,
///     GREAT  → <see cref="F:osu.Game.Rulesets.Scoring.HitResult.Great">HitResult.Great</see>,
///     GOOD   → <see cref="F:osu.Game.Rulesets.Scoring.HitResult.Good">HitResult.Good</see>,
///     BAD    → <see cref="F:osu.Game.Rulesets.Scoring.HitResult.Ok">HitResult.Ok</see>,
///     POOR   → <see cref="F:osu.Game.Rulesets.Scoring.HitResult.Meh">HitResult.Meh</see>
///     (both passive miss and in-range fast keypress that consumes the note).
///     Empty POOR (keypress outside all note windows) produces no <see cref="T:osu.Game.Rulesets.Judgements.JudgementResult"/>.
/// </remarks>
public class BmsJudgement(BmsHitObject? source = null) : Judgement
{
    public override HitResult MaxResult => source is BmsLandmine ? HitResult.Meh : HitResult.Perfect;

    public override HitResult MinResult => HitResult.Miss;
}
