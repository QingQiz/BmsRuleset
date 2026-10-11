using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Components;

namespace osu.Game.Rulesets.BmsRuleset.UI.Skinning.Runtime;

internal interface IBmsGameplaySkinDrawableSource
{
    BmsResolvedDrawableFactory? GetDrawableFactory(BmsSkinComponentLookup lookup);
}
