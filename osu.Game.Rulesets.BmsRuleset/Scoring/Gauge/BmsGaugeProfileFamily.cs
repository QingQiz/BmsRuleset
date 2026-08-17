using System;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;

public enum BmsGaugeProfileFamily
{
    FiveKeys,
    SevenKeys,
    Pms,
    Keyboard,
    Lr2,
}

public static class BmsGaugeProfileFamilyProvider
{
    public static BmsGaugeProfileFamily FromLayout(BmsLayoutVariant layout) => layout switch
    {
        BmsLayoutVariant.Bms5K or BmsLayoutVariant.Bms5K2P or BmsLayoutVariant.Bms5KDouble => BmsGaugeProfileFamily.FiveKeys,
        BmsLayoutVariant.Bme7K or BmsLayoutVariant.Bme7K2P or BmsLayoutVariant.Bme7KDouble => BmsGaugeProfileFamily.SevenKeys,
        BmsLayoutVariant.Pms9K or BmsLayoutVariant.Pms9K2P or BmsLayoutVariant.Pms9KDouble => BmsGaugeProfileFamily.Pms,
        _ => throw new ArgumentOutOfRangeException(nameof(layout), layout, null),
    };
}
