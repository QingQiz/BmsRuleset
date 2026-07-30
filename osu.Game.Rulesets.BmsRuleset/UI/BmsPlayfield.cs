using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.Skinning.Embedded;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;
using osu.Game.Rulesets.BmsRuleset.UI.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Objects;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI;

/// <inheritdoc cref="Playfield" />
/// <summary>
///     Native BMS playfield.  Manages the stage, hit-object container, key-sound playback,
///     judgement display, scroll-speed HUD, and input routing for all BMS layout variants.
/// </summary>
[Cached]
[Cached(typeof(IBmsLnScoring))]
public sealed partial class BmsPlayfield : Playfield, IKeyBindingHandler<BmsAction>, IBmsLnScoring
{

    #region Constants

    private const float minimum_side_padding = 20;

    internal const double RESUME_REWIND_DURATION = 5000;

    internal const double RESUME_REWIND_ANIMATION_DURATION = 400;

    #endregion

    #region Construction

    public BmsPlayfield(BmsBeatmap beatmap)
    {
        Beatmap = beatmap;
        textEventManager = new BmsTextEventManager(beatmap.TextEvents);

        activeSkin = new BmsEmbeddedSkinSource();
        skinCache = new BmsGameplaySkinCache(activeSkin);

        TotalColumns = Math.Max(1, beatmap.TotalColumns);
        LayoutVariant = beatmap.LayoutVariant;
        TimingMap = beatmap.TimingMap;
        ScrollController = new BmsScrollController(TimingMap);
        ScrollController.ScrollSpeedChanged += onScrollSpeedChanged;

        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        RelativeSizeAxes = Axes.Both;

        Stage = new BmsStage(this);
        Stage.SkinHitTargetPositionChanged += onSkinHitTargetPositionChanged;

        InternalChildren =
        [
            Stage,
        ];
    }

    #endregion

    #region Disposal

    protected override void Dispose(bool isDisposing)
    {
        ScrollController.ScrollSpeedChanged -= onScrollSpeedChanged;
        Stage.SkinHitTargetPositionChanged -= onSkinHitTargetPositionChanged;
        NewResult -= onNewResult;
        parentSkin.SourceChanged -= updateEmbeddedSkinFallback;
        base.Dispose(isDisposing);
        skinCache.Dispose();
        activeSkin.DisposeEmbeddedSkins();
    }

    #endregion

    #region HitObject routing

    public override void Add(HitObject hitObject)
    {
        if (hitObject is BmsHitObject bmsHo)
        {
            var col = bmsHo.Column;
            if (col >= 0 && col < Stage.Columns.Length)
            {
                ((Playfield)Stage.Columns[col]).Add(hitObject);
                return;
            }
        }

        base.Add(hitObject);
    }

    #endregion

    #region Skin

    private void updateEmbeddedSkinFallback()
    {
        activeSkin.SetSources(parentSkin, BmsEmbeddedSkinFallbackFactory.Create(parentSkin.AllSources, Beatmap, host.Renderer));
    }

    #endregion

    #region BmsEvents

    private readonly BmsTextEventManager textEventManager;

    private void triggerEvents()
    {
        textEventManager.Update(Time.Current, gameplayEvents.RaiseText);
    }

    #endregion

    #region Public properties

    public int TotalColumns { get; }

    public BmsLayoutVariant LayoutVariant { get; }

    public BmsStage Stage { get; }

    public override Quad SkinnableComponentScreenSpaceDrawQuad => Stage.ScreenSpaceDrawQuad;

    internal void AddBehindStage(Drawable drawable) => AddInternal(drawable);

    public BmsTimingMap? TimingMap { get; }

    internal BmsScrollController ScrollController { get; }

    /// <summary>
    /// Applied only to predictable scrolling visuals so judgements remain based on <c>Time.Current</c>.
    /// </summary>
    internal BindableDouble VisualOffset { get; } = new();

    #endregion

    #region Skin / DI

    [Cached(typeof(ISkinSource))]
    private readonly BmsEmbeddedSkinSource activeSkin;

    [Cached]
    private readonly BmsGameplaySkinCache skinCache;

    internal readonly BmsBeatmap Beatmap;

    private BmsHealthProcessor? healthProcessor => resolvedHealthProcessor as BmsHealthProcessor;

    private BmsScoreProcessor? scoreProcessor => resolvedScoreProcessor as BmsScoreProcessor;

    [Resolved(CanBeNull = true)]
    private HealthProcessor? resolvedHealthProcessor { get; set; }

    [Resolved(CanBeNull = true)]
    private ScoreProcessor? resolvedScoreProcessor { get; set; }

    [Resolved]
    private GameHost host { get; set; } = null!;

    [Resolved]
    private ISkinSource parentSkin { get; set; } = null!;

