namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

public enum BmsLayoutVariant
{
    Bms5K = 5,
    Bme7K = 7,
    Popn9K = 9,
    Bms5KDouble = 105,
    Bme7KDouble = 107,
    Popn9KDouble = 109,
    Bms5K2P = 1005,
    Bme7K2P = 1007,
    Popn9K2P = 1009,
    Popn5K = 2005,
    Keyboard24K = 2026,
    Keyboard24KDouble = 2052,

    // Generic layouts encode their key count after this boundary.
    Generic = 10000,
}
