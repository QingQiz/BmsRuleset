using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Skinning.Embedded;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;
using osu.Game.Rulesets.BmsRuleset.UI.Components;
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
        NewResult -= onNewResult;
        parentSkin.SourceChanged -= updateEmbeddedSkinFallback;
        skinCache.Dispose();
        activeSkin.DisposeEmbeddedSkins();
        base.Dispose(isDisposing);
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

    public BmsTimingMap? TimingMap { get; }

    internal BmsScrollController ScrollController { get; }

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

        if (outcome == PressOutcome.EmptyPoor)
            registerEmptyPoor();

        return outcome == PressOutcome.Hit;
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

    internal void RefreshAllLifetimes()
    {
        var currentTime = IsLoaded ? Time.Current : (double?)null;

        foreach (var column in Stage.Columns)
        {
            if (column.HitObjectContainer is BmsColumnHitObjectContainer container)
                container.RefreshAllEntries(currentTime);
        }
    }

    #endregion

    #region Lifecycle

    [BackgroundDependencyLoader(true)]
    private void load()
    {
        ScrollController.SetHitTargetPosition(Stage.HitTargetPosition);

        parentSkin.SourceChanged += updateEmbeddedSkinFallback;
        updateEmbeddedSkinFallback();
    }

    protected override HitObjectLifetimeEntry CreateLifetimeEntry(HitObject hitObject) => new BmsHitObjectLifetimeEntry(hitObject, ScrollController);

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

        RefreshAllLifetimes();
    }

    protected override void Update()
    {
        ScrollController.Update(Time.Current);

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

    private void registerEmptyPoor()
    {
        scoreProcessor?.RegisterEmptyPoor();
        healthProcessor?.RegisterEmptyPoor();
        requestJudgementDisplay(HitResult.Miss);
    }

    private void requestJudgementDisplay(HitResult result)
    {
        if (result == HitResult.Meh && textEventManager.Mistake != null)
            gameplayEvents.RaiseText(textEventManager.Mistake);

        gameplayEvents.RaiseJudgementDisplayed(result);
    }

    /// <summary>
    ///     Registers an HCN head judgement that should not end the drawable yet.
    /// </summary>
    public void ApplyLongNoteHead(DrawableBmsHitObject drawable, double eventTime, HitResult result)
    {
        if (drawable.HitObject == null)
            return;

        var scoreResult = scoreProcessor?.ApplyLongNoteHead(drawable.HitObject, eventTime, result);

        if (scoreResult != null)
            healthProcessor?.ApplyLongNoteHead(scoreResult);

        requestJudgementDisplay(result);
    }

    /// <summary>
    ///     Registers a synthetic long-note endpoint (CN/HCN tail) through
    ///     the score and health processors, and triggers a visual hit explosion.
    /// </summary>
    public void ApplySyntheticLongNoteEndpoint(DrawableBmsHitObject drawable, double endpointTime, double eventTime, HitResult result)
    {
        if (drawable.HitObject is not BmsLongNote ln)
            return;

        var scoreResult = scoreProcessor?.ApplySyntheticLongNoteEndpoint(ln, endpointTime, eventTime, result);

        if (scoreResult != null)
            healthProcessor?.ApplySyntheticLongNoteEndpoint(scoreResult);

        if (result.IsHit())
        {
            var column = Math.Clamp(drawable.HitObject.Column, 0, Stage.Columns.Length - 1);
            Stage.Columns[column].TriggerHitExplosion(drawable.HitObject is BmsLongNote);
        }

        requestJudgementDisplay(result);
    }

    /// <summary>
    ///     Applies a HellChargeNote body gauge tick for the currently pressed column.
    /// </summary>
    public void ApplyHellChargeTick(bool holding, double scale = 0.5) => healthProcessor?.ApplyHellChargeTick(holding, scale);

    #endregion

}
