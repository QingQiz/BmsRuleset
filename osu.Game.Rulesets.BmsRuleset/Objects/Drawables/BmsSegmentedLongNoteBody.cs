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
using osu.Game.Rulesets.BmsRuleset.Skinning;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;
using RectangleF = osu.Framework.Graphics.Primitives.RectangleF;

namespace osu.Game.Rulesets.BmsRuleset.Objects.Drawables;

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

    private const float max_source_slice_height = 1024;
    private const double body_animation_frame_length = 30;

    private readonly Box fallback;
    private readonly Container segmentContainer;
    private readonly List<Sprite> spritePool = [];

    private readonly List<(int SegmentIndex, float Height)> reusablePartSizes = new();
    private readonly List<BmsLongNoteSegmentComposer.Part> reusableParts = new();

    // Animation frames are time-varying images (for example classic mania-note1L-0..5).
    // They are not spatial segments. We choose one frame, then split that frame spatially if needed.
    private Texture[] bodyFrames = [];

    // Spatial slices are top-to-bottom body pieces. For ultra-tall raw resources these are decoded
    // before texture upload so the GPU max-size limiter never flattens the source image first.
    private Texture[] slices = [];
    private float[] naturalHeights = [];
    private IReadOnlyList<BmsLongNoteSegmentComposer.Part> parts = [];

    private float lastBodyHeight = -1;
    private float lastDrawWidth = -1;
    private int currentFrameIndex;
    private bool tailAtTop;
    private int? column;
    private BmsLayoutVariant layoutVariant;
    private bool slicesDirty = true;

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

        InternalChildren =
        [
            fallback = new Box
            {
                RelativeSizeAxes = Axes.Both,
            },
            segmentContainer = new Container
            {
                RelativeSizeAxes = Axes.X,
            },
        ];
    }

    #region Disposal

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);

        if (skin != null)
            skin.SourceChanged -= onSkinChanged;
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

    public void UpdateBody(float bodyHeight, bool newTailAtTop, bool isHolding)
    {
        if (slices.Length == 0 && bodyFrames.Length == 0)
            return;

        ensureSlicesLoaded();

        var heightChanged = Math.Abs(lastBodyHeight - bodyHeight) >= 1;
        var width = Math.Max(1, DrawWidth);
        var widthChanged = Math.Abs(lastDrawWidth - width) >= 1;
        var directionChanged = tailAtTop != newTailAtTop;
        var nextFrameIndex = isHolding && bodyFrames.Length > 1
            ? (int)(Time.Current / body_animation_frame_length) % bodyFrames.Length
            : 0;
        var frameChanged = currentFrameIndex != nextFrameIndex;

        if (!heightChanged && !widthChanged && !directionChanged && !frameChanged)
            return;

        lastBodyHeight = bodyHeight;
        lastDrawWidth = width;
        currentFrameIndex = nextFrameIndex;
        tailAtTop = newTailAtTop;

        // Classic -0..5 LN bodies are animation frames that advance only while the note is held.
        // A frame change can alter aspect ratio, so rebuild spatial slices from the selected frame.
        if (bodyFrames.Length > 0)
            updateSlicesFromCurrentFrame();

        rebuildSegments();
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (skin != null)
            skin.SourceChanged += onSkinChanged;

        slicesDirty = true;
        ensureSlicesLoaded();
    }

    private void onSkinChanged()
    {
        // Do not synchronously resolve/decode textures for every active LN on a skin switch. A replay
        // can have many pooled LN drawables alive; forcing all of them to reload immediately causes a
        // visible hitch. Mark dirty and let the next UpdateBody() reload only drawables that are used.
        slicesDirty = true;
        bodyFrames = [];
        slices = [];
        naturalHeights = [];
        parts = [];
        reusablePartSizes.Clear();
        reusableParts.Clear();
        segmentContainer.Clear(disposeChildren: true);
        spritePool.Clear();
        fallback.Alpha = 1;
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
        bodyFrames = [];
        slices = [];
        naturalHeights = [];
        parts = [];
        currentFrameIndex = 0;
        segmentContainer.Clear(disposeChildren: true);
        spritePool.Clear();

        if (skin == null || column == null)
            return;

        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.HoldNoteBody, layoutVariant, column.Value);
        var textures = gameplaySkinCache?.GetLongNoteBodyTextureSet(lookup, renderer)
                       ?? BmsLongNoteBodySource.Resolve(skin, lookup, renderer);

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
            // Normal legacy lookup returned animation frames. They are not spatial segments; pick
            // one frame in UpdateBody(), then crop only that frame if it is still too tall to draw.
            bodyFrames = textures.Value.Textures;
            updateSlicesFromCurrentFrame();
        }
    }

    private void updateSlicesFromCurrentFrame()
    {
        slices = [];
        naturalHeights = [];

        if (bodyFrames.Length == 0)
            return;

        var texture = bodyFrames[Math.Clamp(currentFrameIndex, 0, bodyFrames.Length - 1)];

        if (texture.DisplayWidth <= 0 || texture.DisplayHeight <= 0)
            return;

        // For ordinary textures this crop happens after texture upload, so it cannot fix max-size
        // downscaling. It is still useful for sprite geometry: huge body frames are drawn as several
        // manageable cropped regions rather than one very tall sprite.
        var count = Math.Max(1, (int)Math.Ceiling(texture.DisplayHeight / max_source_slice_height));
        var sliceTextures = new Texture[count];
        var sliceHeights = new float[count];

        for (var i = 0; i < count; i++)
        {
            var y = texture.Height * i / (float)count;
            var nextY = texture.Height * (i + 1) / (float)count;
            var height = nextY - y;

            var slice = texture.Crop(new RectangleF(0, y, texture.Width, height), wrapModeS: WrapMode.ClampToEdge, wrapModeT: WrapMode.ClampToEdge);
            slice.ScaleAdjust = texture.ScaleAdjust;
            sliceTextures[i] = slice;
            sliceHeights[i] = displayHeightFor(slice);
        }

        slices = sliceTextures;
        naturalHeights = sliceHeights;
    }

    private void rebuildSegments()
    {
        segmentContainer.Height = Math.Max(1, lastBodyHeight);
        fallback.Alpha = 1;
        parts = [];

        if (slices.Length == 0 || lastBodyHeight <= 0)
        {
            syncSprites();
            return;
        }

        if (naturalHeights.Length != slices.Length)
            naturalHeights = new float[slices.Length];

        for (var i = 0; i < slices.Length; i++)
            naturalHeights[i] = displayHeightFor(slices[i]);

        fallback.Alpha = 0;
        parts = BmsLongNoteSegmentComposer.ComposeInto(naturalHeights, lastBodyHeight, tailAtTop, reusablePartSizes, reusableParts);
        syncSprites();
    }

    private float displayHeightFor(Texture texture)
    {
        var width = Math.Max(1, lastDrawWidth > 0 ? lastDrawWidth : DrawWidth);
        return Math.Max(1, texture.DisplayHeight * width / Math.Max(1, texture.DisplayWidth));
    }

    private void syncSprites()
    {
        while (spritePool.Count < parts.Count)
        {
            // Pool sprites because LN height changes every frame while scrolling. Creating/destroying
            // child drawables per frame would be far more expensive than hiding unused pooled sprites.
            var sprite = new Sprite
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.X,
                Width = 1,
                FillMode = FillMode.Stretch,
            };
            spritePool.Add(sprite);
            segmentContainer.Add(sprite);
        }

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
            sprite.Y = part.Y;
        }
    }
}
