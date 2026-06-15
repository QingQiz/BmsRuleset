using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;

internal static class BmsGameplaySkinDrawableResolver
{
    public static BmsResolvedDrawableFactory Resolve(ISkinSource skin, BmsSkinComponentLookup lookup)
    {
        if (skin is IBmsGameplaySkinDrawableSource source
            && source.GetDrawableFactory(lookup) is { } sourceFactory)
        {
            return sourceFactory;
        }

        foreach (var provider in skin.AllSources)
        {
            if (provider is IBmsGameplaySkinDrawableSource factorySource
                && factorySource.GetDrawableFactory(lookup) is { } factory)
            {
                return factory;
            }
        }

        return new BmsResolvedDrawableFactory(() => skin.GetDrawableComponent(lookup));
    }
}
