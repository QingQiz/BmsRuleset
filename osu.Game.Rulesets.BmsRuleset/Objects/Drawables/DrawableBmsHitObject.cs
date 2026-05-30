using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Game.Rulesets.BmsRuleset.Audio;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Skinning;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image;
using RectangleF = osu.Framework.Graphics.Primitives.RectangleF;

namespace osu.Game.Rulesets.BmsRuleset.Objects.Drawables;

/// <summary>
///     Placeholder drawable for native BMS notes.
/// </summary>
/// <remarks>
///     This intentionally favours clarity over final gameplay fidelity. The object is rendered in a
///     lane determined by <see cref="BmsHitObject.Column" /> and scrolls by projected osu! time.
/// </remarks>
public sealed partial class DrawableBmsHitObject : DrawableHitObject<BmsHitObject>
{
    private const float default_note_height = 14;
    private const float max_long_note_piece_height = 4096;

    private Container noteContainer = null!;
    private SegmentedLongNoteBody longNoteBody = null!;
    private Container longNoteTailContainer = null!;
    private bool longNoteStarted;
    private BmsPlayfield? playfield;
    private float currentNoteHeight = default_note_height;
    private int skinnedColumn = -1;
    private BmsLayoutVariant? skinnedLayout;
    private BmsSkinComponents? skinnedComponent;

    [Resolved(CanBeNull = true)]
    private ISkinSource? skin { get; set; }

    public DrawableBmsHitObject()
        : base(null!)
    {
        Origin = Anchor.TopLeft;
        Size = new Vector2(40, default_note_height);
    }

    public bool TryHit()
    {
        if (Judged || HitObject?.HitWindows == null)
            return false;

        var bmsWindows = (BmsHitWindows)HitObject.HitWindows;
        var result = bmsWindows.BmsResultFor(Time.Current - HitObject.StartTime);

        if (result == HitResult.None)
            return false;

        if (HitObject.IsLongNote)
        {
            longNoteStarted = true;
            return true;
        }

        ApplyResult(result);
        return true;
    }

    public bool TryRelease()
    {
        if (Judged || HitObject?.HitWindows == null || !HitObject.IsLongNote || !longNoteStarted)
            return false;

        var bmsWindows = (BmsHitWindows)HitObject.HitWindows;
        var result = bmsWindows.BmsResultFor(Time.Current - HitObject.EndTime);

        if (result == HitResult.None)
            return false;

        ApplyResult(result);
        return true;
    }

    public override void PlaySamples()
    {
    }

    protected override void OnApply()
    {
        base.OnApply();

        Alpha = 1;
        longNoteStarted = false;
        playfield = null;
        skinnedColumn = -1;
        skinnedLayout = null;
        skinnedComponent = null;
        updateSkinPieces();
    }

