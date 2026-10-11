using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Layout;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;

/// <summary>
/// Renders a legacy note backed by one texture without introducing a composite wrapper.
/// </summary>
internal sealed partial class BmsResolvedNoteSprite : Sprite
{
    private readonly float? widthForNoteHeightScale;
    private Texture? appliedTexture;
    private float appliedDrawWidth;
    private float appliedNoteWidth;
    private bool sizingDirty = true;

    internal bool IsMaskedForBatch { get; private set; }

    protected override bool ComputeIsMaskedAway(RectangleF maskingBounds)
        => IsMaskedForBatch = base.ComputeIsMaskedAway(maskingBounds);

    internal BmsResolvedNoteSprite(Texture texture, float? widthForNoteHeightScale)
    {
        this.widthForNoteHeightScale = widthForNoteHeightScale;

        Anchor = Anchor.TopLeft;
        Origin = Anchor.TopLeft;
        RelativeSizeAxes = Axes.X;
        Texture = texture;
        Height = texture.DisplayHeight;
    }

    protected override void Update()
    {
        base.Update();

        var texture = Texture;
        if (texture == null || (!sizingDirty && ReferenceEquals(texture, appliedTexture)))
            return;

        var drawWidth = DrawWidth;
        var noteWidth = widthForNoteHeightScale ?? drawWidth;
        sizingDirty = false;
        if (ReferenceEquals(texture, appliedTexture) && drawWidth == appliedDrawWidth && noteWidth == appliedNoteWidth)
            return;

        appliedTexture = texture;
        appliedDrawWidth = drawWidth;
        appliedNoteWidth = noteWidth;

        // Keep the legacy width-based scaling rule while letting this Sprite own the final height.
        Height = drawWidth == 0 ? 0 : texture.DisplayHeight * (noteWidth / Math.Max(1, texture.DisplayWidth));
    }

    protected override bool OnInvalidate(Invalidation invalidation, InvalidationSource source)
    {
        // Scrolling invalidates DrawInfo without changing the width used for note-height scaling.
        if ((invalidation & (Invalidation.DrawSize | Invalidation.Parent)) != 0)
            sizingDirty = true;

        return base.OnInvalidate(invalidation, source);
    }

    protected override void OnSizingChanged()
    {
        sizingDirty = true;
        base.OnSizingChanged();
    }

    protected override Quad ComputeScreenSpaceDrawQuad()
    {
        if (EdgeSmoothness != Vector2.Zero)
            return base.ComputeScreenSpaceDrawQuad();

        var matrix = DrawInfo.Matrix;
        var translation = matrix.Row2.Xy;
        var size = DrawSize;
        // The unsmoothed draw rectangle starts at zero. Each basis product is shared by two corners;
        // the framework still owns invalidation, masking and each draw-buffer snapshot.
        var horizontal = matrix.Row0.Xy * size.X;
        var vertical = matrix.Row1.Xy * size.Y;
        return new Quad(translation, horizontal + translation, vertical + translation, horizontal + vertical + translation);
    }
}
