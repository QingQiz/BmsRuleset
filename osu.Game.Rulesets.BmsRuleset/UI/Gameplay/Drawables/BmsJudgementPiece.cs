using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.Judgements;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;

internal sealed partial class BmsJudgementPiece : CompositeDrawable, IAnimatableJudgement
{
    private readonly bool missed;
    private readonly Drawable animation;

    public BmsJudgementPiece(bool missed, Drawable animation)
    {
        this.missed = missed;
        this.animation = animation;

        Origin = Anchor.Centre;
        AutoSizeAxes = Axes.Both;
        InternalChild = animation.With(drawable =>
        {
            drawable.Anchor = Anchor.Centre;
            drawable.Origin = Anchor.Centre;
        });
    }

    public void PlayAnimation()
    {
        (animation as IFramedAnimation)?.GotoFrame(0);

        this.FadeInFromZero(20, Easing.Out)
            .Then().Delay(160)
            .FadeOutFromOne(40, Easing.In);

        if (missed)
        {
            animation.ScaleTo(1.2f).Then().ScaleTo(1, 100, Easing.Out);
            return;
        }

        animation.ScaleTo(0.8f)
            .Then().ScaleTo(1, 40)
            .Then().ScaleTo(0.85f)
            .Then().ScaleTo(0.7f, 40)
            .Then().Delay(100)
            .Then().ScaleTo(0.4f, 40, Easing.In);
    }

    public Drawable? GetAboveHitObjectsProxiedContent() => null;

}
