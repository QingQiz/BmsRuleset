using System;
using osu.Framework.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;

internal sealed class BmsResolvedDrawableFactory(Func<Drawable?> create)
{
    public Drawable? Create() => create();
}
