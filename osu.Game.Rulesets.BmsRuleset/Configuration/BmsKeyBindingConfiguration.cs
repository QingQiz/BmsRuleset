using System.Collections.Generic;
using osu.Framework.Input.Bindings;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.Configuration;

public static class BmsKeyBindingConfiguration
{
    public static IEnumerable<int> AvailableVariants =>
    [
        (int)BmsLayoutVariant.Bms5K,
        (int)BmsLayoutVariant.Bme7K,
        (int)BmsLayoutVariant.Pms9K,
        (int)BmsLayoutVariant.Bms5K2P,
        (int)BmsLayoutVariant.Bme7K2P,
        (int)BmsLayoutVariant.Bms5KDouble,
        (int)BmsLayoutVariant.Bme7KDouble,
        (int)BmsLayoutVariant.Pms9KDouble,
    ];

    public static KeyBinding[] GetDefaultKeyBindings(int variant) => (BmsLayoutVariant)variant switch
    {
        BmsLayoutVariant.Bms5K => bindings5K(),
        BmsLayoutVariant.Bms5K2P => bindings5K2P(),
        BmsLayoutVariant.Bme7K => bindings7K(),
        BmsLayoutVariant.Bme7K2P => bindings7K2P(),
        BmsLayoutVariant.Pms9K => bindings9K(),
        BmsLayoutVariant.Bms5KDouble => bindings5KDouble(),
        BmsLayoutVariant.Bme7KDouble => bindings7KDouble(),
        BmsLayoutVariant.Pms9KDouble => bindings9KDouble(),
        _ => bindings7K(),
    };

    public static int? ActionToColumn(BmsAction action, BmsLayoutVariant variant = BmsLayoutVariant.Bme7K) => variant switch
    {
        BmsLayoutVariant.Bms5K or BmsLayoutVariant.Bms5K2P => map5K(action),
        BmsLayoutVariant.Bme7K or BmsLayoutVariant.Bme7K2P => map7K(action),
        BmsLayoutVariant.Pms9K => map9K(action),
        BmsLayoutVariant.Bms5KDouble => map5KDouble(action),
        BmsLayoutVariant.Bme7KDouble => map7KDouble(action),
        BmsLayoutVariant.Pms9KDouble => map9KDouble(action),
        _ => null,
    };

    public static BmsAction? ActionForColumn(BmsLayoutVariant layout, int column) => layout switch
    {
        BmsLayoutVariant.Bms5K or BmsLayoutVariant.Bms5K2P => actionFor5K(column),
        BmsLayoutVariant.Bme7K or BmsLayoutVariant.Bme7K2P => actionFor7K(column),
        BmsLayoutVariant.Pms9K => actionFor9K(column),
        BmsLayoutVariant.Bms5KDouble => actionFor5KDouble(column),
        BmsLayoutVariant.Bme7KDouble => actionFor7KDouble(column),
        BmsLayoutVariant.Pms9KDouble => actionFor9KDouble(column),
        _ => null,
    };

    private static KeyBinding[] bindings5K() =>
    [
        ..scrollSpeedBindings(),
        new(InputKey.LShift, BmsAction.Scratch),
        new(InputKey.Z, BmsAction.Key1),
        new(InputKey.S, BmsAction.Key2),
        new(InputKey.X, BmsAction.Key3),
        new(InputKey.D, BmsAction.Key4),
        new(InputKey.C, BmsAction.Key5),
    ];

    private static KeyBinding[] bindings7K() =>
    [
        ..scrollSpeedBindings(),
        new(InputKey.LShift, BmsAction.Scratch),
        new(InputKey.Z, BmsAction.Key1),
        new(InputKey.S, BmsAction.Key2),
        new(InputKey.X, BmsAction.Key3),
        new(InputKey.D, BmsAction.Key4),
        new(InputKey.C, BmsAction.Key5),
        new(InputKey.F, BmsAction.Key6),
        new(InputKey.V, BmsAction.Key7),
    ];

    private static KeyBinding[] bindings5KDouble() =>
    [
        ..bindings5K(),
        new(InputKey.RShift, BmsAction.P2Scratch),
        new(InputKey.Keypad1, BmsAction.P2Key1),
        new(InputKey.Keypad2, BmsAction.P2Key2),
        new(InputKey.Keypad3, BmsAction.P2Key3),
        new(InputKey.Keypad4, BmsAction.P2Key4),
        new(InputKey.Keypad5, BmsAction.P2Key5),
    ];