    protected override void Update()
    {
        base.Update();

        // HitObject may not be set yet during early pool lifecycle; bail out.
        if (HitObject == null)
            return;

        // Lazily resolve the playfield on the first valid frame; stage is a shorthand reference.
        playfield ??= Parent?.FindClosestParent<BmsPlayfield>();
        var stage = playfield?.Stage;

        // Clamp the column index to avoid out-of-range access when the layout changes at runtime.
        var column = Math.Clamp(HitObject.Column, 0, playfield?.TotalColumns - 1 ?? 0);
        longNoteBody.SetSkinLookup(playfield?.LayoutVariant ?? BmsLayoutVariant.Bme7K, column);
        updateSkinPieces(playfield?.LayoutVariant, column);
        // Prefer the per-column HitObjectArea as the sizing reference; fall back to Parent if unavailable.
        var columnContainer = stage != null && column < stage.Columns.Length ? stage.Columns[column].HitObjectArea : Parent;
        var parentWidth = columnContainer?.DrawWidth ?? Parent?.DrawWidth ?? 0;
        var parentHeight = columnContainer?.DrawHeight ?? Parent?.DrawHeight ?? 0;

        // Skip positioning until the layout has finished its first valid measure pass.
        if (!float.IsFinite(parentWidth) || !float.IsFinite(parentHeight) || parentWidth <= 0 || parentHeight <= 0)
        {
            UpdateResult(false);
            return;
        }

        var scaledParentWidth = parentWidth;

        // Account for any scale transform on the column container when computing the note width.
        if (columnContainer != null && Parent != null)
        {
            var left = columnContainer.ToSpaceOfOtherDrawable(Vector2.Zero, Parent);
            var right = columnContainer.ToSpaceOfOtherDrawable(new Vector2(parentWidth, 0), Parent);
            scaledParentWidth = (right - left).Length;
        }

        currentNoteHeight = getCurrentNoteHeight(Math.Max(1, scaledParentWidth), playfield?.LayoutVariant ?? BmsLayoutVariant.Bme7K, column);

        // How far in time this note is from the current playback position.
        var timeUntilHit = HitObject.StartTime - Time.Current;
        var endTimeUntilHit = HitObject.EndTime - Time.Current;

        // NaN/Infinity can occur before timing is initialised; bail out safely.
        if (!double.IsFinite(timeUntilHit))
        {
            UpdateResult(false);
            return;
        }

        // timeRange is the visible time window (ms) that maps to the full travel distance on screen.
        var timeRange = playfield?.TimeRange ?? BmsDrawableRuleset.ComputeScrollTime(8);
        // hitTargetPosition is the Y offset (px) of the judgement line from the bottom of the column.
        var hitTargetPosition = stage?.HitTargetPosition ?? BmsStage.HIT_TARGET_POSITION;
        // travelDistance is the number of pixels from the top of the visible play area to the hit target.
        // Using the actual visible height (rather than a hardcoded constant) ensures notes spawn exactly
        // at the top of the screen when timeUntilHit == timeRange and arrive at the hit target when
        // timeUntilHit == 0, at the correct visual scroll speed.
        var travelDistance = Math.Max(1f, parentHeight - hitTargetPosition);
        // Notes are positioned by their bottom edge. At judgement time the bottom edge must sit
        // exactly on the visible judgement line; the drawable itself uses top-left coordinates.
        var y = parentHeight - hitTargetPosition - (float)(timeUntilHit / timeRange) * travelDistance - currentNoteHeight;
        var tailY = parentHeight - hitTargetPosition - (float)(endTimeUntilHit / timeRange) * travelDistance - currentNoteHeight;
        // Once an LN head has been pressed, clamp visualHeadY to the hit target so it doesn't scroll past it.
        var visualHeadY = HitObject.IsLongNote && longNoteStarted && Time.Current >= HitObject.StartTime
            ? Math.Min(y, parentHeight - hitTargetPosition - currentNoteHeight)
            : y;
        var visualTailY = HitObject.IsLongNote && longNoteStarted && Time.Current >= HitObject.EndTime
            ? Math.Min(tailY, parentHeight - hitTargetPosition - currentNoteHeight)
            : tailY;
        // Map the column-local Y into the parent drawable's coordinate space.
        var objectTop = HitObject.IsLongNote ? Math.Min(visualHeadY, visualTailY) : visualHeadY;
        var headOffset = visualHeadY - objectTop;
        var tailOffset = visualTailY - objectTop;
        var position = columnContainer != null && Parent != null
            ? columnContainer.ToSpaceOfOtherDrawable(new Vector2(0, objectTop), Parent)
            : new Vector2(0, objectTop);

        // A non-finite mapped position means the coordinate transform is not ready yet.
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
        {
            UpdateResult(false);
            return;
        }

        Position = position;

        // Enforce a 1 px minimum in both dimensions to avoid zero-size drawables.
        var objectHeight = HitObject.IsLongNote
            ? Math.Max(currentNoteHeight, Math.Abs(visualTailY - visualHeadY) + currentNoteHeight)
            : currentNoteHeight;

        Size = new Vector2(Math.Max(1, scaledParentWidth), objectHeight);
        // Reposition the LN body and tail pieces to match the updated head/tail Y values.
        updateLongNotePieces(headOffset, tailOffset);

        // The execution of the Update function is frame-by-frame.
        // Therefore, when the execution finds that the current time is
        // greater than the trigger time of the hitobject,
        // it means it is being triggered in the current or next frame.
        // At this point, the mine is processed:
        // if it is held down, trigger the mine; otherwise, let it expire immediately.
        if (HitObject.IsMine && !Judged && Time.Current >= HitObject.StartTime)
        {
            if (playfield?.IsColumnPressedForLandmine(HitObject.Column) == true)
            {
                playfield.DetonateLandmine(HitObject);
                ApplyResult(HitResult.Meh);
            }
            else
                Expire(); // column was not held — mine passes silently

            return;
        }

        // Mark LN as started so visualHeadY clamping kicks in from the next frame.
        if (playfield?.IsAutoplay == true && HitObject.IsLongNote && Time.Current >= HitObject.StartTime)
            longNoteStarted = true;

        // Autoplay: hide and auto-judge the note at its hit time (tail time for LNs, start time for normals).
        if (playfield?.IsAutoplay == true && !Judged && Time.Current >= (HitObject.IsLongNote ? HitObject.EndTime : HitObject.StartTime))
        {
            Alpha = 0;
            ApplyMaxResult();
            return;
        }

        // Passive miss check: called every frame so CheckForResult can apply a miss once the hit window closes.
        UpdateResult(false);
    }

