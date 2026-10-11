using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Layout;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

public abstract partial class DrawableBmsHitObject
{
    private BmsNoteVisual? singlePieceVisual;
    private BmsResolvedNoteSprite? unwrappedNoteSprite;

    internal BmsResolvedNoteSprite? SingleSpriteForBatch
        => CanBeFlattened && InternalChildren.Count == 2 && !Samples.IsAlive
           && unwrappedNoteSprite is { IsAlive: true } sprite && sprite.Parent == this ? sprite : null;

    protected override bool CheckChildrenLife()
    {
        var sprite = unwrappedNoteSprite;
        // This exact two-child tree has no lifetime transition to process: our sealed
        // sprite is always alive, while the framework sound has an empty lifetime.
        // Read the public state each time so expiry, skin changes and added children
        // immediately retain the framework's original lifecycle processing.
        if (sprite is { IsAlive: true, LifetimeStart: double.MinValue, LifetimeEnd: double.MaxValue }
            && Samples is { IsAlive: false, LifetimeStart: double.PositiveInfinity, LifetimeEnd: double.PositiveInfinity, RemoveWhenNotAlive: false }
            && InternalChildren.Count == 2 && sprite.Parent == this && Samples.Parent == this)
            return false;

        return base.CheckChildrenLife();
    }

    protected override void ApplySkin(ISkinSource skin, bool allowFallback)
    {
        base.ApplySkin(skin, allowFallback);
        // An idle or detached wrapper has no update traversal to run its pending reload.
        // The hit object's existing skin notification also covers its first pooled use.
        singlePieceVisual?.PreparePendingSkin();
    }

    private void refreshSinglePieceVisual()
    {
        var visual = singlePieceVisual;
        if (visual == null)
            return;
        if (!IsLoaded || !visual.IsLoaded)
            return;
        if (unwrappedNoteSprite == null && visual.Parent != this)
            return;
        if (visual.Drawable is BmsResolvedNoteSprite { IsLoaded: false })
            return;

        // Only our static sprite has a known parent-independent update contract. Retain
        // the original composition whenever the wrapper contributes a visual operation.
        var unwrap = visual.Drawable is BmsResolvedNoteSprite { IsLoaded: true }
                     && visual.Alpha == 1 && visual.Scale == Vector2.One
                     && visual.Position == Vector2.Zero && visual.Rotation == 0 && visual.Shear == Vector2.Zero
                     && visual.Anchor == Anchor.TopLeft && visual.Origin == Anchor.TopLeft
                     && visual.RelativeSizeAxes == Axes.X && visual.Size == new Vector2(1, 0)
                     && visual.Margin.Equals(default(MarginPadding)) && visual.Padding.Equals(default(MarginPadding))
                     && !visual.Masking && !visual.HasProxy && !visual.ForceLocalVertexBatch
                     && visual.Colour.Equals((ColourInfo)Colour4.White)
                     && visual.RelativeChildSize == Vector2.One && visual.RelativeChildOffset == Vector2.Zero
                     && RelativeChildSize == Vector2.One && RelativeChildOffset == Vector2.Zero
                     && Padding.Equals(default(MarginPadding)) && DrawHeight == 0
                     && DrawColourInfo.Colour.HasSingleColour;

        if (!unwrap)
        {
            restoreSinglePieceWrapper();
            return;
        }
        if (unwrappedNoteSprite != null)
            return;

        var sprite = (BmsResolvedNoteSprite)visual.Drawable;
        visual.DetachComponent();
        RemoveInternal(visual, false);
        AddInternal(unwrappedNoteSprite = sprite);
    }

    private void restoreSinglePieceWrapper()
    {
        if (unwrappedNoteSprite == null)
            return;

        RemoveInternal(unwrappedNoteSprite, false);
        unwrappedNoteSprite = null;
        singlePieceVisual!.AttachComponent();
        AddInternal(singlePieceVisual);
    }

    protected override void Dispose(bool isDisposing)
    {
        if (singlePieceVisual != null)
        {
            singlePieceVisual.ComponentChanging -= restoreSinglePieceWrapper;
            singlePieceVisual.ComponentChanged -= refreshSinglePieceVisual;
            singlePieceVisual.ComponentInvalidated -= restoreSinglePieceWrapper;
            // Detached controllers remain registered for idle skin preparation and still
            // own a skin-source subscription, so their lifetime must follow the hit object.
            if (singlePieceVisual.Parent == null)
                singlePieceVisual.Dispose();
        }
        base.Dispose(isDisposing);
    }
}
