using System.ComponentModel;

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
    Scratch,

    [Description("Key 1")]
    Key1,

    [Description("Key 2")]
    Key2,

    [Description("Key 3")]
    Key3,

    [Description("Key 4")]
    Key4,

    [Description("Key 5")]
    Key5,

    [Description("Key 6")]
    Key6,

    [Description("Key 7")]
    Key7,

    [Description("P2 Scratch")]
    P2Scratch,

    [Description("P2 Key 1")]
    P2Key1,

    [Description("P2 Key 2")]
    P2Key2,

    [Description("P2 Key 3")]
    P2Key3,

    [Description("P2 Key 4")]
    P2Key4,

    [Description("P2 Key 5")]
    P2Key5,

    [Description("P2 Key 6")]
    P2Key6,

    [Description("P2 Key 7")]
    P2Key7,

    [Description("PMS Key 1")]
    PmsKey1,

    [Description("PMS Key 2")]
    PmsKey2,

    [Description("PMS Key 3")]
    PmsKey3,

    [Description("PMS Key 4")]
    PmsKey4,

    [Description("PMS Key 5")]
    PmsKey5,

    [Description("PMS Key 6")]
    PmsKey6,

    [Description("PMS Key 7")]
    PmsKey7,

    [Description("PMS Key 8")]
    PmsKey8,

    [Description("PMS Key 9")]
    PmsKey9,

    [Description("P2 PMS Key 1")]
    P2PmsKey1,

    [Description("P2 PMS Key 2")]
    P2PmsKey2,

    [Description("P2 PMS Key 3")]
    P2PmsKey3,

    [Description("P2 PMS Key 4")]
    P2PmsKey4,

    [Description("P2 PMS Key 5")]
    P2PmsKey5,

    [Description("P2 PMS Key 6")]
    P2PmsKey6,

    [Description("P2 PMS Key 7")]
    P2PmsKey7,

    [Description("P2 PMS Key 8")]
    P2PmsKey8,

    [Description("P2 PMS Key 9")]
    P2PmsKey9,

    [Description("Increase Scroll Speed")]
    IncreaseScrollSpeed,

    [Description("Decrease Scroll Speed")]
    DecreaseScrollSpeed,
}