    protected override void CheckForResult(bool userTriggered, double timeOffset)
    {
        if (userTriggered || HitObject.HitWindows == null || HitObject.IsMine)
            return;

        var missWindow = HitObject.HitWindows.WindowFor(HitResult.Ok); // BAD is the passive-miss boundary

        if (HitObject.IsLongNote)
        {
            // LN head never pressed: passive POOR once the head BAD window is exhausted.
            if (!longNoteStarted && Time.Current > HitObject.StartTime + missWindow)
            {
                ApplyResult(HitResult.Meh);
                return;
            }

            // LN held but player never released before the tail BAD window expired:
            // this is a "drop" — scores POOR (Meh) in BMS.
            if (longNoteStarted && Time.Current > HitObject.EndTime + missWindow)
            {
                ApplyResult(HitResult.Meh);
                // ReSharper disable once RedundantJumpStatement
                return;
            }

            // For an in-progress LN the framework-supplied timeOffset is relative to
            // StartTime; do not apply the generic miss check below until the tail window.
            return;
        }

        // Normal note: passive POOR (Meh) once the BAD window is passed with no keypress.
        if (timeOffset > missWindow)
            ApplyResult(HitResult.Meh);
    }

    protected override void UpdateInitialTransforms()
    {
        base.UpdateInitialTransforms();
        this.FadeInFromZero(100);
    }

    protected override void UpdateHitStateTransforms(ArmedState state)
    {
        base.UpdateHitStateTransforms(state);

        switch (state)
        {
            case ArmedState.Hit:
                this.FadeOut();
                LifetimeEnd = Math.Max(HitObject.EndTime, HitStateUpdateTime) + 100;
                break;

            case ArmedState.Miss:
                this.FadeColour(Color4.Red, 80).FadeOut(220).Expire();
                break;
        }
    }

    protected override JudgementResult CreateResult(Judgement judgement) => new(HitObject, judgement);

    protected override void LoadSamples()
    {
        if (!string.IsNullOrEmpty(HitObject.SamplePath))
            Samples.Samples = [new BmsSampleInfo(HitObject.SamplePath)];
        else
            base.LoadSamples();
    }

    private static string fallbackColumnIndex(BmsSkinComponentLookup lookup)
    {
        if (lookup.IsScratch)
            return "S";

        var maniaColumnsPerStage = lookup.LayoutVariant switch
        {
            BmsLayoutVariant.Bms5KDouble => 5,
            BmsLayoutVariant.Bme7KDouble => 7,
            BmsLayoutVariant.Pms9KDouble => 9,
            _ => lookup.ManiaKeyCount,
        };
        var columnInStage = Math.Clamp(lookup.ManiaColumnIndex ?? 0, 0, Math.Max(0, maniaColumnsPerStage - 1)) % maniaColumnsPerStage;
        var distanceToEdge = Math.Min(columnInStage, maniaColumnsPerStage - 1 - columnInStage);
        return distanceToEdge % 2 == 0 ? "1" : "2";
    }

