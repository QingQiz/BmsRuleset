using System;
using System.Collections.Specialized;
using System.Linq;
using osu.Framework.Graphics;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

internal sealed partial class BmsStageHudController : Component
{
    // HUD resizing is applied through Stage.Scale.X, so this remains the skin/layout-derived
    // baseline width. It changes when the layout or skin column widths change, not when the HUD is resized.
    internal float StageWidth => playfield.Stage.DrawWidth;

    private readonly BmsPlayfield playfield;

    private BmsStageHud? stageHud;
    private ISerialisableDrawableContainer? stageHudContainer;
    private bool ensuringStageHud;

    public BmsStageHudController(BmsPlayfield playfield)
    {
        this.playfield = playfield;
        AlwaysPresent = true;
    }

    protected override void Update()
    {
        base.Update();
        normaliseProportionalHudWidth();
        tryInitialiseHudSize();
        updateStageTransform();
        updatePositionOffsetRanges();
    }

    internal void Register(BmsStageHud hud, ISerialisableDrawableContainer? container = null)
    {
        if (container != null)
            RegisterContainer(container);

        stageHud = hud;

        // Width used to be stored relative to the skin editor canvas, which differs from the gameplay canvas.
        // Such values are unusably small as absolute widths and must be rebuilt from the current Stage.
        if (stageHud.Width is > 0 and <= 1)
            stageHud.Width = 0;

        normaliseProportionalHudWidth();
        scheduleStageHudSingleton();
        SetNoteHeightScale(stageHud?.NoteHeightScale.Value ?? 1);
        tryInitialiseHudSize();
        updateStageTransform();
        updatePositionOffsetRanges();
        SetHitTargetPositionOffset(stageHud!.JudgementLineOffset.Value);
        SetLightPositionOffset(stageHud.LightPositionOffset.Value);
    }

    internal void SetHitTargetPositionOffset(float offset) => playfield.Stage.SetHitTargetPositionOffset(offset);

    internal void SetLightPositionOffset(float offset) => playfield.Stage.SetLightPositionOffset(offset);

    internal void SetNoteHeightScale(float scale) => playfield.Stage.SetNoteHeightScale(scale);

    internal void Unregister(BmsStageHud hud)
    {
        if (stageHud != hud)
            return;

        stageHud = null;
        hud.SetCurrentStageWidth(0);
        playfield.Stage.SetHitTargetPositionOffset(0);
        playfield.Stage.SetLightPositionOffset(0);
        playfield.Stage.SetNoteHeightScale(1);
        playfield.Stage.ClearHudTransform();
    }

    internal void RegisterContainer(ISerialisableDrawableContainer container)
    {
        if (stageHudContainer == container)
        {
            scheduleStageHudSingleton();
            return;
        }

        unregisterContainer();

        stageHudContainer = container;
        stageHudContainer.Components.CollectionChanged += onComponentsChanged;

        if (stageHudContainer is SkinnableContainer skinnableContainer)
            skinnableContainer.OnComponentsLoaded += onComponentsLoaded;

        scheduleStageHudSingleton();
    }

