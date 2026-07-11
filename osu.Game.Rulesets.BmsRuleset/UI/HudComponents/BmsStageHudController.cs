using System;
using System.Collections.Specialized;
using System.Linq;
using osu.Framework.Graphics;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

internal sealed partial class BmsStageHudController : Component
{
    private readonly BmsPlayfield playfield;

    private BmsStageHud? stageHud;
    private ISerialisableDrawableContainer? stageHudContainer;
    private SerialisedDrawableInfo? lastStageHudInfo;
    private bool stageHudSizeNormalised;
    private bool ensuringStageHud;

    public BmsStageHudController(BmsPlayfield playfield)
    {
        this.playfield = playfield;
        AlwaysPresent = true;
    }

    protected override void Update()
    {
        base.Update();
        tryInitialiseHudSize();
        updateStageTransform();
    }

    internal void Register(BmsStageHud hud, ISerialisableDrawableContainer? container = null)
    {
        if (container != null)
            RegisterContainer(container);

        stageHud = hud;
        stageHudSizeNormalised = false;
        ensureStageHudSingleton();
        tryInitialiseHudSize();
        updateStageTransform();
    }

    internal void Unregister(BmsStageHud hud)
    {
        if (stageHud != hud)
            return;

        lastStageHudInfo = hud.CreateSerialisedInfo();
        stageHud = null;
        stageHudSizeNormalised = false;
        playfield.Stage.ClearHudTransform();
    }

    internal void RegisterContainer(ISerialisableDrawableContainer container)
    {
        if (stageHudContainer == container)
        {
            ensureStageHudSingleton();
            return;
        }

        unregisterContainer();

        stageHudContainer = container;
        stageHudContainer.Components.CollectionChanged += onComponentsChanged;

        if (stageHudContainer is SkinnableContainer skinnableContainer)
            skinnableContainer.OnComponentsLoaded += onComponentsLoaded;

        ensureStageHudSingleton();
    }

    protected override void Dispose(bool isDisposing)
    {
        unregisterContainer();
        playfield.Stage.ClearHudTransform();

        base.Dispose(isDisposing);
    }

    private void ensureStageHudSingleton()
    {
        if (stageHudContainer == null || ensuringStageHud || !stageHudContainerLoaded)
            return;

        ensuringStageHud = true;

        try
        {
            var huds = stageHudContainer.Components.OfType<BmsStageHud>().ToArray();

            if (huds.Length == 0)
            {
                stageHudContainer.Add(createReplacement());
                return;
            }

            var keeper = huds[0];

            foreach (var duplicate in huds.Where(hud => hud != keeper).ToArray())
                stageHudContainer.Remove(duplicate, true);

            stageHud = keeper;
            stageHudSizeNormalised = false;
            tryInitialiseHudSize();
            updateStageTransform();
        }
        finally
        {
            ensuringStageHud = false;
        }
    }

    private bool stageHudContainerLoaded => stageHudContainer is not SkinnableContainer skinnableContainer || skinnableContainer.ComponentsLoaded;

    private BmsStageHud createReplacement() =>
        lastStageHudInfo?.CreateInstance() as BmsStageHud ?? new BmsStageHud();

