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
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Embedded;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Runtime;
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
public sealed partial class BmsPlayfield : Playfield, IKeyBindingHandler<BmsAction>
{

    #region Constants

    private const float minimum_side_padding = 20;

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
        ScrollController.InitializeVisibility(beatmap);
        ScrollController.ScrollSpeedChanged += onScrollSpeedChanged;

        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        RelativeSizeAxes = Axes.Both;

        Stage = new BmsStage(this);
        Stage.SkinHitTargetPositionChanged += onSkinHitTargetPositionChanged;
        var judgementColumns = Stage.Columns.Cast<BmsColumn>().ToArray();
        var judgementObjects = Beatmap.HitObjects.ToArray();
        judgementEngine = new BmsJudgementEngine(judgementColumns.Select(column => new BmsJudgementColumnDefinition(
            judgementObjects, LayoutVariant, column.Index, column.JudgementBridge = new BmsJudgementBridge(this, column))).ToArray());
        foreach (var column in judgementColumns)
        {
            column.Judgements = judgementEngine.GetColumn(column.Index);
            var container = (BmsColumnHitObjectContainer)column.HitObjectContainer;
            container.Judgements = column.Judgements;
            container.JudgementBridge = column.JudgementBridge;
        }

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

    // Replay seeks need HCN history; live play only advances the current state.
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

    private readonly BmsJudgementEngine judgementEngine;

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

        judgementEngine.QueueInput(Time.Current, column.Value, true, BmsKeyBindingConfiguration.IsReverseScratch(e.Action));
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

        judgementEngine.QueueInput(Time.Current, column.Value, false, BmsKeyBindingConfiguration.IsReverseScratch(e.Action));
    }

    private void processJudgementFrame()
        => judgementEngine.ProcessFrame(new BmsFrameContext(Time.Current, (Clock as IGameplayClock)?.GetTrueGameplayRate() ?? Clock.Rate,
            (Clock as IGameplayClock)?.IsRewinding == true), Time.Elapsed, JudgementAlgorithm);

    internal void CompleteAtEnd(double time)
    {
        processJudgementFrame();
        judgementEngine.CompleteLongNotesAtEnd(new BmsFrameContext(time,
            (Clock as IGameplayClock)?.GetTrueGameplayRate() ?? Clock.Rate));
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

    internal bool HasPendingVisualUpdate { get; private set; }

    internal void BeginGameplayFrame()
    {
        // Parent input is forwarded before FrameStabilityContainer advances our clock.
        // Flush that complete batch here so its original judgement time is preserved;
        // local devices and replay inputs are flushed later in UpdateSubTree.
        if (IsLoaded && judgementEngine.HasPendingInputs)
            processJudgementFrame();

        skinCache.RefreshIdleSkins();
        HasPendingVisualUpdate = false;
        foreach (var column in Stage.Columns)
            ((BmsColumnHitObjectContainer)column.HitObjectContainer).BeginGameplayFrame();
    }

    internal void EndGameplayFrame()
    {
        foreach (var column in Stage.Columns)
            ((BmsColumnHitObjectContainer)column.HitObjectContainer).EndGameplayFrame(!HasPendingVisualUpdate);
        if (HasPendingVisualUpdate)
            base.UpdateSubTree();
        HasPendingVisualUpdate = false;
    }

    // Frame stability requests masking after every replay input. While visual traversal is
    // deferred, the final game-frame pass will mask the complete, current set of pulses.
    public override bool UpdateSubTreeMasking() => !HasPendingVisualUpdate && base.UpdateSubTreeMasking();

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
                ((BmsColumnHitObjectContainer)column.HitObjectContainer).UpdateDeferredFrame();
            HasPendingVisualUpdate = true;
            return true;
        }

        HasPendingVisualUpdate = false;
        return base.UpdateSubTree();
    }

    private readonly List<(double Time, BmsLongNoteJudgementResult Result)> syntheticResults = [];

    internal double DisplayTime { get; private set; }

    internal static double ComputeDisplayTime(double currentTime, double visualOffset, double playbackRate) =>
        currentTime + visualOffset * playbackRate;

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
        updateScrollPosition();

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

        triggerEvents();
        updateStageScale();
    }

    private void updateScrollPosition()
    {
        DisplayTime = ComputeDisplayTime(Time.Current, VisualOffset.Value, ScrollController.PlaybackRate);
        ScrollController.Update(DisplayTime);
    }

    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();
        if (Time.Elapsed < 0)
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
        {
            var stageScale = new Vector2(scale, 1);
            if (Stage.Scale != stageScale)
                Stage.Scale = stageScale;
        }
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

    internal void RegisterEmptyPoor(double expectedTime, int column, double? eventTime = null)
    {
        scoreProcessor?.RegisterEmptyPoor(eventTime ?? Time.Current, expectedTime, column);
        healthProcessor?.RegisterEmptyPoor(eventTime ?? Time.Current);
        requestJudgementDisplay(HitResult.Miss);
    }

    internal void RegisterNonConsumingBad(double expectedTime, int column, double? eventTime = null)
    {
        scoreProcessor?.RegisterNonConsumingJudgement(HitResult.Ok, eventTime ?? Time.Current, expectedTime, column);
        healthProcessor?.RegisterNonConsumingJudgement(HitResult.Ok, eventTime ?? Time.Current);
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
    internal void ApplySyntheticLongNoteEndpoint(BmsLongNoteEndpointResult endpoint, bool suppressPenalty)
    {
        var note = endpoint.Source;
        var scoreResult = scoreProcessor?.ApplySyntheticLongNoteEndpoint(endpoint, suppressPenalty);

        if (scoreResult != null)
        {
            healthProcessor?.ApplySyntheticLongNoteEndpoint(scoreResult);
            // Synthetic endpoints have no drawable entry in the framework's rewind stack.
            syntheticResults.Add((endpoint.ApplicationTime ?? endpoint.EventTime, scoreResult));
        }

        if (endpoint.Kind == BmsLongNoteEndpointKind.Tail && endpoint.Result.IsHit())
        {
            var column = Math.Clamp(note.Column, 0, Stage.Columns.Length - 1);
            Stage.Columns[column].TriggerHitExplosion(BmsHitLightStyle.Hold);
        }

        if (!suppressPenalty)
            requestJudgementDisplay(endpoint.Result);
    }

    /// <summary>
    ///     Applies a HellChargeNote body gauge tick for the currently pressed column.
    /// </summary>
    public void ApplyHellChargeTick(bool holding, double scale = 0.5, double? eventTime = null) => healthProcessor?.ApplyHellChargeTick(holding, scale, eventTime ?? Time.Current);

    #endregion

}
