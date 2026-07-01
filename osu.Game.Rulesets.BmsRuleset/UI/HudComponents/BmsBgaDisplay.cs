using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Rulesets.BmsRuleset.Audio;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents.Bga;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

public sealed partial class BmsBgaDisplay : CompositeDrawable, ISerialisableDrawable
{
    private static readonly string[] video_resource_extensions =
    [
        ".mp4",
        ".webm",
        ".mov",
        ".mpeg",
        ".mpg",
        ".avi",
        ".wmv",
    ];

    private static readonly string[] image_resource_extensions =
    [
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
    ];

    private static readonly HashSet<string> video_extensions = new(video_resource_extensions, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> image_extensions = new(image_resource_extensions, StringComparer.OrdinalIgnoreCase);

    public bool UsesFixedAnchor { get; set; }

    public bool AutoSizeToParent { get; init; }

    // Init-only and not a [SettingSource], so saved skin layouts keep using the rehost path.
    // Otherwise deserialised BGAs would stay in the HUD overlay and render above the playfield.
    public bool RenderOutsideHudVisibility { get; init; } = true;

    private readonly Container layers;
    private readonly Dictionary<BmsBgaLayer, BmsBgaEvent> activeEvents = new();
    private readonly Dictionary<BmsBgaLayer, BmsBgaOpacityEvent> activeOpacityEvents = new();
    private readonly Dictionary<BmsBgaLayer, Container> layerHosts = new();

    private BmsBgaTimeline? bga;
    private BmsBgaResourceStore? resources;
    private TextureStore? textures;
    private BmsBgaEvent[] events = [];
    private BmsBgaOpacityEvent[] opacityEvents = [];
    private int nextEventIndex;
    private int nextOpacityEventIndex;
    private double lastTime = double.NegativeInfinity;
    private double poorLayerUntil = double.NegativeInfinity;
    private bool poorLayerVisible;
    private IBmsGameplayEvents? gameplayEvents;
    private BmsBgaDisplay? rehostedDisplay;
    private Container? rehostedDisplayHost;
    private IBindable<double>? bgaDim;
    private readonly bool isRehostedDisplay;

    [Resolved(CanBeNull = true)]
    private DrawableRuleset? drawableRuleset { get; set; }

    [Resolved(CanBeNull = true)]
    private IBindable<WorkingBeatmap>? workingBeatmap { get; set; }

    public BmsBgaDisplay()
        : this(false)
    {
    }

    private BmsBgaDisplay(bool isRehostedDisplay)
    {
        this.isRehostedDisplay = isRehostedDisplay;

        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        Size = new Vector2(640, 480);
        Masking = true;

        InternalChild = layers = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children =
            [
                createLayerHost(BmsBgaLayer.Base),
                createLayerHost(BmsBgaLayer.Layer1),
                createLayerHost(BmsBgaLayer.Layer2),
                createLayerHost(BmsBgaLayer.Poor),
            ],
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        bindBgaDim();
        applyAutoSizeToParent();

        if (tryAddPlayfieldDisplay())
            return;

        gameplayEvents = (drawableRuleset as BmsDrawableRuleset)?.GameplayEvents;
        if (gameplayEvents != null)
            gameplayEvents.JudgementDisplayed += onJudgementDisplayed;
    }

    protected override void Update()
    {
        base.Update();

        if (rehostedDisplay != null)
        {
            syncRehostedDisplay();
            return;
        }

        if (bga == null || drawableRuleset == null)
            return;

        var time = drawableRuleset.FrameStableClock.CurrentTime;

        if (time < lastTime)
        {
            activeEvents.Clear();
            activeOpacityEvents.Clear();
            nextEventIndex = 0;
            nextOpacityEventIndex = 0;
            poorLayerVisible = false;
            poorLayerUntil = double.NegativeInfinity;
            clearLayerHosts();
        }

        lastTime = time;

        if (poorLayerVisible && time > poorLayerUntil)
        {
            poorLayerVisible = false;
            applyLayerVisibility();
        }

        while (nextOpacityEventIndex < opacityEvents.Length && opacityEvents[nextOpacityEventIndex].Time <= time)
        {
            var evt = opacityEvents[nextOpacityEventIndex++];
            activeOpacityEvents[evt.Layer] = evt;
            applyLayerOpacity(evt.Layer);
        }

        while (nextEventIndex < events.Length && events[nextEventIndex].Time <= time)
        {
            var evt = events[nextEventIndex++];
            activeEvents[evt.Layer] = evt;
            setLayerDrawable(evt);
        }
    }

    protected override void Dispose(bool isDisposing)
    {
        textures?.Dispose();
        resources?.Dispose();
        rehostedDisplayHost?.RemoveAndDisposeImmediately();
        rehostedDisplayHost = null;
        rehostedDisplay = null;

        if (gameplayEvents != null)
            gameplayEvents.JudgementDisplayed -= onJudgementDisplayed;

        base.Dispose(isDisposing);
    }

    [BackgroundDependencyLoader]
    private void load(GameHost host)
    {
        bindBgaDim();

        if (drawableRuleset is not BmsDrawableRuleset bmsDrawableRuleset || bmsDrawableRuleset.Beatmap is not BmsBeatmap bmsBeatmap)
        {
            return;
        }

        bga = bmsBeatmap.Bga;
        events = bga.Events.OrderBy(e => e.Time).ThenBy(e => e.Sequence).ToArray();
        opacityEvents = bga.OpacityEvents.OrderBy(e => e.Time).ThenBy(e => e.Sequence).ToArray();

        if (bga.BitmapDefinitions.Count == 0)
        {
            return;
        }

        resources = new BmsBgaResourceStore(bmsDrawableRuleset.BeatmapSourceDirectory, workingBeatmap);
        textures = new TextureStore(host.Renderer, host.CreateTextureLoaderStore(resources), false, scaleAdjust: 1);

    }

    private void bindBgaDim()
    {
        if (bgaDim != null || drawableRuleset is not BmsDrawableRuleset bmsDrawableRuleset)
            return;

        bgaDim = bmsDrawableRuleset.BgaDim.GetBoundCopy();
        bgaDim.BindValueChanged(e => applyBgaDim(e.NewValue), true);
    }

    private void applyAutoSizeToParent()
    {
        // The HUD default (AutoSizeToParent) fills the parent so the rehost host — sized to the
        // parent quad — defines the BGA window; content letterboxes transparently via FillMode.Fit.
        // The non-auto path keeps an explicit skin-edited size.
        if (AutoSizeToParent)
        {
            RelativeSizeAxes = Axes.Both;
            Size = Vector2.One;
        }
    }

    private void applyBgaDim(double dim)
    {
        if (rehostedDisplay != null)
        {
            rehostedDisplay.applyBgaDim(dim);
            layers.Alpha = 0;
            return;
        }

        var clampedDim = Math.Clamp(dim, 0, 1);
        var brightness = (float)(1 - clampedDim);
        layers.Colour = OsuColour.Gray(brightness);
        layers.Alpha = clampedDim < 1 ? 1 : 0;
        layers.AlwaysPresent = false;
    }

    private bool tryAddPlayfieldDisplay()
    {
        var hudOverlay = this.FindClosestParent<HUDOverlay>();
        var playfield = drawableRuleset?.Playfield as BmsPlayfield;

        if (!RenderOutsideHudVisibility || isRehostedDisplay || hudOverlay is null || playfield is null)
            return false;

        rehostedDisplay = new BmsBgaDisplay(true)
        {
            AutoSizeToParent = AutoSizeToParent,
            Anchor = Anchor,
            Origin = Origin,
            Position = Position,
            Scale = Scale,
            Rotation = Rotation,
            Size = Size,
            Depth = float.MaxValue,
            RenderOutsideHudVisibility = false,
        };
        if (bgaDim != null)
            rehostedDisplay.applyBgaDim(bgaDim.Value);
        rehostedDisplayHost = new Container
        {
            Depth = float.MaxValue,
            Child = rehostedDisplay,
        };
        playfield.AddBehindStage(rehostedDisplayHost);

        layers.Alpha = 0;
        AlwaysPresent = true;
        syncRehostedDisplay();
        return true;
    }

    private void syncRehostedDisplay()
    {
        if (rehostedDisplay == null || rehostedDisplayHost == null || drawableRuleset?.Playfield is not BmsPlayfield playfield || Parent == null)
            return;

        var parentQuad = Parent.ScreenSpaceDrawQuad;
        var topLeft = playfield.ToLocalSpace(parentQuad.TopLeft);
        var topRight = playfield.ToLocalSpace(parentQuad.TopRight);
        var bottomLeft = playfield.ToLocalSpace(parentQuad.BottomLeft);
        var xAxis = topRight - topLeft;
        var yAxis = bottomLeft - topLeft;

        rehostedDisplayHost.Position = topLeft;
        rehostedDisplayHost.Size = new Vector2(xAxis.Length, yAxis.Length);
        rehostedDisplayHost.Rotation = MathHelper.RadiansToDegrees(MathF.Atan2(xAxis.Y, xAxis.X));

        syncRehostedDisplayState(this, rehostedDisplay);
    }

    private static void syncRehostedDisplayState(BmsBgaDisplay source, BmsBgaDisplay target)
    {
        target.Anchor = source.Anchor;
        target.Origin = source.Origin;
        target.Position = source.Position;
        target.Scale = source.Scale;
        target.Rotation = source.Rotation;
        target.Size = source.Size;
    }

    private Container createLayerHost(BmsBgaLayer layer)
    {
        var host = new Container
        {
            RelativeSizeAxes = Axes.Both,
        };
        layerHosts[layer] = host;
        return host;
    }

    private void clearLayerHosts()
    {
        foreach (var host in layerHosts.Values)
            host.Clear();
    }

    private void setLayerDrawable(BmsBgaEvent evt)
    {
        if (!layerHosts.TryGetValue(evt.Layer, out var host))
        {
            return;
        }

        var drawable = createDrawable(evt);
        host.Clear();

        if (drawable != null)
            host.Add(drawable);

        applyLayerOpacity(evt.Layer);
        applyLayerVisibility();
    }

    private void applyLayerOpacity(BmsBgaLayer layer)
    {
        if (layer == BmsBgaLayer.Poor || (poorLayerVisible && bga?.PoorMode == BmsPoorBgaMode.Replace))
        {
            applyLayerVisibility();
            return;
        }

        if (layerHosts.TryGetValue(layer, out var host))
            host.Alpha = opacityFor(layer);
    }

    private void applyLayerVisibility()
    {
        if (!layerHosts.TryGetValue(BmsBgaLayer.Poor, out var poor))
            return;

        var showPoor = bga?.PoorMode != BmsPoorBgaMode.Off
                       && poorLayerVisible
                       && activeEvents.ContainsKey(BmsBgaLayer.Poor);

        poor.Alpha = showPoor
            ? opacityFor(BmsBgaLayer.Poor)
            : 0;

        var showNormal = !showPoor || bga?.PoorMode == BmsPoorBgaMode.Add;
        if (layerHosts.TryGetValue(BmsBgaLayer.Base, out var baseLayer))
            baseLayer.Alpha = showNormal ? opacityFor(BmsBgaLayer.Base) : 0;
        if (layerHosts.TryGetValue(BmsBgaLayer.Layer1, out var layer1))
            layer1.Alpha = showNormal ? opacityFor(BmsBgaLayer.Layer1) : 0;
        if (layerHosts.TryGetValue(BmsBgaLayer.Layer2, out var layer2))
            layer2.Alpha = showNormal ? opacityFor(BmsBgaLayer.Layer2) : 0;
    }

    private float opacityFor(BmsBgaLayer layer) =>
        activeOpacityEvents.TryGetValue(layer, out var evt) ? evt.Opacity : 1;

    private Drawable? createDrawable(BmsBgaEvent evt)
    {
        if (bga == null || textures == null || resources == null)
        {
            return null;
        }

        var bitmapKey = evt.DefinitionKey;
        BmsBgaDefinition? definition = null;

        if (bga.BgaDefinitions.TryGetValue(evt.DefinitionKey, out var defined))
        {
            bitmapKey = defined.BitmapKey;
            definition = defined;
        }

        if (!bga.BitmapDefinitions.TryGetValue(bitmapKey, out var path) || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var resolvedPath = resources.ResolveLookup(path);
        if (resolvedPath == null)
        {
            return null;
        }

        if (isVideo(resolvedPath))
        {
            return createVideo(resolvedPath, evt.Time);
        }

        var texture = textures.Get(resolvedPath);
        if (texture == null)
        {
            return null;
        }

        return definition is { } d
            ? createCroppedSprite(texture, d)
            : createFullCanvasSprite(texture);
    }

    private static Drawable createFullCanvasSprite(Texture texture) => new Sprite
    {
        Anchor = Anchor.Centre,
        Origin = Anchor.Centre,
        RelativeSizeAxes = Axes.Both,
        // Fit (letterbox) preserves aspect ratio; Stretch distorts non-square BGA (beatoraja BGAEXPAND=1 default).
        FillMode = FillMode.Fit,
        Texture = texture,
    };

    private static Drawable createCroppedSprite(Texture texture, BmsBgaDefinition definition)
    {
        var cropped = texture.Crop(
            new RectangleF(definition.SourceX, definition.SourceY, definition.SourceWidth, definition.SourceHeight),
            wrapModeS: WrapMode.ClampToEdge,
            wrapModeT: WrapMode.ClampToEdge);

        return new Sprite
        {
            Texture = cropped,
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = new Vector2(definition.DestinationX, definition.DestinationY),
            Size = new Vector2(definition.SourceWidth, definition.SourceHeight),
        };
    }

    private Drawable? createVideo(string path, double eventStartTime)
    {
        var stream = resources?.GetStream(path);
        if (stream == null)
            return null;

        return BmsBgaVideoFactory.Create(path, stream, drawableRuleset?.FrameStableClock ?? Clock, eventStartTime);
    }

    private static bool isVideo(string path) => video_extensions.Contains(Path.GetExtension(path));

    private void onJudgementDisplayed(HitResult result)
    {
        if (result != HitResult.Miss || drawableRuleset == null || bga == null || bga.PoorMode == BmsPoorBgaMode.Off)
            return;

        if (!activeEvents.ContainsKey(BmsBgaLayer.Poor)
            && bga.BitmapDefinitions.ContainsKey(0))
        {
            var time = drawableRuleset.FrameStableClock.CurrentTime;
            var sequence = activeEvents.Values.Select(e => e.Sequence).DefaultIfEmpty(0).Max() + 1;
            var evt = new BmsBgaEvent(time, 0, 0, BmsBgaLayer.Poor, sequence);
            activeEvents[BmsBgaLayer.Poor] = evt;
            setLayerDrawable(evt);
        }

        if (!activeEvents.ContainsKey(BmsBgaLayer.Poor))
            return;

        poorLayerUntil = drawableRuleset.FrameStableClock.CurrentTime + 500;
        poorLayerVisible = true;
        applyLayerVisibility();
    }

    private sealed class BmsBgaResourceStore(string? sourceDirectory, IBindable<WorkingBeatmap>? workingBeatmap) : IResourceStore<byte[]>
    {
        private readonly BmsFileResourceStore? externalStore = !string.IsNullOrWhiteSpace(sourceDirectory) && Directory.Exists(sourceDirectory)
            ? new BmsFileResourceStore(sourceDirectory)
            : null;

        public byte[] Get(string? name)
        {
            using var stream = GetStream(name);
            if (stream == null)
                return null!;

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        public Task<byte[]> GetAsync(string? name, CancellationToken cancellationToken = default) =>
            Task.Run(() => Get(name), cancellationToken);

        public Stream? GetStream(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            foreach (var lookup in getBgaResourceLookups(name))
            {
                if (externalStore?.GetStream(lookup) is { } externalStream)
                {
                    return externalStream;
                }

                foreach (var workingBeatmapLookup in getWorkingBeatmapLookups(lookup))
                {
                    try
                    {
                        if (workingBeatmap?.Value.GetStream(workingBeatmapLookup) is { } stream)
                        {
                            return stream;
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            return null;
        }

        public string? ResolveLookup(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            foreach (var lookup in getBgaResourceLookups(name))
            {
                using (var externalStream = externalStore?.GetStream(lookup))
                {
                    if (externalStream != null)
                        return lookup;
                }

                foreach (var workingBeatmapLookup in getWorkingBeatmapLookups(lookup))
                {
                    Stream? stream = null;

                    try
                    {
                        stream = workingBeatmap?.Value.GetStream(workingBeatmapLookup);
                        if (stream != null)
                            return workingBeatmapLookup;
                    }
                    catch (Exception)
                    {
                    }
                    finally
                    {
                        stream?.Dispose();
                    }
                }
            }

            return null;
        }

        private static IEnumerable<string> getBgaResourceLookups(string name)
        {
            var yielded = new HashSet<string>(StringComparer.Ordinal);
            var extension = Path.GetExtension(name);
            var baseName = extension.Length > 0 ? name[..^extension.Length] : name;
            var candidateExtensions = video_extensions.Contains(extension)
                ? video_resource_extensions
                : image_extensions.Contains(extension)
                    ? image_resource_extensions
                    : [];

            foreach (var candidateExtension in candidateExtensions)
            {
                var candidate = baseName + candidateExtension;
                if (yielded.Add(candidate))
                    yield return candidate;
            }

            if (yielded.Add(name))
                yield return name;
        }

        private static IEnumerable<string> getWorkingBeatmapLookups(string name)
        {
            yield return name;

            var normalised = name.Replace('\\', '/');
            if (!string.Equals(normalised, name, StringComparison.Ordinal))
                yield return normalised;

            var fileName = Path.GetFileName(normalised);
            if (!string.IsNullOrWhiteSpace(fileName) && !string.Equals(fileName, normalised, StringComparison.Ordinal))
                yield return fileName;
        }

        public IEnumerable<string> GetAvailableResources() => [];

        public void Dispose()
        {
            externalStore?.Dispose();
        }
    }
}