    [Resolved]
    private IBmsGameplayEvents gameplayEvents { get; set; } = null!;

    #endregion

    #region Input

    public bool OnPressed(KeyBindingPressEvent<BmsAction> e)
    {
        switch (e.Action)
        {
            case BmsAction.IncreaseScrollSpeed:
                ScrollController.AdjustScrollSpeed(1);
                return true;

            case BmsAction.DecreaseScrollSpeed:
                ScrollController.AdjustScrollSpeed(-1);
                return true;
        }

        var column = BmsKeyBindingConfiguration.ActionToColumn(e.Action, LayoutVariant);

        if (column == null || column.Value >= TotalColumns)
            return false;

        var outcome = Stage.Columns[column.Value].HandlePress(Time.Current);

        if (outcome is { Kind: PressOutcomeKind.EmptyPoor, ExpectedTime: { } expectedTime })
            registerEmptyPoor(expectedTime, outcome.Column);

        return outcome.Kind == PressOutcomeKind.Hit;
    }

    public void OnReleased(KeyBindingReleaseEvent<BmsAction> e)
    {
        switch (e.Action)
        {
            case BmsAction.IncreaseScrollSpeed:
            case BmsAction.DecreaseScrollSpeed:
                return;
        }

        var column = BmsKeyBindingConfiguration.ActionToColumn(e.Action, LayoutVariant);

        if (column == null || column.Value >= TotalColumns)
            return;

        Stage.Columns[column.Value].HandleRelease(Time.Current);
    }

    #endregion

    #region Scroll Speed

    public double ScrollSpeed => ScrollController.ScrollSpeed;

    private void onScrollSpeedChanged(double multiplier)
    {
        gameplayEvents.RaiseScrollSpeedChanged(multiplier);
        // The framework's default lifetime starts at hit time; BMS scroll needs notes alive
        // before then so near-future objects can be positioned and judged.
        RefreshAllLifetimes();
    }

    private void onSkinHitTargetPositionChanged(float position)
    {
        ScrollController.SetHitTargetPosition(position);

        if (IsLoaded)
            RefreshAllLifetimes();
    }

    internal void RefreshAllLifetimes()
    {
        var currentTime = IsLoaded ? Time.Current : (double?)null;

        foreach (var column in Stage.Columns)
        {
            if (column.HitObjectContainer is BmsColumnHitObjectContainer container)
                container.RefreshAllEntries(currentTime);
        }
    }

    internal void ApplyVisualOffsetToAllLifetimes()
    {
        foreach (var column in Stage.Columns)
        {
            if (column.HitObjectContainer is BmsColumnHitObjectContainer container)
                container.ApplyVisualOffsetToAllEntries();
        }
    }

    #endregion

    #region Lifecycle

    private double resumeRewindInitialVisualOffset;

    private double resumeRewindAnimationElapsed = RESUME_REWIND_ANIMATION_DURATION;

    internal double ResumeRewindStartTime { get; private set; } = double.MinValue;

    internal double ResumeRewindEndTime { get; private set; } = double.MinValue;

    internal bool IsResumeRewinding => Time.Current < ResumeRewindEndTime;

    internal bool IsResumeRewindAnimating => resumeRewindAnimationElapsed < RESUME_REWIND_ANIMATION_DURATION;

    internal double DisplayTime { get; private set; }

    internal double BeginResumeRewind(double pauseTime, double minimumTime)
    {
        var continuesExistingRewind = pauseTime >= ResumeRewindStartTime && pauseTime < ResumeRewindEndTime;
        var rewindTarget = continuesExistingRewind
            ? ResumeRewindStartTime
            : ComputeResumeRewindTarget(pauseTime, minimumTime);

        if (!continuesExistingRewind)
            ResumeRewindStartTime = rewindTarget;

        ResumeRewindEndTime = Math.Max(ResumeRewindEndTime, pauseTime);
        resumeRewindInitialVisualOffset = pauseTime - rewindTarget;
        resumeRewindAnimationElapsed = 0;
        return rewindTarget;
    }

    internal static double ComputeResumeRewindTarget(double pauseTime, double minimumTime) =>
        Math.Max(minimumTime, pauseTime - RESUME_REWIND_DURATION);

    internal static double ComputeResumeRewindVisualOffset(double initialOffset, double elapsed)
    {
        var progress = Math.Clamp(elapsed / RESUME_REWIND_ANIMATION_DURATION, 0, 1);
        return initialOffset * Math.Pow(1 - progress, 3);
    }

    internal static double ComputeDisplayTime(double currentTime, double visualOffset, double playbackRate, double resumeRewindOffset) =>
        currentTime + visualOffset * playbackRate + resumeRewindOffset;

