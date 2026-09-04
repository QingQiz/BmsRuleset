using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.NoteTextures;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;
using RectangleF = osu.Framework.Graphics.Primitives.RectangleF;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.LnHelper;

/// <summary>
/// Draws a long-note body from legacy skin textures using BMS segment ordering rules.
/// </summary>
/// <remarks>
/// This class owns animation-frame selection, spatial crop rendering, and sprite pooling.
/// Texture source lookup is delegated to <see cref="BmsLongNoteBodySource" /> and spatial
/// segment ordering is delegated to <see cref="BmsLongNoteSegmentComposer" />.
/// </remarks>
public sealed partial class BmsSegmentedLongNoteBody : CompositeDrawable
{

    public Color4 BodyColour
    {
        get => fallback.Colour;
        set => fallback.Colour = value;
    }

    private const double body_animation_frame_length = 30;

    private readonly Box fallback;
    private readonly List<Sprite> spritePool = [];

    private readonly List<(int SegmentIndex, float Height)> reusablePartSizes = new();
    private readonly List<BmsLongNoteSegmentComposer.Part> reusableParts = new();

    // Animation frames are time-varying images (for example classic mania-note1L-0..5).
    // The selected frame is stretched across the complete body content behind the dynamic mask.
    private Texture[] bodyFrames = [];

    // Spatial slices are top-to-bottom body pieces. For ultra-tall raw resources these are decoded
    // before texture upload so the GPU max-size limiter never flattens the source image first.
    private Texture[] slices = [];
    private float[] naturalHeights = [];
    private IReadOnlyList<BmsLongNoteSegmentComposer.Part> parts = [];

    private float lastBodyHeight = -1;
    private float lastDrawWidth = -1;
    private float contentHeight = -1;
    private float contentOffset;
    private int currentFrameIndex;
    private bool tailAtTop;
    private int? column;
    private BmsLayoutVariant layoutVariant;
    private bool slicesDirty = true;
    private BmsLongNoteBodySource.BmsLongNoteBodyTextureCache? fallbackTextureCache;

    [Resolved(CanBeNull = true)]
    private ISkinSource? skin { get; set; }

    [Resolved(CanBeNull = true)]
    private BmsGameplaySkinCache? gameplaySkinCache { get; set; }

    [Resolved]
    private IRenderer renderer { get; set; } = null!;

    public BmsSegmentedLongNoteBody()
    {
        Masking = true;
        RelativeSizeAxes = Axes.X;

        InternalChild = fallback = new Box
        {
            RelativeSizeAxes = Axes.Both,
        };
    }

    #region Disposal

    protected override void Dispose(bool isDisposing)
    {
        if (skin != null)
            skin.SourceChanged -= onSkinChanged;

        fallbackTextureCache?.Dispose();

        base.Dispose(isDisposing);
    }

    #endregion

    public void SetSkinLookup(BmsLayoutVariant newLayoutVariant, int newColumn)
    {
        if (column == newColumn && layoutVariant == newLayoutVariant)
            return;

        column = newColumn;
        layoutVariant = newLayoutVariant;
        slicesDirty = true;
        // Column affects legacy fallback names (mania-note1L vs mania-note2L vs scratch), so changing
        // it requires a full texture reload rather than just recomposing existing parts.
        ensureSlicesLoaded();
    }

    public void ResetBody()
    {
        lastBodyHeight = -1;
        contentHeight = -1;
        contentOffset = 0;
    }

    public void UpdateAnimation(bool isHolding)
    {
        ensureSlicesLoaded();

        if (bodyFrames.Length <= 1 || (!isHolding && currentFrameIndex == 0))
            return;

        var nextFrameIndex = isHolding
            ? (int)(Time.Current / body_animation_frame_length) % bodyFrames.Length
            : 0;

        if (currentFrameIndex == nextFrameIndex)
            return;

        currentFrameIndex = nextFrameIndex;
        updateAnimationFrameTexture();
    }

    public void UpdateBody(float bodyHeight, bool newTailAtTop, bool isHolding)
    {
        ensureSlicesLoaded();

        if (slices.Length == 0 && bodyFrames.Length == 0)
            return;

        var heightChanged = Math.Abs(lastBodyHeight - bodyHeight) >= 0.5f;
        var width = Math.Max(1, DrawWidth);
        var widthChanged = Math.Abs(lastDrawWidth - width) >= 1;
        var directionChanged = tailAtTop != newTailAtTop;
        var nextFrameIndex = isHolding && bodyFrames.Length > 1
            ? (int)(Time.Current / body_animation_frame_length) % bodyFrames.Length
            : 0;
        var frameChanged = currentFrameIndex != nextFrameIndex;
        var capacityExceeded = bodyHeight > contentHeight;

        if (!heightChanged && !widthChanged && !directionChanged && !frameChanged && !capacityExceeded)
            return;

        lastBodyHeight = bodyHeight;
        lastDrawWidth = width;
        currentFrameIndex = nextFrameIndex;
        tailAtTop = newTailAtTop;

        if (capacityExceeded)
            contentHeight = bodyHeight;

        if (bodyFrames.Length > 0)
        {
            if (spritePool.Count == 0)
            {
                rebuildSegments();
                return;
            }

            if (heightChanged || capacityExceeded || directionChanged)
                updateAnimationFrameGeometry();

            if (frameChanged)
                updateAnimationFrameTexture();

            updateContentAlignment();
            return;
        }

        if (widthChanged || directionChanged || frameChanged || capacityExceeded)
            rebuildSegments();
        else
            updateContentAlignment();
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (skin != null)
            skin.SourceChanged += onSkinChanged;

        slicesDirty = true;
        ensureSlicesLoaded();
    }

