using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.LegacyDrawables;

/// <summary>
/// Keeps the legacy target texture continuous while its stage-level position places it below column lights and notes.
/// </summary>
internal sealed partial class LegacyBmsHitTarget : CompositeDrawable
{
    internal Drawable Target { get; }

    public LegacyBmsHitTarget(BmsLegacySkinTransformer transformer)
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;

        var targetImage = transformer.GetHitTargetImageName();
        var showJudgementLine = transformer.GetManiaConfig<bool>(LegacyManiaSkinConfigurationLookups.ShowJudgementLine)?.Value ?? true;
        var lineColour = transformer.GetManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.JudgementLineColour)?.Value ?? Color4.White;

        InternalChildren =
        [
            Target = transformer.GetLegacyAnimation(targetImage)?.With(d =>
            {
                d.RelativeSizeAxes = Axes.X;
                d.Width = 1;
                d.Scale = new Vector2(1, 1.44225f);
            }) ?? Empty(),
            new Box
            {
                Anchor = Anchor.CentreLeft,
                RelativeSizeAxes = Axes.X,
                Height = 1,
                Colour = lineColour,
                Alpha = showJudgementLine ? 0.9f : 0,
            },
        ];
    }
}
