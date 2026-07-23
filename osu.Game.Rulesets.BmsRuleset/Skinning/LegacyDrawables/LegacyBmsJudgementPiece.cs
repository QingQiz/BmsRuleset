using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.LegacyDrawables;

/// <summary>
/// Legacy BMS judgement image wrapper.
/// </summary>
/// <remarks>
/// BMS judgement names are mapped to osu!mania legacy image names by
/// BmsLegacySkinTransformer. This drawable only owns the playback transform:
/// PGREAT/GREAT/GOOD use the shrinking stable-style pop, while POOR/E-POOR use a simpler pulse.
/// </remarks>
internal sealed partial class LegacyBmsJudgementPiece : CompositeDrawable, IAnimatableJudgement
{
    private readonly HitResult result;
    private readonly Drawable animation;

    public LegacyBmsJudgementPiece(HitResult result, Drawable animation)
    {
        this.result = result;
        this.animation = animation;

        Origin = Anchor.Centre;
        AutoSizeAxes = Axes.Both;
    }

    public void PlayAnimation()
    {
        (animation as IFramedAnimation)?.GotoFrame(0);

        this.FadeInFromZero(20, Easing.Out)
            .Then().Delay(160)
            .FadeOutFromOne(40, Easing.In);

        if (result is HitResult.Meh or HitResult.Miss)
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

    protected override void LoadComplete()
    {
        base.LoadComplete();

        InternalChild = animation.With(d =>
        {
            d.Anchor = Anchor.Centre;
            d.Origin = Anchor.Centre;
        });
    }
}