    private static KeyBinding[] bindings7KDouble() =>
    [
        ..bindings7K(),
        new(InputKey.RShift, BmsAction.P2Scratch),
        new(InputKey.Keypad1, BmsAction.P2Key1),
        new(InputKey.Keypad2, BmsAction.P2Key2),
        new(InputKey.Keypad3, BmsAction.P2Key3),
        new(InputKey.Keypad4, BmsAction.P2Key4),
        new(InputKey.Keypad5, BmsAction.P2Key5),
        new(InputKey.Keypad6, BmsAction.P2Key6),
        new(InputKey.Keypad7, BmsAction.P2Key7),
    ];

    private static KeyBinding[] bindings9K() =>
    [
        ..scrollSpeedBindings(),
        new(InputKey.A, BmsAction.PmsKey1),
        new(InputKey.S, BmsAction.PmsKey2),
        new(InputKey.D, BmsAction.PmsKey3),
        new(InputKey.F, BmsAction.PmsKey4),
        new(InputKey.Space, BmsAction.PmsKey5),
        new(InputKey.J, BmsAction.PmsKey6),
        new(InputKey.K, BmsAction.PmsKey7),
        new(InputKey.L, BmsAction.PmsKey8),
        new(InputKey.Semicolon, BmsAction.PmsKey9),
    ];

    private static KeyBinding[] scrollSpeedBindings() =>
    [
        new(InputKey.Up, BmsAction.IncreaseScrollSpeed),
        new(InputKey.Down, BmsAction.DecreaseScrollSpeed),
    ];

    private static KeyBinding[] bindings9KDouble() =>
    [
        ..bindings9K(),
        new(InputKey.Keypad1, BmsAction.P2PmsKey1),
        new(InputKey.Keypad2, BmsAction.P2PmsKey2),
        new(InputKey.Keypad3, BmsAction.P2PmsKey3),
        new(InputKey.Keypad4, BmsAction.P2PmsKey4),
        new(InputKey.Keypad5, BmsAction.P2PmsKey5),
        new(InputKey.Keypad6, BmsAction.P2PmsKey6),
        new(InputKey.Keypad7, BmsAction.P2PmsKey7),
        new(InputKey.Keypad8, BmsAction.P2PmsKey8),
        new(InputKey.Keypad9, BmsAction.P2PmsKey9),
    ];

    private static int? map5K(BmsAction action) => action switch
    {
        BmsAction.Scratch => 0,
        BmsAction.Key1 => 1,
        BmsAction.Key2 => 2,
        BmsAction.Key3 => 3,
        BmsAction.Key4 => 4,
        BmsAction.Key5 => 5,
        _ => null,
    };

    private static int? map7K(BmsAction action) => action switch
    {
        BmsAction.Scratch => 0,
        BmsAction.Key1 => 1,
        BmsAction.Key2 => 2,
        BmsAction.Key3 => 3,
        BmsAction.Key4 => 4,
        BmsAction.Key5 => 5,
        BmsAction.Key6 => 6,
        BmsAction.Key7 => 7,
        _ => null,
    };

    private static int? map5KDouble(BmsAction action) => map5K(action) ?? action switch
    {
        BmsAction.P2Key1 => 6,
        BmsAction.P2Key2 => 7,
        BmsAction.P2Key3 => 8,
        BmsAction.P2Key4 => 9,
        BmsAction.P2Key5 => 10,
        BmsAction.P2Scratch => 11,
        _ => null,
    };

    private static int? map7KDouble(BmsAction action) => map7K(action) ?? action switch
    {
        BmsAction.P2Key1 => 8,
        BmsAction.P2Key2 => 9,
        BmsAction.P2Key3 => 10,
        BmsAction.P2Key4 => 11,
        BmsAction.P2Key5 => 12,
        BmsAction.P2Key6 => 13,
        BmsAction.P2Key7 => 14,
        BmsAction.P2Scratch => 15,
        _ => null,
    };

    private static int? map9K(BmsAction action) => action switch
    {
        BmsAction.PmsKey1 => 0,
        BmsAction.PmsKey2 => 1,
        BmsAction.PmsKey3 => 2,
        BmsAction.PmsKey4 => 3,
        BmsAction.PmsKey5 => 4,
        BmsAction.PmsKey6 => 5,
        BmsAction.PmsKey7 => 6,
        BmsAction.PmsKey8 => 7,
        BmsAction.PmsKey9 => 8,
        _ => null,
    };

