using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.Drawables;

internal sealed partial class BmsResolvedNotePiece : CompositeDrawable
{
    private readonly float? widthForNoteHeightScale;
    private readonly Drawable noteAnimation;

    public BmsResolvedNotePiece(Texture[] textures, float? widthForNoteHeightScale)
    {
        this.widthForNoteHeightScale = widthForNoteHeightScale;

        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Origin = Anchor.TopLeft;

        InternalChild = noteAnimation = createTextureDrawable(textures, true).With(d =>
        {
            d.Anchor = Anchor.TopLeft;
            d.Origin = Anchor.TopLeft;
        });
    }

    protected override void Update()
    {
        base.Update();

        var texture = noteAnimation switch
        {
            Sprite sprite => sprite.Texture,
            TextureAnimation animation when animation.FrameCount > 0 => animation.CurrentFrame,
            _ => null,
        };

        if (texture == null)
            return;

        var noteWidth = widthForNoteHeightScale ?? DrawWidth;
        noteAnimation.Scale = Vector2.Divide(new Vector2(DrawWidth, noteWidth), Math.Max(1, texture.DisplayWidth));
    }

    private static Drawable createTextureDrawable(Texture[] textures, bool looping)
    {
        if (textures.Length == 0)
            return Empty();

        if (textures.Length == 1)
            return new Sprite { Texture = textures[0] };

        var animation = new TextureAnimation
        {
            DefaultFrameLength = 1000 / 60d,
            Loop = looping,
        };

        foreach (var texture in textures)
            animation.AddFrame(texture);

        return animation;
    }
}
