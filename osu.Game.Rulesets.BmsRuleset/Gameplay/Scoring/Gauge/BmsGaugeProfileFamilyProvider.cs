using System;
using RhythmEngine.Profiles.Bms;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;

public static class BmsGaugeProfileFamilyProvider
{
    public static BmsGaugeProfileFamily FromLayout(BmsLayoutVariant layout) => layout switch
    {
        BmsLayoutVariant.Bms5K or BmsLayoutVariant.Bms5K2P or BmsLayoutVariant.Bms5KDouble => BmsGaugeProfileFamily.FiveKeys,
        BmsLayoutVariant.Bme7K or BmsLayoutVariant.Bme7K2P or BmsLayoutVariant.Bme7KDouble => BmsGaugeProfileFamily.SevenKeys,
        BmsLayoutVariant.Popn5K or BmsLayoutVariant.Popn9K or BmsLayoutVariant.Popn9K2P or BmsLayoutVariant.Popn9KDouble => BmsGaugeProfileFamily.Pms,
        BmsLayoutVariant.Keyboard24K or BmsLayoutVariant.Keyboard24KDouble => BmsGaugeProfileFamily.Keyboard,
        _ when BmsLayout.IsGeneric(layout) => BmsGaugeProfileFamily.Keyboard,
        _ => throw new ArgumentOutOfRangeException(nameof(layout), layout, null),
    };
}