    private static int? map9KDouble(BmsAction action) => map9K(action) ?? action switch
    {
        BmsAction.P2PmsKey1 => 9,
        BmsAction.P2PmsKey2 => 10,
        BmsAction.P2PmsKey3 => 11,
        BmsAction.P2PmsKey4 => 12,
        BmsAction.P2PmsKey5 => 13,
        BmsAction.P2PmsKey6 => 14,
        BmsAction.P2PmsKey7 => 15,
        BmsAction.P2PmsKey8 => 16,
        BmsAction.P2PmsKey9 => 17,
        _ => null,
    };

    private static BmsAction? actionFor5K(int column) => column switch
    {
        0 => BmsAction.Scratch,
        1 => BmsAction.Key1,
        2 => BmsAction.Key2,
        3 => BmsAction.Key3,
        4 => BmsAction.Key4,
        5 => BmsAction.Key5,
        _ => null,
    };

    private static BmsAction? actionFor7K(int column) => column switch
    {
        <= 5 => actionFor5K(column),
        6 => BmsAction.Key6,
        7 => BmsAction.Key7,
        _ => null,
    };

    private static BmsAction? actionFor5KDouble(int column) => column switch
    {
        <= 5 => actionFor5K(column),
        6 => BmsAction.P2Key1,
        7 => BmsAction.P2Key2,
        8 => BmsAction.P2Key3,
        9 => BmsAction.P2Key4,
        10 => BmsAction.P2Key5,
        11 => BmsAction.P2Scratch,
        _ => null,
    };

    private static BmsAction? actionFor7KDouble(int column) => column switch
    {
        <= 7 => actionFor7K(column),
        8 => BmsAction.P2Key1,
        9 => BmsAction.P2Key2,
        10 => BmsAction.P2Key3,
        11 => BmsAction.P2Key4,
        12 => BmsAction.P2Key5,
        13 => BmsAction.P2Key6,
        14 => BmsAction.P2Key7,
        15 => BmsAction.P2Scratch,
        _ => null,
    };

    private static BmsAction? actionFor9K(int column) => column switch
    {
        0 => BmsAction.PmsKey1,
        1 => BmsAction.PmsKey2,
        2 => BmsAction.PmsKey3,
        3 => BmsAction.PmsKey4,
        4 => BmsAction.PmsKey5,
        5 => BmsAction.PmsKey6,
        6 => BmsAction.PmsKey7,
        7 => BmsAction.PmsKey8,
        8 => BmsAction.PmsKey9,
        _ => null,
    };

    private static BmsAction? actionFor9KDouble(int column) => column switch
    {
        <= 8 => actionFor9K(column),
        9 => BmsAction.P2PmsKey1,
        10 => BmsAction.P2PmsKey2,
        11 => BmsAction.P2PmsKey3,
        12 => BmsAction.P2PmsKey4,
        13 => BmsAction.P2PmsKey5,
        14 => BmsAction.P2PmsKey6,
        15 => BmsAction.P2PmsKey7,
        16 => BmsAction.P2PmsKey8,
        17 => BmsAction.P2PmsKey9,
        _ => null,
    };

    private static KeyBinding[] bindings5K2P() =>
    [
        ..scrollSpeedBindings(),
        new(InputKey.Z, BmsAction.Key1),
        new(InputKey.S, BmsAction.Key2),
        new(InputKey.X, BmsAction.Key3),
        new(InputKey.D, BmsAction.Key4),
        new(InputKey.C, BmsAction.Key5),
        new(InputKey.RShift, BmsAction.Scratch),
    ];

    private static KeyBinding[] bindings7K2P() =>
    [
        ..scrollSpeedBindings(),
        new(InputKey.Z, BmsAction.Key1),
        new(InputKey.S, BmsAction.Key2),
        new(InputKey.X, BmsAction.Key3),
        new(InputKey.D, BmsAction.Key4),
        new(InputKey.C, BmsAction.Key5),
        new(InputKey.F, BmsAction.Key6),
        new(InputKey.V, BmsAction.Key7),
        new(InputKey.RShift, BmsAction.Scratch),
    ];
}
