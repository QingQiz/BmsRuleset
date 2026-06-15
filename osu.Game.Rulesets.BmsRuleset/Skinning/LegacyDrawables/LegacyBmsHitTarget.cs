using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Skinning;
using osuTK;
using osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.LegacyDrawables;

/// <summary>
/// Stage-wide legacy judgement-line drawable.
/// </summary>
/// <remarks>
/// The target image is stretched to lane width and overlaid with the optional judgement line from
/// skin.ini. It is requested only once for the stage, not per-column.
/// </remarks>
internal sealed partial class LegacyBmsHitTarget : CompositeDrawable
{
    public LegacyBmsHitTarget(BmsLegacySkinTransformer transformer)
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;

        var targetImage = transformer.GetManiaConfig<string>(LegacyManiaSkinConfigurationLookups.HitTargetImage)?.Value ?? "mania-stage-hint";
        var showJudgementLine = transformer.GetManiaConfig<bool>(LegacyManiaSkinConfigurationLookups.ShowJudgementLine)?.Value ?? true;
        var lineColour = transformer.GetManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.JudgementLineColour)?.Value ?? Color4.White;
        var target = transformer.GetLegacyAnimation(targetImage);

        InternalChild = new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Children =
            [
                target?.With(d =>
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
            ],
        };
    }
}
