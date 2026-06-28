using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;
using osu.Game.Skinning;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.LegacyDrawables;

/// <summary>
/// Per-column judgement-line fallback supplied by the legacy skin layer.
/// </summary>
internal sealed partial class LegacyBmsColumnHitTarget : CompositeDrawable
{
    public LegacyBmsColumnHitTarget(BmsLegacySkinTransformer transformer, BmsSkinComponentLookup lookup)
    {
        RelativeSizeAxes = Axes.X;
        Height = lookup.IsScratch ? 5 : 3;

        var showJudgementLine = transformer.GetManiaConfig<bool>(LegacyManiaSkinConfigurationLookups.ShowJudgementLine)?.Value ?? true;
        var lineColour = transformer.GetManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.JudgementLineColour)?.Value ?? Color4.White;

        InternalChild = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Colour = lineColour,
            Alpha = showJudgementLine ? 1 : 0,
        };
    }
}
