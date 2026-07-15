using System.ComponentModel;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Localisation;

namespace osu.Game.Rulesets.BmsRuleset;

/// <summary>
///     Native BMS input actions.
/// </summary>
/// <remarks>
///     Actions cover the supported BMS-family layouts separately so key bindings do not have to reuse
///     scratch-based BMS actions for PMS key layouts.
/// </remarks>
public enum BmsAction
{
    [Description("Scratch")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionScratch))]
    Scratch,

    [Description("Key 1")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKey1))]
    Key1,

    [Description("Key 2")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKey2))]
    Key2,

    [Description("Key 3")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKey3))]
    Key3,

    [Description("Key 4")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKey4))]
    Key4,

    [Description("Key 5")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKey5))]
    Key5,

    [Description("Key 6")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKey6))]
    Key6,

    [Description("Key 7")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKey7))]
    Key7,

    [Description("P2 Scratch")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2Scratch))]
    P2Scratch,

    [Description("P2 Key 1")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2Key1))]
    P2Key1,

    [Description("P2 Key 2")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2Key2))]
    P2Key2,

    [Description("P2 Key 3")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2Key3))]
    P2Key3,

    [Description("P2 Key 4")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2Key4))]
    P2Key4,

    [Description("P2 Key 5")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2Key5))]
    P2Key5,

    [Description("P2 Key 6")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2Key6))]
    P2Key6,

    [Description("P2 Key 7")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2Key7))]
    P2Key7,

    [Description("PMS Key 1")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionPmsKey1))]
    PmsKey1,

    [Description("PMS Key 2")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionPmsKey2))]
    PmsKey2,

    [Description("PMS Key 3")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionPmsKey3))]
    PmsKey3,

    [Description("PMS Key 4")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionPmsKey4))]
    PmsKey4,

    [Description("PMS Key 5")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionPmsKey5))]
    PmsKey5,

    [Description("PMS Key 6")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionPmsKey6))]
    PmsKey6,

    [Description("PMS Key 7")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionPmsKey7))]
    PmsKey7,

    [Description("PMS Key 8")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionPmsKey8))]
    PmsKey8,

    [Description("PMS Key 9")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionPmsKey9))]
    PmsKey9,

    [Description("P2 PMS Key 1")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2PmsKey1))]
    P2PmsKey1,

    [Description("P2 PMS Key 2")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2PmsKey2))]
    P2PmsKey2,

    [Description("P2 PMS Key 3")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2PmsKey3))]
    P2PmsKey3,

    [Description("P2 PMS Key 4")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2PmsKey4))]
    P2PmsKey4,

    [Description("P2 PMS Key 5")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2PmsKey5))]
    P2PmsKey5,

    [Description("P2 PMS Key 6")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2PmsKey6))]
    P2PmsKey6,

    [Description("P2 PMS Key 7")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2PmsKey7))]
    P2PmsKey7,

    [Description("P2 PMS Key 8")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2PmsKey8))]
    P2PmsKey8,

    [Description("P2 PMS Key 9")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2PmsKey9))]
    P2PmsKey9,

    [Description("Increase Scroll Speed")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionIncreaseScrollSpeed))]
    IncreaseScrollSpeed,

    [Description("Decrease Scroll Speed")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionDecreaseScrollSpeed))]
    DecreaseScrollSpeed,
}
