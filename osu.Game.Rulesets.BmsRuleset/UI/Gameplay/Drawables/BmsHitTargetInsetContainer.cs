using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Configuration;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;

/// <summary>
/// Keeps legacy separators from covering key-area artwork below the hit target, matching mania's stage composition.
/// </summary>
internal sealed partial class BmsHitTargetInsetContainer : Container
{
    [Resolved(CanBeNull = true)]
    private BmsPlayfield? playfield { get; set; }

    private float skinHitPosition = BmsStage.HIT_TARGET_POSITION;

    [BackgroundDependencyLoader]
    private void load(ISkinSource skin)
    {
        if (playfield == null)
            skinHitPosition = skin.GetConfig<BmsSkinConfigurationLookup, float>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.HitPosition))?.Value ?? BmsStage.HIT_TARGET_POSITION;
    }

    public BmsHitTargetInsetContainer()
    {
        RelativeSizeAxes = Axes.Both;
    }

    protected override void Update()
    {
        base.Update();

        var bottomInset = playfield?.Stage.HitTargetPosition ?? skinHitPosition;

        if (Padding.Bottom == bottomInset)
            return;

        Padding = new MarginPadding
        {
            Bottom = bottomInset,
        };
    }
}