    [BackgroundDependencyLoader(true)]
    private void load()
    {
        ScrollController.SetHitTargetPosition(Stage.SkinHitTargetPosition);

        parentSkin.SourceChanged += updateEmbeddedSkinFallback;
        updateEmbeddedSkinFallback();
        skinCache.WarmLongNoteTextures(Beatmap, host.Renderer);
    }

    protected override HitObjectLifetimeEntry CreateLifetimeEntry(HitObject hitObject) =>
        new BmsHitObjectLifetimeEntry(hitObject, ScrollController, () => VisualOffset.Value);

    protected override void LoadComplete()
    {
        base.LoadComplete();

        populateMeasureLines();

        // Subscribe to per-column NewResult events so BmsPlayfield aggregates all results.
        foreach (var column in Stage.Columns)
        {
            if (column is Playfield pf)
            {
                pf.NewResult += onNewResult;
                AddNested(pf);
            }
        }

        VisualOffset.BindValueChanged(_ => ApplyVisualOffsetToAllLifetimes());
        RefreshAllLifetimes();
    }

    protected override void Update()
    {
        if (IsResumeRewindAnimating && Time.Elapsed > 0)
            resumeRewindAnimationElapsed += Time.Elapsed / Math.Max(Math.Abs(Clock.Rate), 0.01);

        var resumeRewindOffset = ComputeResumeRewindVisualOffset(resumeRewindInitialVisualOffset, resumeRewindAnimationElapsed);
        DisplayTime = ComputeDisplayTime(Time.Current, VisualOffset.Value, ScrollController.PlaybackRate, resumeRewindOffset);
        ScrollController.Update(DisplayTime);

        // Playfield.Update normally reverts results newer than the clock. During the resume lead-in,
        // those results belong to the completed attempt and must remain authoritative.
        if (!IsResumeRewinding)
            base.Update();

        triggerEvents();
        updateStageScale();
    }

    #endregion

    #region Layout

    private void populateMeasureLines()
    {
        if (TimingMap == null)
            return;

        Stage.MeasureLineArea.SetTimingMap(TimingMap, ScrollController, Stage);
    }

    private void updateStageScale()
    {
        if (Stage.HasHudTransform)
            return;

        if (!Stage.IsLoaded || Stage.DrawWidth <= 0 || DrawWidth <= 0)
            return;

        var healthReserve = Math.Max(64, minimum_side_padding);
        var availableWidth = Math.Max(1, DrawWidth - healthReserve * 2);
        var scale = Math.Min(1, availableWidth / Stage.DrawWidth);

        if (float.IsFinite(scale) && scale > 0)
            Stage.Scale = new Vector2(scale, 1);
    }

    #endregion

    #region Judgements

    private void onNewResult(DrawableHitObject drawableHitObject, JudgementResult result)
    {
        if (drawableHitObject is not DrawableBmsHitObject bmsHitObject)
            return;

        if (bmsHitObject.HitObject is BmsLandmine)
        {
            requestJudgementDisplay(HitResult.Meh);
            return;
        }

        requestJudgementDisplay(result.Type);
    }

    private void registerEmptyPoor(double expectedTime, int column)
    {
        scoreProcessor?.RegisterEmptyPoor(Time.Current, expectedTime, column);
        healthProcessor?.RegisterEmptyPoor(Time.Current);
        requestJudgementDisplay(HitResult.Miss);
    }

    private void requestJudgementDisplay(HitResult result)
    {
        if (result == HitResult.Meh)
            textEventManager.TriggerMistake(gameplayEvents.RaiseText);

        gameplayEvents.RaiseJudgementDisplayed(result);
    }

    /// <summary>
    ///     Registers a separate CN/HCN endpoint through the score and health processors.
    /// </summary>
    public void ApplySyntheticLongNoteEndpoint(DrawableBmsHitObject drawable, BmsLongNoteEndpointResult endpoint)
    {
        if (drawable.HitObject is not BmsLongNote)
            return;

        var scoreResult = scoreProcessor?.ApplySyntheticLongNoteEndpoint(endpoint);

        if (scoreResult != null)
            healthProcessor?.ApplySyntheticLongNoteEndpoint(scoreResult);

        if (endpoint.Kind == BmsLongNoteEndpointKind.Tail && endpoint.Result.IsHit())
        {
            var column = Math.Clamp(drawable.HitObject.Column, 0, Stage.Columns.Length - 1);
            Stage.Columns[column].TriggerHitExplosion(drawable.HitObject is BmsLongNote);
        }

        requestJudgementDisplay(endpoint.Result);
    }

    /// <summary>
    ///     Applies a HellChargeNote body gauge tick for the currently pressed column.
    /// </summary>
    public void ApplyHellChargeTick(bool holding, double scale = 0.5) => healthProcessor?.ApplyHellChargeTick(holding, scale, Time.Current);

    #endregion

}
