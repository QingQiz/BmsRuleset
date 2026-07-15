using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Mods.LongNoteMode;

/// <summary>
///     Charge-note (CN) mode — beatoraja-style charge note with two scoring endpoints.
///     Head press emits a judgement immediately; tail release is judged separately
///     via the long-note tail window table.  Missing the head produces two POORs
///     (head and tail), and each endpoint updates score, combo, and health independently.
/// </summary>
public class BmsModChargeNote : BmsModLongNoteModeBase
{
    public override string Name => "LN2: Charge Note (CN)";

    public override string Acronym => "L2";

    public override LocalisableString Description => BmsStrings.ModChargeNote;

    protected override BmsLongNoteMode TargetMode => BmsLongNoteMode.ChargeNote;
}
