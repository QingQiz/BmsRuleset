using System;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Sprites;
using osu.Game.Rulesets.Judgements;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;

internal sealed partial class BmsJudgementDrawable : SkinReloadableDrawable
{
    internal Drawable Drawable { get; private set; } = null!;

    private readonly ISkinComponentLookup defaultLookup;
    private readonly Bindable<string> image;
    private readonly bool missAnimation;
    private readonly Func<Drawable>? createDefault;

    internal BmsJudgementDrawable(ISkinComponentLookup defaultLookup, Bindable<string> image, bool missAnimation, Func<Drawable>? createDefault = null)
    {
        this.defaultLookup = defaultLookup;
        this.image = image.GetBoundCopy();
        this.missAnimation = missAnimation;
        this.createDefault = createDefault;
        AutoSizeAxes = Axes.Both;
    }

    protected override void LoadAsyncComplete()
    {
        base.LoadAsyncComplete();

        // The hidden judgement pool does not run LoadComplete for unused grades.
        image.BindValueChanged(_ => SkinChanged(CurrentSkin));
    }

    protected override void SkinChanged(ISkinSource skin)
    {
        var texture = string.IsNullOrEmpty(image.Value) ? null : skin.GetTexture(image.Value);
        Drawable = texture != null
            ? new BmsJudgementPiece(missAnimation, new Sprite { Texture = texture }) { Alpha = 0 }
            : skin.GetDrawableComponent(defaultLookup) ?? (createDefault != null ? new BmsJudgementPiece(missAnimation, createDefault()) : Empty());
        Drawable.Anchor = Anchor.Centre;
        Drawable.Origin = Anchor.Centre;
        InternalChild = Drawable;
    }

    internal void PlayAnimation()
    {
        FlushPendingSkinChanges();
        if (Drawable is IAnimatableJudgement animatable)
        {
            (Drawable as IFramedAnimation)?.GotoFrame(0);
            animatable.PlayAnimation();
        }
    }
}
