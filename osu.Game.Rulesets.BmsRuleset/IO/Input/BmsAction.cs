using System.ComponentModel;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Localisation;

namespace osu.Game.Rulesets.BmsRuleset.IO.Input;

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

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionPmsKey1))]
    PmsKey1,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionPmsKey2))]
    PmsKey2,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionPmsKey3))]
    PmsKey3,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionPmsKey4))]
    PmsKey4,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionPmsKey5))]
    PmsKey5,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionPmsKey6))]
    PmsKey6,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionPmsKey7))]
    PmsKey7,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionPmsKey8))]
    PmsKey8,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionPmsKey9))]
    PmsKey9,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2PmsKey1))]
    P2PmsKey1,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2PmsKey2))]
    P2PmsKey2,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2PmsKey3))]
    P2PmsKey3,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2PmsKey4))]
    P2PmsKey4,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2PmsKey5))]
    P2PmsKey5,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2PmsKey6))]
    P2PmsKey6,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2PmsKey7))]
    P2PmsKey7,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2PmsKey8))]
    P2PmsKey8,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2PmsKey9))]
    P2PmsKey9,

    [Description("Increase Scroll Speed")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionIncreaseScrollSpeed))]
    IncreaseScrollSpeed,

    [Description("Decrease Scroll Speed")]
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionDecreaseScrollSpeed))]
    DecreaseScrollSpeed,

    // Append actions so existing key bindings and replay action numbers remain stable.
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionScratchReverse))]
    ScratchReverse,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionP2ScratchReverse))]
    P2ScratchReverse,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey1))]
    KeyboardKey1,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey2))]
    KeyboardKey2,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey3))]
    KeyboardKey3,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey4))]
    KeyboardKey4,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey5))]
    KeyboardKey5,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey6))]
    KeyboardKey6,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey7))]
    KeyboardKey7,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey8))]
    KeyboardKey8,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey9))]
    KeyboardKey9,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey10))]
    KeyboardKey10,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey11))]
    KeyboardKey11,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey12))]
    KeyboardKey12,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey13))]
    KeyboardKey13,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey14))]
    KeyboardKey14,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey15))]
    KeyboardKey15,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey16))]
    KeyboardKey16,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey17))]
    KeyboardKey17,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey18))]
    KeyboardKey18,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey19))]
    KeyboardKey19,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey20))]
    KeyboardKey20,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey21))]
    KeyboardKey21,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey22))]
    KeyboardKey22,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey23))]
    KeyboardKey23,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey24))]
    KeyboardKey24,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey25))]
    KeyboardKey25,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey26))]
    KeyboardKey26,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey27))]
    KeyboardKey27,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey28))]
    KeyboardKey28,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey29))]
    KeyboardKey29,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey30))]
    KeyboardKey30,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey31))]
    KeyboardKey31,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey32))]
    KeyboardKey32,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey33))]
    KeyboardKey33,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey34))]
    KeyboardKey34,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey35))]
    KeyboardKey35,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey36))]
    KeyboardKey36,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey37))]
    KeyboardKey37,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey38))]
    KeyboardKey38,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey39))]
    KeyboardKey39,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey40))]
    KeyboardKey40,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey41))]
    KeyboardKey41,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey42))]
    KeyboardKey42,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey43))]
    KeyboardKey43,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey44))]
    KeyboardKey44,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey45))]
    KeyboardKey45,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey46))]
    KeyboardKey46,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey47))]
    KeyboardKey47,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey48))]
    KeyboardKey48,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey49))]
    KeyboardKey49,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey50))]
    KeyboardKey50,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey51))]
    KeyboardKey51,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey52))]
    KeyboardKey52,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey53))]
    KeyboardKey53,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey54))]
    KeyboardKey54,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey55))]
    KeyboardKey55,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey56))]
    KeyboardKey56,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey57))]
    KeyboardKey57,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey58))]
    KeyboardKey58,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey59))]
    KeyboardKey59,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey60))]
    KeyboardKey60,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey61))]
    KeyboardKey61,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey62))]
    KeyboardKey62,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey63))]
    KeyboardKey63,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.ActionKeyboardKey64))]
    KeyboardKey64,

}
