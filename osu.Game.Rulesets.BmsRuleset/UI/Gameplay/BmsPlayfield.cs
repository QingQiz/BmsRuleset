using System;
using System.Collections.Generic;
using System.Linq;
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
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

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
        textEventController = new BmsGameplayTextEventController(beatmap.TextEvents);

        activeSkin = new BmsEmbeddedSkinSource();
        skinCache = new BmsGameplaySkinCache(activeSkin);

        TotalColumns = Math.Max(1, beatmap.TotalColumns);
        InitialPoolSizes = BmsHitObjectPoolPlan.Create(beatmap.HitObjects, TotalColumns);
        InitialHitExplosionSizes = BmsHitObjectPoolPlan.CreateHitExplosionSizes(beatmap.HitObjects, TotalColumns);
        InitialLongNoteHitExplosionSizes = BmsHitObjectPoolPlan.CreateLongNoteHitExplosionSizes(beatmap.HitObjects, TotalColumns);
        LayoutVariant = beatmap.LayoutVariant;
        TimingMap = beatmap.TimingMap;
        ScrollController = new BmsGameplayScrollController(TimingMap);
        ScrollController.ScrollSpeedChanged += onScrollSpeedChanged;

        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        RelativeSizeAxes = Axes.Both;

        Stage = new BmsStage(this);
        Stage.SkinHitTargetPositionChanged += onSkinHitTargetPositionChanged;
        framePressedDirections = new int[TotalColumns];

        // beatoraja visits each side's ordinary lanes before its scratch. Our first
        // scratch is column zero, so visual column order would change gauge clipping.
        var firstScratchLane = LayoutVariant switch
        {
            BmsLayoutVariant.Bms5K or BmsLayoutVariant.Bms5K2P or BmsLayoutVariant.Bms5KDouble => 5,
            BmsLayoutVariant.Bme7K or BmsLayoutVariant.Bme7K2P or BmsLayoutVariant.Bme7KDouble => 7,
            _ => -1,
        };
        judgementColumns = Stage.Columns.Cast<BmsColumn>()
                                .OrderBy(column => firstScratchLane < 0 || column.Index > firstScratchLane
                                    ? column.Index : column.Index == 0 ? firstScratchLane : column.Index - 1)
                                .ToArray();

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

    private readonly BmsGameplayTextEventController textEventController;

    private void triggerEvents()
    {
        textEventController.Update(Time.Current, gameplayEvents.RaiseText);
    }

    #endregion

    #region Public properties

    public int TotalColumns { get; }

    internal BmsHitObjectPoolPlan.ColumnSizes[] InitialPoolSizes { get; }

    internal int[] InitialHitExplosionSizes { get; }

    internal int[] InitialLongNoteHitExplosionSizes { get; }

    public BmsLayoutVariant LayoutVariant { get; }

    public BmsStage Stage { get; }

    public override Quad SkinnableComponentScreenSpaceDrawQuad => Stage.ScreenSpaceDrawQuad;

    internal void AddBehindStage(Drawable drawable) => AddInternal(drawable);

    public BmsTimingMap? TimingMap { get; }

    internal BmsGameplayScrollController ScrollController { get; }

    /// <summary>
    /// Applied only to predictable scrolling visuals so judgements remain based on <c>Time.Current</c>.
    /// </summary>
    internal BindableDouble VisualOffset { get; } = new();

    internal BindableDouble LongNoteTailVisualOffset { get; } = new();

    // Null identifies replays recorded before selectable algorithms were introduced.
    internal BmsJudgementAlgorithm? JudgementAlgorithm { get; set; } = BmsJudgementAlgorithm.Combo;

    // Live play finalises active holds at pause boundaries, while replay playback must retain
    // HCN frame history so arbitrary replay seeks can reconstruct the accumulator.
    internal bool PreserveLongNoteHistory { get; private set; }

    internal void SetReplayPlayback(bool replayPlayback) => PreserveLongNoteHistory = replayPlayback;

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

    private readonly List<(double Time, int Column, bool Pressed, bool ReverseScratch)> pendingInputs = [];
    private readonly int[] framePressedDirections;
    private readonly BmsColumn[] judgementColumns;

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

        pendingInputs.Add((Time.Current, column.Value, true, BmsKeyBindingConfiguration.IsReverseScratch(e.Action)));
        return true;
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

        pendingInputs.Add((Time.Current, column.Value, false, BmsKeyBindingConfiguration.IsReverseScratch(e.Action)));
    }

    private void processJudgementFrame(bool suppressJudgements = false)
    {
        foreach (var column in judgementColumns)
            framePressedDirections[column.Index] = column.PressedDirections;

        foreach (var input in pendingInputs)
        {
            var direction = Stage.Columns[input.Column].IsScratch && input.ReverseScratch ? 2 : 1;
            if (input.Pressed)
                framePressedDirections[input.Column] |= direction;
            else
                framePressedDirections[input.Column] &= ~direction;
        }

        if (suppressJudgements || (Clock as IGameplayClock)?.IsRewinding == true || Time.Elapsed < 0)
        {
            // Rewinding must keep the input state current without letting queued endpoints
            // or passing judgements mutate the attempt that the result stacks are restoring.
            foreach (var column in judgementColumns)
                column.PressedDirections = framePressedDirections[column.Index];
            pendingInputs.Clear();
            return;
        }

        // beatoraja scans all passing mines before all HCN bodies, then input endpoints,
        // then passive expiry. Gauge clipping makes each of these phase boundaries visible.
        // The complete input batch supplies final physical held state for mines and bodies;
        // successful tails only enable automatic body holding from the following frame.
        // Resume lead-in preserves passing judgements, while unjudged boundary notes
        // remain playable through the input phase below.
        if (!IsResumeRewinding)
        {
            foreach (var column in judgementColumns)
                ((BmsColumnHitObjectContainer)column.HitObjectContainer).UpdateLandmines(framePressedDirections[column.Index] != 0);

            foreach (var column in judgementColumns)
                ((BmsColumnHitObjectContainer)column.HitObjectContainer).UpdateHellChargeBodies(framePressedDirections[column.Index] != 0);
        }

        foreach (var input in pendingInputs)
        {
            var column = Stage.Columns[input.Column];
            if (input.Pressed)
            {
                var outcome = column.HandlePress(input.Time, input.ReverseScratch);
                if (outcome is { Kind: PressOutcomeKind.EmptyPoor, ExpectedTime: { } expectedTime })
                    registerEmptyPoor(expectedTime, outcome.Column);
            }
            else
                column.HandleRelease(input.Time, input.ReverseScratch);
        }

        pendingInputs.Clear();
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

    internal void ApplyLongNoteTailVisualOffsetToAllHitObjects()
    {
        foreach (var longNote in Beatmap.HitObjects.OfType<BmsLongNote>())
            ScrollController.ApplyLongNoteTailVisualOffset(longNote, LongNoteTailVisualOffset.Value);
    }

    #endregion

    #region Lifecycle

    private bool visualUpdatePending;

    internal bool HasPendingVisualUpdate => visualUpdatePending;

    internal void BeginGameplayFrame()
    {
        // Parent input is forwarded before FrameStabilityContainer advances our clock.
        // Flush that complete batch here so its original judgement time is preserved;
        // local devices and replay inputs are flushed later in UpdateSubTree.
        if (IsLoaded && pendingInputs.Count > 0)
            processJudgementFrame();

        skinCache.RefreshIdleSkins();
        visualUpdatePending = false;
        foreach (var column in Stage.Columns)
            ((BmsColumnHitObjectContainer)column.HitObjectContainer).BeginGameplayFrame();
    }

    internal void EndGameplayFrame()
    {
        foreach (var column in Stage.Columns)
            ((BmsColumnHitObjectContainer)column.HitObjectContainer).EndGameplayFrame(!visualUpdatePending);
        if (visualUpdatePending)
            base.UpdateSubTree();
        visualUpdatePending = false;
    }

    internal void CompleteLongNotesAtPause(double currentTime)
    {
        foreach (var column in judgementColumns)
            ((BmsColumnHitObjectContainer)column.HitObjectContainer).CompleteLongNotesAtPause(currentTime);
    }

    public override bool UpdateSubTreeMasking()
    {
        // Frame stability requests masking after every replay input. While visual traversal is
        // deferred, the final game-frame pass will mask the complete, current set of pulses.
        return !visualUpdatePending && base.UpdateSubTreeMasking();
    }

    public override bool UpdateSubTree()
    {
        if (IsLoaded)
            processJudgementFrame();

        var canDefer = IsLoaded;
        foreach (var column in Stage.Columns)
            canDefer &= ((BmsColumnHitObjectContainer)column.HitObjectContainer).CanDeferUpdate;
        if (canDefer)
        {
            updateScrollPosition();
            // Input is dispatched by our parent before this traversal. Holds still need passive
            // tails, HCN ticks and light pulses at every timestamp, without visiting future skins.
            foreach (var column in Stage.Columns)
                ((BmsColumnHitObjectContainer)column.HitObjectContainer).UpdateDeferredLongNotes();
            visualUpdatePending = true;
            return true;
        }

        visualUpdatePending = false;
        return base.UpdateSubTree();
    }

    private double resumeRewindInitialVisualOffset;

    private readonly List<(double Time, BmsLongNoteJudgementResult Result)> syntheticResults = [];

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
        processJudgementFrame(suppressJudgements: true);
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
        LongNoteTailVisualOffset.BindValueChanged(_ => ApplyLongNoteTailVisualOffsetToAllHitObjects());
        ApplyLongNoteTailVisualOffsetToAllHitObjects();
        RefreshAllLifetimes();
    }

    protected override void Update()
    {
        if (IsResumeRewindAnimating && Time.Elapsed > 0)
            resumeRewindAnimationElapsed += Time.Elapsed / Math.Max(Math.Abs(Clock.Rate), 0.01);

        updateScrollPosition();

        // Playfield.Update normally reverts results newer than the clock. During the resume lead-in,
        // those results belong to the completed attempt and must remain authoritative.
        if (!IsResumeRewinding)
        {
            if (Time.Elapsed < 0)
                scoreProcessor?.RewindEmptyPoors(Time.Current);

            while (syntheticResults.Count > 0 && syntheticResults[^1].Time > Time.Current)
            {
                var result = syntheticResults[^1].Result;
                syntheticResults.RemoveAt(syntheticResults.Count - 1);
                healthProcessor?.RevertResult(result);
                scoreProcessor?.RevertResult(result);
            }

            base.Update();
        }

        triggerEvents();
        updateStageScale();
    }

    private void updateScrollPosition()
    {
        var resumeRewindOffset = ComputeResumeRewindVisualOffset(resumeRewindInitialVisualOffset, resumeRewindAnimationElapsed);
        DisplayTime = ComputeDisplayTime(Time.Current, VisualOffset.Value, ScrollController.PlaybackRate, resumeRewindOffset);
        ScrollController.Update(DisplayTime);
    }

    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();
        if (Time.Elapsed < 0 && !IsResumeRewinding)
            healthProcessor?.Rewind(Time.Current);
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
        if (result is BmsJudgementResult { SuppressPenalty: true })
            return;
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

    internal void RegisterNonConsumingBad(double expectedTime, int column)
    {
        scoreProcessor?.RegisterNonConsumingJudgement(HitResult.Ok, Time.Current, expectedTime, column);
        healthProcessor?.RegisterNonConsumingJudgement(HitResult.Ok, Time.Current);
        requestJudgementDisplay(HitResult.Ok);
    }

    private void requestJudgementDisplay(HitResult result)
    {
        if (result is HitResult.IgnoreMiss or HitResult.IgnoreHit)
            return;

        if (result == HitResult.Meh)
            textEventController.TriggerMistake(gameplayEvents.RaiseText);

        gameplayEvents.RaiseJudgementDisplayed(result);
    }

    /// <summary>
    ///     Registers a separate CN/HCN endpoint through the score and health processors.
    /// </summary>
    public void ApplySyntheticLongNoteEndpoint(DrawableBmsHitObject drawable, BmsLongNoteEndpointResult endpoint)
    {
        if (drawable.HitObject is not BmsLongNote)
            return;

        var suppressPenalty = endpoint.Kind == BmsLongNoteEndpointKind.Head && endpoint.Result == HitResult.Meh
                                                                            && Stage.Columns[drawable.HitObject.Column] is BmsColumn sourceColumn && sourceColumn.HasPmsMistake(drawable.HitObject);
        var scoreResult = scoreProcessor?.ApplySyntheticLongNoteEndpoint(endpoint, suppressPenalty);

        if (scoreResult != null)
        {
            healthProcessor?.ApplySyntheticLongNoteEndpoint(scoreResult);
            // Synthetic endpoints have no drawable entry in the framework's rewind stack.
            syntheticResults.Add((Time.Current, scoreResult));
        }

        if (endpoint.Kind == BmsLongNoteEndpointKind.Tail && endpoint.Result.IsHit())
        {
            var column = Math.Clamp(drawable.HitObject.Column, 0, Stage.Columns.Length - 1);
            Stage.Columns[column].TriggerHitExplosion(drawable.HitObject is BmsLongNote);
        }

        if (!suppressPenalty)
            requestJudgementDisplay(endpoint.Result);
    }

    /// <summary>
    ///     Applies a HellChargeNote body gauge tick for the currently pressed column.
    /// </summary>
    public void ApplyHellChargeTick(bool holding, double scale = 0.5) => healthProcessor?.ApplyHellChargeTick(holding, scale, Time.Current);

    #endregion

}