    private void onComponentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems?.OfType<BmsStageHud>().FirstOrDefault(hud => hud == stageHud) is BmsStageHud removedHud)
            lastStageHudInfo = removedHud.CreateSerialisedInfo();

        // SkinnableContainer clears its component list before marking a reload as incomplete.
        // The loaded callback is the stable point at which to repair the freshly loaded layout.
        if (e.Action == NotifyCollectionChangedAction.Reset && stageHudContainer is SkinnableContainer)
            return;

        if (e.NewItems?.OfType<BmsStageHud>().Any() == true)
        {
            Schedule(ensureStageHudSingleton);
            return;
        }

        ensureStageHudSingleton();
    }

    private void onComponentsLoaded(Drawable drawable) => ensureStageHudSingleton();

    private void unregisterContainer()
    {
        if (stageHudContainer == null)
            return;

        stageHudContainer.Components.CollectionChanged -= onComponentsChanged;

        if (stageHudContainer is SkinnableContainer skinnableContainer)
            skinnableContainer.OnComponentsLoaded -= onComponentsLoaded;

        stageHudContainer = null;
    }

    private void tryInitialiseHudSize()
    {
        if (stageHud?.Parent == null)
            return;

        var parentSize = stageHud.Parent.ChildSize;

        if (!isFiniteAndPositive(parentSize.X) || !isFiniteAndPositive(parentSize.Y))
            return;

        if (!stageHudSizeNormalised)
        {
            // Stage frames pre-dating relative sizing stored pixel dimensions. Converting them once
            // keeps existing layouts visually equivalent while making subsequent editor viewport
            // changes proportional to the gameplay viewport.
            stageHud.Size = new Vector2(
                isLegacyPixelDimension(stageHud.Size.X) ? stageHud.Size.X / parentSize.X : stageHud.Size.X,
                isLegacyPixelDimension(stageHud.Size.Y) ? stageHud.Size.Y / parentSize.Y : stageHud.Size.Y);
            stageHudSizeNormalised = true;
        }

        if (stageHud.Size.X > 0 && stageHud.Size.Y > 0)
            return;

        var stageQuad = playfield.Stage.ScreenSpaceDrawQuad;
        var topLeft = stageHud.Parent.ToLocalSpace(stageQuad.TopLeft);
        var topRight = stageHud.Parent.ToLocalSpace(stageQuad.TopRight);
        var bottomLeft = stageHud.Parent.ToLocalSpace(stageQuad.BottomLeft);
        var nativeSize = new Vector2(
            Vector2.Distance(topLeft, topRight),
            Vector2.Distance(topLeft, bottomLeft));

        if (!isFiniteAndPositive(nativeSize.X) || !isFiniteAndPositive(nativeSize.Y))
            return;

        stageHud.Size = new Vector2(
            stageHud.Size.X > 0 ? stageHud.Size.X : nativeSize.X / parentSize.X,
            stageHud.Size.Y > 0 ? stageHud.Size.Y : nativeSize.Y / parentSize.Y);
    }

    private void updateStageTransform()
    {
        if (stageHud?.Parent == null || playfield.DrawWidth <= 0 || playfield.DrawHeight <= 0)
        {
            playfield.Stage.ClearHudTransform();
            return;
        }

        var hudQuad = stageHud.ScreenSpaceDrawQuad;
        var topLeft = playfield.ToLocalSpace(hudQuad.TopLeft);
        var topRight = playfield.ToLocalSpace(hudQuad.TopRight);
        var bottomLeft = playfield.ToLocalSpace(hudQuad.BottomLeft);
        var localSize = new Vector2(
            Vector2.Distance(topLeft, topRight),
            Vector2.Distance(topLeft, bottomLeft));
        var stageSize = playfield.Stage.DrawSize;
        var scale = new Vector2(localSize.X / stageSize.X, localSize.Y / stageSize.Y);

        if (!isFiniteAndPositive(scale.X) || !isFiniteAndPositive(scale.Y))
        {
            playfield.Stage.ClearHudTransform();
            return;
        }

        var localCentre = playfield.ToLocalSpace(hudQuad.Centre);
        var viewportHeight = stageSize.Y;

        // Corner resizing preserves the native aspect ratio, while edge resizing may change the
        // axes independently. Treat only matching axis ratios as uniform scaling so a vertical
        // crop still works after the stage has already been stretched horizontally.
        if (Math.Abs(scale.X - scale.Y) >= 0.001f)
        {
            viewportHeight = localSize.Y;
            scale.Y = 1;
        }

        playfield.Stage.SetHudTransform(localCentre - playfield.DrawSize * 0.5f, scale, viewportHeight);
    }

    private static bool isLegacyPixelDimension(float value) => value > 4;

    private static bool isFiniteAndPositive(float value) => float.IsFinite(value) && value > 0;
}
