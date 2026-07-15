using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Mods.LongNoteMode;

/// <summary>
///     Hell-charge-note (HCN) mode — beatoraja-style hell charge with body gauge drain.
///     Two-endpoint scoring identical to charge-note (L2), plus continuous body-tick
///     health changes at 200 ms intervals: holding the body recovers health at half-GREAT
///     rate, while releasing damages health at half-BAD rate.
///     Body ticks do not add judgement count or affect combo.
/// </summary>
public class BmsModHellChargeNote : BmsModLongNoteModeBase
{
    public override string Name => "LN3: Hell Charge Note (HCN)";

    public override string Acronym => "L3";

    public override LocalisableString Description => BmsStrings.ModHellChargeNote;

    protected override BmsLongNoteMode TargetMode => BmsLongNoteMode.HellChargeNote;
}