    protected override void Dispose(bool isDisposing)
    {
        unregisterContainer();
        playfield.Stage.SetHitTargetPositionOffset(0);
        playfield.Stage.SetLightPositionOffset(0);
        playfield.Stage.SetNoteHeightScale(1);
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
            normaliseProportionalHudWidth();
            SetNoteHeightScale(keeper.NoteHeightScale.Value);
            tryInitialiseHudSize();
            updateStageTransform();
            updatePositionOffsetRanges();
            SetHitTargetPositionOffset(keeper.JudgementLineOffset.Value);
            SetLightPositionOffset(keeper.LightPositionOffset.Value);
        }
        finally
        {
            ensuringStageHud = false;
        }
    }

    private bool stageHudContainerLoaded => stageHudContainer is not SkinnableContainer skinnableContainer || skinnableContainer.ComponentsLoaded;

    private void normaliseProportionalHudWidth()
    {
        if (stageHud == null)
            return;

        var reference = stageHud.ProportionalWidthReference.Value;
        var current = StageWidth;
        stageHud.SetCurrentStageWidth(current);

        if (!isFiniteAndPositive(reference) || !isFiniteAndPositive(current) || !isFiniteAndPositive(stageHud.Width))
            return;

        // Keeping the HUD bounds in the current layout's units makes the editor handle match the
        // proportionally-sized Stage while preserving the configured width ratio across layouts.
        stageHud.Width *= current / reference;
        stageHud.ProportionalWidthReference.Value = current;
    }

    // Registration can run during asynchronous layout loading while the previous content is being disposed.
    // Deferring repair keeps child mutations on the update thread after SkinnableContainer has swapped content.
    private void scheduleStageHudSingleton() => Scheduler.AddOnce(ensureStageHudSingleton);

    private static BmsStageHud createReplacement() => new();

    private void onComponentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems?.OfType<BmsStageHud>().Any(hud => hud == stageHud) == true)
        {
            stageHud = null;
            playfield.Stage.SetHitTargetPositionOffset(0);
            playfield.Stage.SetLightPositionOffset(0);
            playfield.Stage.SetNoteHeightScale(1);
            playfield.Stage.ClearHudTransform();
        }

        // SkinnableContainer clears its component list before marking a reload as incomplete.
        // The loaded callback is the stable point at which to repair the freshly loaded layout.
        if (e.Action == NotifyCollectionChangedAction.Reset && stageHudContainer is SkinnableContainer)
            return;

        scheduleStageHudSingleton();
    }

    private void onComponentsLoaded(Drawable drawable) => scheduleStageHudSingleton();

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

        if (!isFiniteAndPositive(parentSize.Y))
            return;

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
            stageHud.Size.X > 0 ? stageHud.Size.X : nativeSize.X,
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
        var localCentre = playfield.ToLocalSpace(hudQuad.Centre);

        ApplyStageTransform(localSize, localCentre);
    }

    internal void ApplyStageTransform(Vector2 localSize, Vector2 localCentre)
    {
        var stageSize = new Vector2(playfield.Stage.DrawWidth, playfield.Stage.HudBaseDrawHeight);
        var contentScale = Math.Abs(stageHud?.Scale.Y ?? 0);
        var widthReference = stageHud?.ProportionalWidthReference.Value is > 0 and var reference ? reference : stageSize.X;
        var scale = new Vector2(localSize.X / widthReference, contentScale);
        var viewportHeight = localSize.Y / contentScale;

        if (!isFiniteAndPositive(scale.X) || !isFiniteAndPositive(scale.Y) || !isFiniteAndPositive(viewportHeight))
        {
            playfield.Stage.ClearHudTransform();
            return;
        }

        // Corner resizing changes Drawable.Scale, while edge resizing changes Width or Height.
        // Keeping those independent avoids transform drift across arbitrary resize sequences.
        playfield.Stage.SetHudTransform(localCentre - playfield.DrawSize * 0.5f, scale, viewportHeight);
    }

    private void updatePositionOffsetRanges()
    {
        if (stageHud == null)
            return;

        var skinPosition = playfield.Stage.SkinHitTargetPosition;
        var stageHeight = playfield.Stage.HasHudTransform
            ? playfield.Stage.HudViewportHeight
            : playfield.Stage.DrawHeight;

        if (!float.IsFinite(skinPosition) || !isFiniteAndPositive(stageHeight))
            return;

        stageHud.SetJudgementLineOffsetRange(-skinPosition, stageHeight - skinPosition);
        var skinLightPosition = playfield.Stage.SkinLightPosition;
        stageHud.SetLightPositionOffsetRange(-skinLightPosition, stageHeight - skinLightPosition);
    }

    private static bool isFiniteAndPositive(float value) => float.IsFinite(value) && value > 0;
}
