using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;

/// <summary>
/// Legacy left/right stage side panels.
/// </summary>
/// <remarks>
/// Side panels are anchored outside the playfield edges and vertically stretched to the current
/// stage height. Texture height is used when available so animations and sprites scale consistently.
/// </remarks>
internal sealed partial class LegacyBmsStageBackground : CompositeDrawable
{
    private Drawable? leftSprite;
    private Drawable? rightSprite;
    private readonly string[] images;

    [Resolved(CanBeNull = true)]
    private BmsPlayfield? playfield { get; set; }

    public LegacyBmsStageBackground(BmsLegacySkinTransformer transformer)
    {
        RelativeSizeAxes = Axes.Both;
        Masking = false;

        images = transformer.GetStageBackgroundImageNames();
    }

    [BackgroundDependencyLoader]
    private void load(ISkinSource skin)
    {
        InternalChildren =
        [
            leftSprite = skin.GetAnimation(images[0], true, true)?.With(d =>
            {
                d.Anchor = Anchor.TopLeft;
                d.Origin = Anchor.TopRight;
            }) ?? Empty(),
            rightSprite = skin.GetAnimation(images[1], true, true)?.With(d =>
            {
                d.Anchor = Anchor.TopRight;
                d.Origin = Anchor.TopLeft;
            }) ?? Empty(),
        ];
    }

    protected override void Update()
    {
        base.Update();

        if (leftSprite != null)
            scaleStageSide(leftSprite);

        if (rightSprite != null)
            scaleStageSide(rightSprite);
    }

    private void scaleStageSide(Drawable sprite)
    {
        var height = sprite switch
        {
            Sprite s when s.Texture != null => s.Texture.DisplayHeight,
            TextureAnimation a when a.CurrentFrame != null => a.CurrentFrame.DisplayHeight,
            _ => sprite.Height,
        };

        if (height > 0)
        {
            var stageScaleX = playfield?.Stage.Scale.X ?? 1;
            sprite.Scale = new Vector2(HorizontalScaleFor(stageScaleX), DrawHeight / height);
        }
    }

    internal static float HorizontalScaleFor(float stageScaleX) => 1 / Math.Max(0.001f, Math.Abs(stageScaleX));
}
