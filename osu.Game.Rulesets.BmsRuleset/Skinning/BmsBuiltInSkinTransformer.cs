using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning;

public class BmsBuiltInSkinTransformer(ISkin skin) : SkinTransformer(skin)
{
    public override Drawable? GetDrawableComponent(ISkinComponentLookup lookup)
    {
        if (lookup is BmsSkinComponentLookup or SkinComponentLookup<HitResult>)
            return null;

        return base.GetDrawableComponent(lookup);
    }

    public override IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
    {
        if (lookup is BmsSkinConfigurationLookup)
            return null;

        return base.GetConfig<TLookup, TValue>(lookup);
    }
}
