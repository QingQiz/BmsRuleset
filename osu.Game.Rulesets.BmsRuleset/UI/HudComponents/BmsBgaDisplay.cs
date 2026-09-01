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
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.IO.ResourceStore;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Media.Video;
using osu.Game.Rulesets.BmsRuleset.Media.Video.Supplemental;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

public sealed partial class BmsBgaDisplay : BmsHudComponent
{
    private static readonly string[] video_resource_extensions =
    [
        ".mp4",
        ".avi",
        ".webm",
        ".mov",
        ".mpeg",
        ".mpg",
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

    // Init-only and not a [SettingSource], so saved skin layouts keep using the rehost path.
    // Otherwise deserialised BGAs would stay in the HUD overlay and render above the playfield.
    public bool RenderOutsideHudVisibility { get; init; } = true;

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.BgaFillScreen), nameof(BmsStrings.BgaFillScreenDescription))]
    public BindableBool FillScreen { get; } = new();

    private readonly Container layers;
    private readonly Dictionary<BmsBgaLayer, BmsBgaEvent> activeEvents = new();
    private readonly Dictionary<BmsBgaLayer, BmsBgaOpacityEvent> activeOpacityEvents = new();
    private readonly Dictionary<BmsBgaLayer, Container> layerHosts = new();
    private readonly Dictionary<BmsBgaLayer, int> layerGenerations = new();

    private BmsBgaTimeline? bga;
    private BmsBgaResourceStore? resources;
    private TextureStore? textures;
    private BmsBgaVideoPreloader? videoPreloader;
    private BmsBgaEvent[] events = [];
    private BmsBgaOpacityEvent[] opacityEvents = [];
    private int nextEventIndex;
    private int nextOpacityEventIndex;
    private double lastTime = double.NegativeInfinity;
    private double poorLayerUntil = double.NegativeInfinity;
    private bool poorLayerVisible;
    private IBmsGameplayEvents? gameplayEvents;
    private BmsBgaDisplay? rehostedDisplay;
    private RehostedDisplayHost? rehostedDisplayHost;
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
        FillScreen.BindValueChanged(e => applyFillScreen(e.NewValue), true);

        if (tryAddPlayfieldDisplay())
            return;

        gameplayEvents = (drawableRuleset as BmsDrawableRuleset)?.GameplayEvents;
        if (gameplayEvents != null)
            gameplayEvents.JudgementDisplayed += onJudgementDisplayed;
    }

    protected override void Update()
    {
        base.Update();

        if (FillScreen.Value && !isFillScreenLayout())
            disableFillScreenForCustomLayout();

        if (rehostedDisplay != null)
        {
            syncRehostedDisplay();
            return;
        }

        if (bga == null || drawableRuleset == null)
            return;

        var time = drawableRuleset.FrameStableClock.CurrentTime;

        var pendingEvents = upperBound(events, time) - nextEventIndex;
        var pendingOpacityEvents = upperBound(opacityEvents, time) - nextOpacityEventIndex;

        // Replaying every BGA event after a seek (or a long pause) can create hundreds of
        // textures/video drawables in one update. Rebuild only the final state for each layer;
        // normal frame-sized advances retain the ordered path below.
        if (time < lastTime || pendingEvents > 64 || pendingOpacityEvents > 64)
            rebuildStateAt(time);

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

    private void rebuildStateAt(double time)
    {
        activeEvents.Clear();
        activeOpacityEvents.Clear();
        poorLayerVisible = false;
        poorLayerUntil = double.NegativeInfinity;
        clearLayerHosts();

        var eventEnd = upperBound(events, time);
        for (var i = 0; i < eventEnd; i++)
            activeEvents[events[i].Layer] = events[i];

        var opacityEnd = upperBound(opacityEvents, time);
        for (var i = 0; i < opacityEnd; i++)
            activeOpacityEvents[opacityEvents[i].Layer] = opacityEvents[i];

        nextEventIndex = eventEnd;
        nextOpacityEventIndex = opacityEnd;

        foreach (var evt in activeEvents.Values)
            setLayerDrawable(evt);

        foreach (var layer in activeOpacityEvents.Keys)
            applyLayerOpacity(layer);

        applyLayerVisibility();
    }

    private static int upperBound(BmsBgaEvent[] source, double time)
    {
        var low = 0;
        var high = source.Length;

        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (source[middle].Time <= time)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private static int upperBound(BmsBgaOpacityEvent[] source, double time)
    {
        var low = 0;
        var high = source.Length;

        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (source[middle].Time <= time)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    protected override void Dispose(bool isDisposing)
    {
        textures?.Dispose();
        resources?.Dispose();
        // Idempotent: the shell and the rehosted display share one preloader, so Dispose fires twice.
        videoPreloader?.Dispose();
        rehostedDisplayHost?.RemoveFromParentOnUpdate();
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

        resources ??= new BmsBgaResourceStore(bmsDrawableRuleset.BeatmapSourceDirectory, workingBeatmap);
        textures ??= new TextureStore(host.Renderer, host.CreateTextureLoaderStore(resources), false, scaleAdjust: 1);

        // Reuse a preloader handed over from the HUD shell when this display is the rehosted renderer,
        // so the loading screen only spawns one decoder per video (the shell warms; the rehosted
        // display consumes). ??= leaves a shared instance intact and creates one otherwise.
        videoPreloader ??= new BmsBgaVideoPreloader();

        preloadBgaResources();
    }

    private void preloadBgaResources()
    {
        if (bga == null || resources == null || textures == null)
            return;

        var declaredPaths = bga.BitmapDefinitions.Values
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        resources.Preload(declaredPaths);

        foreach (var path in declaredPaths)
        {
            var resolvedPath = resources.ResolveLookup(path);
            if (resolvedPath == null)
                continue;

            // Videos: warm the supplemental decoder on the loader thread now so the first gameplay
            // frame doesn't pay FFmpeg init + first-frame latency. Images: upload into the texture
            // store as before. Files the supplemental probe rejects are left for the factory's lazy
            // path (framework/missing) — no warm source is created for them.
            if (isVideo(resolvedPath))
                videoPreloader?.Preload(resolvedPath, resources.Get(resolvedPath));
            else
                textures.Get(resolvedPath);
        }
    }

    private void bindBgaDim()
    {
        if (bgaDim != null || drawableRuleset is not BmsDrawableRuleset bmsDrawableRuleset)
            return;

        bgaDim = bmsDrawableRuleset.BgaDim.GetBoundCopy();
        bgaDim.BindValueChanged(e => applyBgaDim(e.NewValue), true);
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

    private void applyFillScreen(bool fillScreen)
    {
        if (!fillScreen)
        {
            if (isFillScreenLayout())
                preserveCurrentLayoutAsAbsolute();

            return;
        }

        Anchor = Anchor.TopLeft;
        Origin = Anchor.TopLeft;
        Position = Vector2.Zero;
        Scale = Vector2.One;
        Rotation = 0;
        RelativeSizeAxes = Axes.Both;
        Size = Vector2.One;
    }

    private bool isFillScreenLayout() =>
        Anchor == Anchor.TopLeft
        && Origin == Anchor.TopLeft
        && Position == Vector2.Zero
        && Scale == Vector2.One
        && Rotation == 0
        && RelativeSizeAxes == Axes.Both
        && Size == Vector2.One;

    private void disableFillScreenForCustomLayout()
    {
        preserveCurrentLayoutAsAbsolute();

        FillScreen.Value = false;
    }

    private void preserveCurrentLayoutAsAbsolute()
    {
        if (Parent == null)
            return;

        var quad = ScreenSpaceDrawQuad;
        var topLeft = Parent.ToLocalSpace(quad.TopLeft);
        var width = (Parent.ToLocalSpace(quad.TopRight) - topLeft).Length;
        var height = (Parent.ToLocalSpace(quad.BottomLeft) - topLeft).Length;
        var scale = new Vector2(MathF.Max(0.001f, MathF.Abs(Scale.X)), MathF.Max(0.001f, MathF.Abs(Scale.Y)));

        RelativeSizeAxes = Axes.None;
        Size = new Vector2(width / scale.X, height / scale.Y);
    }

    private bool tryAddPlayfieldDisplay()
    {
        var hudOverlay = this.FindClosestParent<HUDOverlay>();

        if (!RenderOutsideHudVisibility || isRehostedDisplay || hudOverlay is null || drawableRuleset?.Playfield is not BmsPlayfield playfield)
            return false;

        rehostedDisplay = new BmsBgaDisplay(true)
        {
            Anchor = Anchor,
            Origin = Origin,
            Position = Position,
            Scale = Scale,
            Rotation = Rotation,
            Size = Size,
            Depth = float.MaxValue,
            RenderOutsideHudVisibility = false,
        };

        // The shell has already loaded every BGA resource. Transfer the complete resource set to
        // the renderer instead of letting its dependency loader retain a second copy of all source
        // bytes, decoded textures, and warmed video sources for the duration of gameplay.
        rehostedDisplay.bga = bga;
        rehostedDisplay.resources = resources;
        rehostedDisplay.textures = textures;
        rehostedDisplay.videoPreloader = videoPreloader;
        rehostedDisplay.events = events;
        rehostedDisplay.opacityEvents = opacityEvents;
        resources = null;
        textures = null;
        videoPreloader = null;
        if (bgaDim != null)
            rehostedDisplay.applyBgaDim(bgaDim.Value);
        rehostedDisplayHost = new RehostedDisplayHost
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

        var sourceQuad = ScreenSpaceDrawQuad;
        var topLeft = playfield.ToLocalSpace(sourceQuad.TopLeft);
        var topRight = playfield.ToLocalSpace(sourceQuad.TopRight);
        var bottomLeft = playfield.ToLocalSpace(sourceQuad.BottomLeft);
        var xAxis = topRight - topLeft;
        var yAxis = bottomLeft - topLeft;

        rehostedDisplayHost.Position = topLeft;
        rehostedDisplayHost.Size = new Vector2(xAxis.Length, yAxis.Length);
        rehostedDisplayHost.Rotation = MathHelper.RadiansToDegrees(MathF.Atan2(xAxis.Y, xAxis.X));

        configureRehostedDisplayToFillHost(rehostedDisplay);
    }

    private static void configureRehostedDisplayToFillHost(BmsBgaDisplay target)
    {
        target.Anchor = Anchor.TopLeft;
        target.Origin = Anchor.TopLeft;
        target.Position = Vector2.Zero;
        target.Scale = Vector2.One;
        target.Rotation = 0;
        target.RelativeSizeAxes = Axes.Both;
        target.Size = Vector2.One;
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
        var generation = layerGenerations.TryGetValue(evt.Layer, out var previousGeneration)
            ? previousGeneration + 1
            : 0;
        layerGenerations[evt.Layer] = generation;

        if (drawable is BmsDeferredVideoDrawable deferredVideo)
        {
            // Adding a child to an already-loaded host loads it synchronously. Video creation
            // performs file IO and codec probing, so keep that work on the framework loader and
            // only attach the drawable after it is ready. An event may have been superseded while
            // the load was in flight; in that case discard the stale drawable.
            LoadComponentAsync(deferredVideo, loaded =>
            {
                if (!IsDisposed
                    && layerGenerations.TryGetValue(evt.Layer, out var currentGeneration)
                    && currentGeneration == generation
                    && activeEvents.TryGetValue(evt.Layer, out var active)
                    && active.Sequence == evt.Sequence)
                    host.Add(loaded);
                else
                    loaded.Dispose();
            });
        }
        else if (drawable != null)
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
        var clock = drawableRuleset?.FrameStableClock ?? Clock;

        // Hand off the source warmed during loading — no stream open, no FFmpeg init, no first-frame
        // wait on the game thread. Falls through to the factory for files that weren't warmed
        // (framework-routed, probe-rejected, or a second hit after the one-shot warm source was taken).
        if (videoPreloader?.TryCreateDrawable(path, clock, eventStartTime, out var warmDrawable) == true)
            return warmDrawable;

        var resourceStore = resources;
        if (resourceStore == null)
            return null;

        // Opening and probing a video can involve file IO and codec initialisation. Defer both to
        // the drawable loading pathway because this method is reached from Update().
        return new BmsDeferredVideoDrawable(path, () => resourceStore.GetStream(path), clock, eventStartTime)
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            FillMode = FillMode.Stretch,
        };
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

    internal sealed class BmsBgaResourceStore(string? sourceDirectory, IBindable<WorkingBeatmap>? workingBeatmap) : IResourceStore<byte[]>
    {
        private readonly BmsFileResourceStore? externalStore = !string.IsNullOrWhiteSpace(sourceDirectory) && Directory.Exists(sourceDirectory)
            ? new BmsFileResourceStore(sourceDirectory)
            : null;

        private readonly object cacheLock = new();
        private readonly Dictionary<string, byte[]?> cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string?> resolvedLookups = new(StringComparer.OrdinalIgnoreCase);

        public void Preload(IEnumerable<string> names)
        {
            foreach (var name in names)
                Get(name);
        }

        public byte[] Get(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null!;

            lock (cacheLock)
            {
                if (cache.TryGetValue(name, out var cached))
                    return cached!;
            }

            var resolved = ResolveLookup(name);

            if (resolved == null)
            {
                setCache(name, null);
                return null!;
            }

            lock (cacheLock)
            {
                if (cache.TryGetValue(resolved, out var resolvedCached))
                {
                    cache[name] = resolvedCached;
                    return resolvedCached!;
                }
            }

            using var stream = openResourceStream(resolved);
            if (stream == null)
            {
                setCache(name, null);
                return null!;
            }

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();

            lock (cacheLock)
            {
                cache[name] = bytes;
                cache[resolved] = bytes;
            }

            return bytes;
        }

        public Task<byte[]> GetAsync(string? name, CancellationToken cancellationToken = default) =>
            Task.Run(() => Get(name), cancellationToken);

        public Stream GetStream(string? name)
        {
            var bytes = Get(name);
            return new MemoryStream(bytes, false);
        }

        public string? ResolveLookup(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            lock (cacheLock)
            {
                if (resolvedLookups.TryGetValue(name, out var cached))
                    return cached;
            }

            foreach (var lookup in getBgaResourceLookups(name))
            {
                lock (cacheLock)
                {
                    if (cache.TryGetValue(lookup, out var cachedBytes) && cachedBytes != null)
                    {
                        resolvedLookups[name] = lookup;
                        return lookup;
                    }
                }

                using var stream = openResourceStream(lookup);

                if (stream != null)
                {
                    setResolvedLookup(name, lookup);
                    return lookup;
                }
            }

            setResolvedLookup(name, null);
            return null;
        }

        private Stream? openResourceStream(string name)
        {
            if (externalStore?.GetStream(name) is { } externalStream)
                return externalStream;

            foreach (var workingBeatmapLookup in getWorkingBeatmapLookups(name))
            {
                try
                {
                    if (workingBeatmap?.Value.GetStream(workingBeatmapLookup) is { } stream)
                        return stream;
                }
                catch (Exception)
                {
                }
            }

            return null;
        }

        private void setCache(string name, byte[]? bytes)
        {
            lock (cacheLock)
                cache[name] = bytes;
        }

        private void setResolvedLookup(string name, string? resolved)
        {
            lock (cacheLock)
                resolvedLookups[name] = resolved;
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
            lock (cacheLock)
            {
                cache.Clear();
                resolvedLookups.Clear();
            }

            externalStore?.Dispose();
        }
    }

    private sealed partial class RehostedDisplayHost : Container
    {
        private bool removalQueued;
        private Drawable? removalParent;

        public void RemoveFromParentOnUpdate()
        {
            if (IsDisposed || removalQueued)
                return;

            removalQueued = true;

            removalParent = Parent;

            if (removalParent == null)
            {
                Dispose();
                return;
            }

            // Parent update runs before child traversal, so the host leaves the tree before it can be updated again.
            removalParent.OnUpdate += removeFromParent;
        }

        private void removeFromParent(Drawable parent)
        {
            parent.OnUpdate -= removeFromParent;
            removalParent = null;

            if (!IsDisposed)
                this.RemoveAndDisposeImmediately();
        }

        protected override void Dispose(bool isDisposing)
        {
            if (removalParent != null)
            {
                removalParent.OnUpdate -= removeFromParent;
                removalParent = null;
            }

            base.Dispose(isDisposing);
        }
    }
}