    private float getCurrentNoteHeight(float drawWidth, BmsLayoutVariant layoutVariant, int column)
    {
        var component = HitObject.IsMine ? BmsSkinComponents.Mine : HitObject.IsLongNote ? BmsSkinComponents.HoldNoteHead : BmsSkinComponents.Note;
        var lookup = new BmsSkinComponentLookup(component, layoutVariant, column);
        var widthForNoteHeightScale = skin?.GetConfig<BmsSkinConfigurationLookup, float>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.WidthForNoteHeightScale, lookup))?.Value;
        var heightScaleReference = widthForNoteHeightScale ?? drawWidth;

        var textureHeight = getTextureHeightForScaleReference(heightScaleReference, lookup);

        if (textureHeight != null)
            return textureHeight.Value;

        return widthForNoteHeightScale != null ? Math.Max(1, heightScaleReference) : default_note_height;
    }

    private float? getTextureHeightForScaleReference(float heightScaleReference, BmsSkinComponentLookup lookup)
    {
        if (skin == null)
            return null;

        foreach (var name in getNoteImageCandidates(lookup).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())
        {
            var texture = skin.GetTextures(name!, WrapMode.ClampToEdge, WrapMode.ClampToEdge, true, "-", null, out _)
                .FirstOrDefault(t => t.DisplayWidth > 0 && t.DisplayHeight > 0);

            if (texture != null)
                return Math.Max(1, texture.DisplayHeight * heightScaleReference / texture.DisplayWidth);
        }

        return null;
    }

    private IEnumerable<string?> getNoteImageCandidates(BmsSkinComponentLookup lookup)
    {
        var fallback = fallbackColumnIndex(lookup);

        switch (lookup.Component)
        {
            case BmsSkinComponents.Mine:
                yield return skin?.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.Hit100, lookup))?.Value;
                yield return "mania-noteS";

                break;

            case BmsSkinComponents.HoldNoteHead:
                yield return skin?.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.HoldNoteHeadImage, lookup))?.Value;
                yield return $"mania-note{fallback}H";
                yield return skin?.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value;
                yield return $"mania-note{fallback}";

                break;

            default:
                yield return skin?.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value;
                yield return $"mania-note{fallback}";

                break;
        }
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        AddRangeInternal([
            longNoteBody = new SegmentedLongNoteBody
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.X,
                Alpha = 0,
            },
            longNoteTailContainer = new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.X,
                Height = default_note_height,
                Alpha = 0,
            },
            noteContainer = new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.X,
                Height = default_note_height,
            },
        ]);
    }

    private void updateLongNotePieces(float headOffset, float tailOffset)
    {
        if (!HitObject.IsLongNote)
        {
            noteContainer.Y = 0;
            noteContainer.Height = currentNoteHeight;
            longNoteBody.Alpha = 0;
            longNoteTailContainer.Alpha = 0;
            return;
        }

        noteContainer.Y = headOffset;
        noteContainer.Height = currentNoteHeight;

        // The body is allowed to render underneath the caps. Classic body[0] includes the tail-side
        // rounded end, so clipping at cap edges flattens that end before the cap can overlay it.
        var tailAtTop = tailOffset < headOffset;
        var bodyTop = Math.Min(headOffset, tailOffset);
        var bodyBottom = Math.Max(headOffset, tailOffset) + currentNoteHeight;

        // Clamp to within one screen-length of the head so that super-long BMS LNs
        // (where the tail is thousands of pixels away) don't produce enormous geometry.
        var visibleTop = Math.Max(bodyTop, headOffset - max_long_note_piece_height);
        var visibleBottom = Math.Min(bodyBottom, headOffset + max_long_note_piece_height);
        var bodyHeight = Math.Max(0, visibleBottom - visibleTop);

        longNoteBody.Y = visibleTop;
        longNoteBody.Height = Math.Max(1, bodyHeight);
        longNoteBody.UpdateBody(bodyHeight, tailAtTop, longNoteStarted);
        longNoteBody.Alpha = bodyHeight > 0 ? 1 : 0;
        longNoteTailContainer.Y = tailOffset;
        longNoteTailContainer.Height = currentNoteHeight;
        longNoteTailContainer.Alpha = 1;
    }

    private void updateSkinPieces(BmsLayoutVariant? layoutVariant = null, int? column = null)
    {
        if (HitObject == null)
            return;

        var resolvedLayoutVariant = layoutVariant ?? Parent?.FindClosestParent<BmsPlayfield>()?.LayoutVariant ?? BmsLayoutVariant.Bme7K;
        var resolvedColumn = column ?? HitObject.Column;
        var component = HitObject.IsMine ? BmsSkinComponents.Mine : HitObject.IsLongNote ? BmsSkinComponents.HoldNoteHead : BmsSkinComponents.Note;

        if (skinnedColumn == resolvedColumn && skinnedLayout == resolvedLayoutVariant && skinnedComponent == component)
            return;

        skinnedColumn = resolvedColumn;
        skinnedLayout = resolvedLayoutVariant;
        skinnedComponent = component;

        longNoteBody.BodyColour = Color4.Cyan;
        longNoteBody.Alpha = HitObject.IsLongNote ? 0.65f : 0;
        longNoteTailContainer.Alpha = HitObject.IsLongNote ? 1 : 0;

        noteContainer.Clear();
        longNoteTailContainer.Clear();

        noteContainer.Add(new SkinnableDrawable(new BmsSkinComponentLookup(component, resolvedLayoutVariant, resolvedColumn), _ => new DefaultBmsNotePiece(defaultColourFor(component)))
        {
            RelativeSizeAxes = Axes.Both,
            // Legacy BMS note pieces are normalised to top-left anchoring. Do not centre them,
            // otherwise judgement alignment shifts by roughly one cap height.
            CentreComponent = false,
        });

        if (!HitObject.IsLongNote)
            return;

        longNoteTailContainer.Add(new SkinnableDrawable(new BmsSkinComponentLookup(BmsSkinComponents.HoldNoteTail, resolvedLayoutVariant, resolvedColumn), _ => Empty())
        {
            RelativeSizeAxes = Axes.Both,
            // See noteContainer above: cap geometry uses top-left coordinates.
            CentreComponent = false,
        });

        static Color4 defaultColourFor(BmsSkinComponents component) => component switch
        {
            BmsSkinComponents.Mine => Color4.OrangeRed,
            BmsSkinComponents.HoldNoteHead => Color4.Cyan,
            _ => Color4.White,
        };
    }

    private sealed partial class DefaultBmsNotePiece : CompositeDrawable
    {
        public DefaultBmsNotePiece(Color4 colour)
        {
            RelativeSizeAxes = Axes.Both;

            InternalChild = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = colour,
            };
        }
    }

    private sealed partial class SegmentedLongNoteBody : CompositeDrawable
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
        private static readonly FieldInfo? skin_store_field = typeof(Skin).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Dictionary<RawSliceCacheKey, Texture[]> raw_slice_cache = new();
        private static readonly object raw_slice_cache_lock = new();
        private readonly List<(Texture texture, float height)> parts = [];
        private readonly List<Sprite> spritePool = [];

        // Animation frames are time-varying images (for example classic mania-note1L-0..5).
        // They are not spatial segments. We choose one frame, then split that frame spatially if needed.
        private Texture[] bodyFrames = [];

        // Spatial slices are top-to-bottom body pieces. For ultra-tall raw resources these are decoded
        // before texture upload so the GPU max-size limiter never flattens the source image first.
        private Texture[] slices = [];
        private float[] naturalHeights = [];
        private float lastBodyHeight = -1;
        private float lastDrawWidth = -1;
        private int currentFrameIndex;
        private bool tailAtTop;
        private int? column;
        private BmsLayoutVariant layoutVariant;

        [Resolved(CanBeNull = true)]
        private ISkinSource? skin { get; set; }

        [Resolved]
        private IRenderer renderer { get; set; } = null!;

        public SegmentedLongNoteBody()
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

        public void UpdateBody(float bodyHeight, bool newTailAtTop, bool isHolding)
        {
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

            if (bodyFrames.Length > 0)
                updateSlicesFromCurrentFrame();

            rebuildSegments();
        }

        public void SetSkinLookup(BmsLayoutVariant newLayoutVariant, int newColumn)
        {
            if (column == newColumn && layoutVariant == newLayoutVariant)
                return;

            column = newColumn;
            layoutVariant = newLayoutVariant;
            loadSlices();
            rebuildSegments();
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (skin != null)
                skin.SourceChanged += onSkinChanged;

            loadSlices();
            rebuildSegments();
        }

        private static IResourceStore<byte[]>? rawStoreFor(ISkin? provider)
        {
            return provider switch
            {
                BmsEmbeddedSkin embedded => embedded.Resources,
                Skin concreteSkin when skin_store_field?.GetValue(concreteSkin) is IResourceStore<byte[]> store => store,
                _ => null,
            };
        }

        private static ISkin unwrap(ISkin provider)
        {
            while (provider is ISkinTransformer transformer)
                provider = transformer.Skin;

            return provider;
        }

        private static IEnumerable<string> textureStreamCandidates(string candidate)
        {
            // ReSharper disable once InconsistentNaming
            var without2x = candidate.Replace("@2x", string.Empty);
            var extension = Path.GetExtension(without2x);
            var baseName = Path.ChangeExtension(without2x, null);

            if (string.IsNullOrEmpty(extension))
            {
                yield return $"{baseName}@2x.png";
                yield return $"{baseName}.png";
            }
            else
            {
                yield return $"{baseName}@2x{extension}";
                yield return without2x;
            }
        }

        private void onSkinChanged()
        {
            lock (raw_slice_cache_lock)
                raw_slice_cache.Clear();

            loadSlices();
            rebuildSegments();
        }

        private void loadSlices()
        {
            bodyFrames = [];
            slices = [];
            naturalHeights = [];
            currentFrameIndex = 0;
            segmentContainer.Clear(disposeChildren: true);
            spritePool.Clear();

            if (skin == null || column == null)
                return;

            var lookup = new BmsSkinComponentLookup(BmsSkinComponents.HoldNoteBody, layoutVariant, column.Value);
            var textures = findBodyTextures(lookup);

            if (textures == null)
                return;

            if (textures.Value.AreSpatialSlices)
            {
                slices = textures.Value.Textures;
                naturalHeights = slices.Select(displayHeightFor).ToArray();
            }
            else
            {
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

        private float displayHeightFor(Texture texture)
        {
            var width = Math.Max(1, lastDrawWidth > 0 ? lastDrawWidth : DrawWidth);
            return Math.Max(1, texture.DisplayHeight * width / Math.Max(1, texture.DisplayWidth));
        }

        private (Texture[] Textures, bool AreSpatialSlices)? findBodyTextures(BmsSkinComponentLookup lookup)
        {
            if (skin == null)
                return null;

            var fallback = fallbackColumnIndex(lookup);
            var configuredBody = skin.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.HoldNoteBodyImage, lookup))?.Value;
            var configuredNote = skin.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value;
            var bodyIsShortNoteFallback = !string.IsNullOrWhiteSpace(configuredBody) && configuredBody == configuredNote;
            var candidates = new[]
            {
                bodyIsShortNoteFallback ? null : configuredBody,
                $"mania-note{fallback}L",
                configuredNote,
                $"mania-note{fallback}",
            };

            foreach (var candidate in candidates.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct())
            {
                var rawSlices = getRawBodySlices(candidate!);

                if (rawSlices.Length > 0)
                    return (rawSlices, true);

                var textures = skin.GetTextures(candidate!, WrapMode.ClampToEdge, WrapMode.ClampToEdge, true, "-", null, out _)
                    .Where(t => t.DisplayWidth > 0 && t.DisplayHeight > 0)
                    .ToArray();

                if (textures.Length > 0)
                    return (textures, false);
            }

            return null;
        }

        private Texture[] getRawBodySlices(string candidate)
        {
            if (skin == null)
                return [];

            var rawResource = findRawTextureResource(candidate);

            if (rawResource == null)
                return [];

            var key = new RawSliceCacheKey(rawResource.Value.Provider, renderer, rawResource.Value.ResourceName);

            lock (raw_slice_cache_lock)
            {
                if (raw_slice_cache.TryGetValue(key, out var cached))
                    return cached;
            }

            var decoded = decodeRawBodySlices(rawResource.Value.Store, rawResource.Value.ResourceName);

            lock (raw_slice_cache_lock)
                raw_slice_cache[key] = decoded;

            return decoded;
        }

        private Texture[] decodeRawBodySlices(IResourceStore<byte[]> store, string resourceName)
        {
            using var stream = store.GetStream(resourceName);

            if (stream == null)
                return [];

            using var image = Image.Load<Rgba32>(stream);

            if (image.Width <= 0 || image.Height <= 0)
                return [];

            if (image.Height <= max_source_slice_height)
                return [];

            var count = Math.Max(1, (int)Math.Ceiling(image.Height / max_source_slice_height));
            var result = new Texture[count];

            for (var i = 0; i < count; i++)
            {
                var y = image.Height * i / count;
                var nextY = image.Height * (i + 1) / count;
                // Clone only the current slice into a fresh image before creating TextureUpload.
                // This is the important part: the 39906px Argon body, and similar user skins,
                // never enter MaxDimensionLimitedTextureLoaderStore as one huge texture.
                using var slice = image.Clone(ctx => ctx.Crop(new Rectangle(0, y, image.Width, nextY - y)));
                var upload = new TextureUpload(slice.Clone());
                var texture = renderer.CreateTexture(upload.Width, upload.Height, wrapModeS: WrapMode.ClampToEdge, wrapModeT: WrapMode.ClampToEdge);
                texture.SetData(upload);
                result[i] = texture;
            }

            return result;
        }

        private (ISkin Provider, IResourceStore<byte[]> Store, string ResourceName)? findRawTextureResource(string candidate)
        {
            foreach (var provider in rawProviders())
            {
                if (rawStoreFor(provider) is not { } store)
                    continue;

                foreach (var resourceName in textureStreamCandidates(candidate))
                {
                    using var stream = store.GetStream(resourceName);

                    if (stream != null)
                        return (provider, store, resourceName);
                }
            }

            return null;

            IEnumerable<ISkin> rawProviders()
            {
                if (skin is ISkinSource source)
                {
                    foreach (var provider in source.AllSources)
                        yield return unwrap(provider);
                }
                else if (skin != null)
                    yield return unwrap(skin);
            }
        }

        private void rebuildSegments()
        {
            segmentContainer.Height = Math.Max(1, lastBodyHeight);
            fallback.Alpha = 1;
            parts.Clear();

            if (slices.Length == 0 || lastBodyHeight <= 0)
            {
                syncSprites();
                return;
            }

            naturalHeights = slices.Select(displayHeightFor).ToArray();
            fallback.Alpha = 0;

            if (slices.Length == 1)
                addRepeatedSingleSlice(lastBodyHeight);
            else
                addMultiSliceBody(lastBodyHeight);

            syncSprites();
        }

        private void addRepeatedSingleSlice(float targetHeight)
        {
            var height = Math.Max(1, naturalHeights[0]);
            var totalHeight = 0f;

            while (totalHeight < targetHeight)
            {
                parts.Add((slices[0], height));
                totalHeight += height;
            }
        }

        private void addMultiSliceBody(float targetHeight)
        {
            var tailHeight = naturalHeights[0];
            var covered = tailHeight;

            parts.Add((slices[0], tailHeight));

            for (var i = 1; i < slices.Length && covered < targetHeight; i++)
            {
                parts.Add((slices[i], naturalHeights[i]));
                covered += naturalHeights[i];
            }

            while (covered < targetHeight)
            {
                var coverBefore = covered;

                for (var i = 1; i < slices.Length && covered < targetHeight; i++)
                {
                    parts.Add((slices[i], naturalHeights[i]));
                    covered += naturalHeights[i];
                }

                if (covered <= coverBefore)
                    break; // infinite-loop guard: all body slices have zero height
            }
        }

        private void syncSprites()
        {
            // Grow the pool as needed; new sprites are added to the container once and reused thereafter
            while (spritePool.Count < parts.Count)
            {
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

            // Hide pool entries beyond the current parts list
            for (var i = parts.Count; i < spritePool.Count; i++)
                spritePool[i].Alpha = 0;

            if (parts.Count == 0)
                return;

            // Position each part. parts[0] is the tail piece.
            // tailAtTop=true  → parts[0] at y=0, subsequent parts below it
            // tailAtTop=false → parts[0] at the bottom, parts built upward
            var targetHeight = segmentContainer.Height;
            var prefixSum = 0f;

            for (var i = 0; i < parts.Count; i++)
            {
                var sprite = spritePool[i];
                var height = parts[i].height;
                var y = tailAtTop ? prefixSum : targetHeight - prefixSum - height;
                sprite.Alpha = 1;
                sprite.Texture = parts[i].texture;
                sprite.Height = height;
                sprite.Scale = new Vector2(1, !tailAtTop && i == 0 ? -1 : 1);
                sprite.Y = !tailAtTop && i == 0 ? y + height : y;
                prefixSum += height;
            }
        }

        private sealed class RawSliceCacheKey(ISkin provider, IRenderer renderer, string resourceName)
            : IEquatable<RawSliceCacheKey>
        {
            private readonly ISkin provider = provider;
            private readonly IRenderer renderer = renderer;
            private readonly string resourceName = resourceName;

            public bool Equals(RawSliceCacheKey? other) => other != null
                                                           && ReferenceEquals(provider, other.provider)
                                                           && ReferenceEquals(renderer, other.renderer)
                                                           && resourceName == other.resourceName;

            public override bool Equals(object? obj) => obj is RawSliceCacheKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(provider), RuntimeHelpers.GetHashCode(renderer), resourceName);
        }
    }
}
