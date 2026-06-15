using osu.Game.Rulesets.BmsRuleset.Skinning.Components;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;

internal interface IBmsGameplaySkinDrawableSource
{
    BmsResolvedDrawableFactory? GetDrawableFactory(BmsSkinComponentLookup lookup);
}
