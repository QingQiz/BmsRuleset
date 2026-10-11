using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.NoteTextures;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;

internal sealed partial class BmsResolvedNotePiece : CompositeDrawable
{
    // The sprite or animation owns the drawing; retaining this composite node adds
    // traversal without changing the inherited geometry or colour.
    protected override bool CanBeFlattened => !Masking && !HasProxy && !ForceLocalVertexBatch;

    private readonly float? widthForNoteHeightScale;
    private readonly BmsSkinComponentLookup lookup;

    private Drawable? noteAnimation;
    private Texture? appliedTexture;
    private float appliedDrawWidth;
    private float appliedNoteWidth;

    public BmsResolvedNotePiece(BmsSkinComponentLookup lookup, Texture[] textures, float? widthForNoteHeightScale)
    {
        this.lookup = lookup;
        this.widthForNoteHeightScale = widthForNoteHeightScale;

        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Origin = Anchor.TopLeft;

        if (textures.Length > 0)
            setTextures(textures);
    }

    [BackgroundDependencyLoader]
    private void load(ISkinSource skin)
    {
        if (noteAnimation == null)
            setTextures(BmsLegacyTextureResolver.ResolveNoteTextures(skin, lookup));
    }

    private void setTextures(Texture[] textures)
    {
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
            BmsResolvedTextureAnimation animation when animation.FrameCount > 0 => animation.CurrentFrame,
            Sprite sprite => sprite.Texture,
            TextureAnimation animation when animation.FrameCount > 0 => animation.CurrentFrame,
            _ => null,
        };

        if (texture == null)
            return;

        var noteWidth = widthForNoteHeightScale ?? DrawWidth;
        if (ReferenceEquals(texture, appliedTexture) && DrawWidth == appliedDrawWidth && noteWidth == appliedNoteWidth)
            return;

        appliedTexture = texture;
        appliedDrawWidth = DrawWidth;
        appliedNoteWidth = noteWidth;
        noteAnimation?.Scale = Vector2.Divide(new Vector2(DrawWidth, noteWidth), Math.Max(1, texture.DisplayWidth));
    }

    private static Drawable createTextureDrawable(Texture[] textures, bool looping)
    {
        if (textures.Length == 0)
            return Empty();

        if (textures.Length == 1)
            return new Sprite { Texture = textures[0] };

        return new BmsResolvedTextureAnimation(textures) { Loop = looping };
    }
}
