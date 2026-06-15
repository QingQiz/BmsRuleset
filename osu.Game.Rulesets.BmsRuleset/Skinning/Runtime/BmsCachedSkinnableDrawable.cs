using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;

internal partial class BmsCachedSkinnableDrawable : SkinReloadableDrawable
{
    public Drawable Drawable { get; private set; } = null!;

    public bool CentreComponent = true;

    private readonly BmsSkinComponentLookup componentLookup;
    private readonly Func<BmsSkinComponentLookup, Drawable>? createDefault;

    [Resolved(CanBeNull = true)]
    private BmsGameplaySkinCache? gameplaySkinCache { get; set; }

    public BmsCachedSkinnableDrawable(BmsSkinComponentLookup lookup, Func<BmsSkinComponentLookup, Drawable>? defaultImplementation = null)
    {
        componentLookup = lookup;
        createDefault = defaultImplementation;

        RelativeSizeAxes = Axes.Both;
    }

    public void ResetAnimation() => (Drawable as IFramedAnimation)?.GotoFrame(0);

    protected override void SkinChanged(ISkinSource skin)
    {
        var retrieved = gameplaySkinCache != null
            ? gameplaySkinCache.GetDrawableFactory(componentLookup)?.Create()
            : skin.GetDrawableComponent(componentLookup);

        if (retrieved == null)
        {
            Drawable = createDefault?.Invoke(componentLookup) ?? Empty();
        }
        else
        {
            Drawable = retrieved;
        }

        if (CentreComponent)
        {
            Drawable.Origin = Anchor.Centre;
            Drawable.Anchor = Anchor.Centre;
        }

        InternalChild = Drawable;
    }
}