    private void onSkinChanged() => Scheduler.AddOnce(invalidateSkin);

    private void invalidateSkin()
    {
        // Do not synchronously resolve/decode textures for every active LN on a skin switch. A replay
        // can have many pooled LN drawables alive; forcing all of them to reload immediately causes a
        // visible hitch. Mark dirty and let the next UpdateBody() reload only drawables that are used.
        slicesDirty = true;
        fallbackTextureCache?.Clear();
    }

    private void ensureSlicesLoaded()
    {
        if (!slicesDirty)
            return;

        slicesDirty = false;
        loadSlices();
        rebuildSegments();
    }

    private void loadSlices()
    {
        Masking = true;
        bodyFrames = [];
        slices = [];
        naturalHeights = [];
        parts = [];
        currentFrameIndex = 0;
        foreach (var sprite in spritePool)
            RemoveInternal(sprite, true);

        spritePool.Clear();

        if (skin == null || column == null)
            return;

        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.HoldNoteBody, layoutVariant, column.Value);
        var textures = gameplaySkinCache != null
            ? gameplaySkinCache.GetLongNoteBodyTextureSet(lookup, renderer)
            : BmsLongNoteBodySource.Resolve(skin, lookup, renderer, fallbackTextureCache ??= new BmsLongNoteBodySource.BmsLongNoteBodyTextureCache());

        if (textures == null)
            return;

        if (textures.Value.Kind == BmsLongNoteBodyTextureKind.SpatialSlices)
        {
            // Raw ultra-tall source was already split before upload; preserve those spatial slices
            // exactly and let the composer decide which ones repeat for the requested body height.
            slices = textures.Value.Textures;
            naturalHeights = slices.Select(displayHeightFor).ToArray();
        }
        else
        {
            // Animation frames share one full-body texture phase. Geometry and UV cropping expose
            // the visible length directly, so advancing the animation only replaces the texture.
            bodyFrames = textures.Value.Textures;
            Masking = false;
        }
    }

    private void rebuildSegments()
    {
        fallback.Alpha = 1;
        parts = [];

        if (bodyFrames.Length > 0 && contentHeight > 0)
        {
            fallback.Alpha = 0;
            ensureSpritePoolSize(1);

            for (var i = 1; i < spritePool.Count; i++)
                spritePool[i].Alpha = 0;

            updateAnimationFrameGeometry();
            updateAnimationFrameTexture();
            updateContentAlignment();
            return;
        }

        if (slices.Length == 0 || contentHeight <= 0)
        {
            syncSprites();
            updateContentAlignment();
            return;
        }

        if (naturalHeights.Length != slices.Length)
            naturalHeights = new float[slices.Length];

        for (var i = 0; i < slices.Length; i++)
            naturalHeights[i] = displayHeightFor(slices[i]);

        fallback.Alpha = 0;
        parts = BmsLongNoteSegmentComposer.ComposeInto(naturalHeights, contentHeight, tailAtTop, reusablePartSizes, reusableParts);
        syncSprites();
        updateContentAlignment();
    }

    private void updateAnimationFrameGeometry()
    {
        var visibleHeight = Math.Max(1, lastBodyHeight);
        contentOffset = 0;

        if (spritePool.Count == 0)
            return;

        var sprite = spritePool[0];
        sprite.Alpha = lastBodyHeight > 0 ? 1 : 0;
        sprite.Height = visibleHeight;
        sprite.Scale = new Vector2(1, tailAtTop ? 1 : -1);
        sprite.Y = tailAtTop ? 0 : visibleHeight;
        sprite.TextureRectangle = new RectangleF(0, 0, 1, Math.Max(1, contentHeight / visibleHeight));
    }

    private void updateAnimationFrameTexture()
    {
        if (spritePool.Count == 0 || bodyFrames.Length == 0)
            return;

        spritePool[0].Texture = bodyFrames[Math.Clamp(currentFrameIndex, 0, bodyFrames.Length - 1)];
    }

    private void updateContentAlignment()
    {
        var y = bodyFrames.Length > 0 || tailAtTop ? 0 : lastBodyHeight - contentHeight;

        if (Math.Abs(contentOffset - y) <= 0.5f)
            return;

        contentOffset = y;
        syncSpritePositions();
    }

    private float displayHeightFor(Texture texture)
    {
        var width = Math.Max(1, lastDrawWidth > 0 ? lastDrawWidth : DrawWidth);
        return Math.Max(1, texture.DisplayHeight * width / Math.Max(1, texture.DisplayWidth));
    }

    private void syncSprites()
    {
        ensureSpritePoolSize(parts.Count);

        for (var i = parts.Count; i < spritePool.Count; i++)
            spritePool[i].Alpha = 0;

        for (var i = 0; i < parts.Count; i++)
        {
            var sprite = spritePool[i];
            var part = parts[i];
            sprite.Alpha = 1;
            sprite.Texture = slices[part.SegmentIndex];
            sprite.Height = part.Height;
            sprite.Scale = new Vector2(1, part.FlipY ? -1 : 1);
            sprite.Y = contentOffset + part.Y;
        }
    }

    private void syncSpritePositions()
    {
        for (var i = 0; i < parts.Count; i++)
            spritePool[i].Y = contentOffset + parts[i].Y;
    }

    private void ensureSpritePoolSize(int count)
    {
        while (spritePool.Count < count)
        {
            // Retain the high-water capacity so pooled LN drawables can change skin, direction, or
            // content length without repeatedly creating and destroying child drawables.
            var sprite = new Sprite
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.X,
                Width = 1,
                FillMode = FillMode.Stretch,
            };
            spritePool.Add(sprite);
            AddInternal(sprite);
        }
    }
}
