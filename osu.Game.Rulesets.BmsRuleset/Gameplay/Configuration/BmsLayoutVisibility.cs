using System.Collections.Generic;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Localisation;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;

internal static class BmsLayoutVisibility
{
    internal static readonly IReadOnlyList<(BmsRulesetSetting Setting, BmsLayoutVariant Layout, LocalisableString Caption)> Options =
    [
        (BmsRulesetSetting.ShowBms5K, BmsLayoutVariant.Bms5K, BmsStrings.ShowBms5K),
        (BmsRulesetSetting.ShowBme7K, BmsLayoutVariant.Bme7K, BmsStrings.ShowBme7K),
        (BmsRulesetSetting.ShowPopn5K, BmsLayoutVariant.Popn5K, BmsStrings.ShowPopn5K),
        (BmsRulesetSetting.ShowPms9K, BmsLayoutVariant.Popn9K, BmsStrings.ShowPms9K),
        (BmsRulesetSetting.ShowBms5KDouble, BmsLayoutVariant.Bms5KDouble, BmsStrings.ShowBms5KDp),
        (BmsRulesetSetting.ShowBme7KDouble, BmsLayoutVariant.Bme7KDouble, BmsStrings.ShowBme7KDp),
        (BmsRulesetSetting.ShowPms9KDouble, BmsLayoutVariant.Popn9KDouble, BmsStrings.ShowPms9KDp),
        (BmsRulesetSetting.ShowKeyboard24K, BmsLayoutVariant.Keyboard24K, BmsStrings.ShowKeyboard24K),
        (BmsRulesetSetting.ShowKeyboard24KDouble, BmsLayoutVariant.Keyboard24KDouble, BmsStrings.ShowKeyboard24KDouble),
        (BmsRulesetSetting.ShowGenericKeyboard, BmsLayoutVariant.Generic, BmsStrings.ShowGenericKeyboard),
    ];
}
