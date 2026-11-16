using RhythmEngine.Profiles.Common;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;

// Persisted chart metadata keeps the source format's numeric identities.
public enum BmsLongNoteMode
{
    Undefined = 0,
    LongNote = 1,
    ChargeNote = 2,
    HellChargeNote = 3,
}

internal static class BmsLongNoteModeExtensions
{
    internal static KeyNoteKind ToNoteKind(this BmsLongNoteMode mode) => mode switch
    {
        BmsLongNoteMode.ChargeNote => KeyNoteKind.ReleaseHold,
        BmsLongNoteMode.HellChargeNote => KeyNoteKind.SustainHold,
        _ => KeyNoteKind.Hold,
    };
}
