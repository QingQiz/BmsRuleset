using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Mods.LongNoteMode;

/// <summary>
///     Classic long-note (LN) mode — the standard BMS hold-note behaviour.
///     The head press records timing offset without emitting a judgement;
///     a single judgement is emitted at the tail using the worse of the
///     head offset and the release-to-tail offset.
///     Missed head produces one POOR.
/// </summary>
public class BmsModLongNote : BmsModLongNoteModeBase
{
    public override string Name => "LN1: Long Note (LN)";

    public override string Acronym => "L1";

    public override LocalisableString Description => "Standard hold-note: single endpoint judged at the tail using worst offset.";

    protected override BmsLongNoteMode TargetMode => BmsLongNoteMode.LongNote;
}
