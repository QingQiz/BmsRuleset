using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;

/// <summary>
/// Keeps legacy separators from covering key-area artwork below the hit target, matching mania's stage composition.
/// </summary>
internal sealed partial class BmsHitTargetInsetContainer : Container
{
    protected override Container<Drawable> Content => content;

    private readonly Container content;

    [Resolved(CanBeNull = true)]
    private BmsPlayfield? playfield { get; set; }

    public BmsHitTargetInsetContainer()
    {
        RelativeSizeAxes = Axes.Both;
        InternalChild = content = new Container { RelativeSizeAxes = Axes.Both };
    }

    protected override void Update()
    {
        base.Update();

        var bottomInset = playfield?.Stage.HitTargetPosition ?? BmsStage.HIT_TARGET_POSITION;

        if (content.Padding.Bottom == bottomInset)
            return;

        content.Padding = new MarginPadding
        {
            Bottom = bottomInset,
        };
    }
}
