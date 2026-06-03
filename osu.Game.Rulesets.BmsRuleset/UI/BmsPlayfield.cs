using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using osu.Game.Rulesets.BmsRuleset.Audio;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Skinning;
using osu.Game.Rulesets.BmsRuleset.UI.Components;
using osu.Game.Rulesets.Judgements;
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
public sealed partial class BmsPlayfield : Playfield, IKeyBindingHandler<BmsAction>
{

    #region Key-sound fields

    public BmsKeySoundPlayer KeySoundPlayer { get; }

    #endregion

    #region HUD fields

    private readonly BmsTextEventManager textEventManager = null!;

    #endregion

    #region Input fields

    private readonly HashSet<int> pressedColumns = [];

    #endregion

    #region Disposal

    protected override void Dispose(bool isDisposing)
    {
        NewResult -= onNewResult;
        parentSkin.SourceChanged -= updateEmbeddedSkinFallback;
        activeSkin.DisposeEmbeddedSkins();
        base.Dispose(isDisposing);
    }

    #endregion

    #region Skin

    private void updateEmbeddedSkinFallback()
    {
        if (beatmap == null)
        {
            activeSkin.SetSources(parentSkin, null, null);
            return;
        }

        var kind = BmsEmbeddedSkinSource.GetEmbeddedSkinKind(parentSkin.AllSources);
        var primary = new BmsLegacySkinTransformer(new BmsEmbeddedSkin(kind, host.Renderer, audio), beatmap);
        BmsLegacySkinTransformer? fallback = null;

        if (kind != BmsEmbeddedSkinKind.LegacyOld)
            fallback = new BmsLegacySkinTransformer(new BmsEmbeddedSkin(BmsEmbeddedSkinKind.LegacyOld, host.Renderer, audio), beatmap);

        activeSkin.SetSources(parentSkin, primary, fallback);
    }

    #endregion

    #region Constants

    private const double default_scroll_speed = BmsRulesetConfigManager.DEFAULT_SCROLL_SPEED;
    private const double min_scroll_speed = 1;
    private const double max_scroll_speed = BmsRulesetConfigManager.MAX_SCROLL_SPEED;
    private const double scroll_speed_delta = 1;

    private const float minimum_side_padding = 20;

    #endregion

    #region Public properties

    public int TotalColumns { get; }

    public BmsLayoutVariant LayoutVariant { get; }

    public BmsStage Stage { get; }

    public override Quad SkinnableComponentScreenSpaceDrawQuad => Stage.ScreenSpaceDrawQuad;

    public BmsTimingMap? TimingMap { get; }

    public double BaseScrollRange { get; private set; }

    public double ScrollSpeedMultiplier { get; private set; }

    public double TimeRange { get; private set; }

    public double ScrollSpeed { get; private set; } = default_scroll_speed;

    public double CurrentScrollPosition { get; private set; }

    public double ScrollRange { get; private set; }

    #endregion

    #region Judgement display fields

    private readonly Dictionary<HitResult, SkinnableDrawable> judgementDrawableCache = new();
    private readonly Container judgementDrawablePool;

    #endregion

    #region Skin / DI

    [Cached(typeof(ISkinSource))]
    private readonly BmsEmbeddedSkinSource activeSkin;

    private readonly BmsBeatmap? beatmap;

    private BmsHealthProcessor? healthProcessor => resolvedHealthProcessor as BmsHealthProcessor;

    private BmsScoreProcessor? scoreProcessor => resolvedScoreProcessor as BmsScoreProcessor;

    [Resolved(CanBeNull = true)]
    private HealthProcessor? resolvedHealthProcessor { get; set; }

    [Resolved(CanBeNull = true)]
    private ScoreProcessor? resolvedScoreProcessor { get; set; }

    [Resolved]
    private GameHost host { get; set; } = null!;

    [Resolved(CanBeNull = true)]
    private AudioManager? audio { get; set; }

    [Resolved]
    private ISkinSource parentSkin { get; set; } = null!;

    #endregion

    #region Construction

    /// <inheritdoc />
    /// <summary>
    ///     Creates a playfield from raw hit objects.  Objects are sorted by
    ///     start time then column.
    /// </summary>
    public BmsPlayfield(
        IReadOnlyList<BmsHitObject> hitObjects, int totalColumns, BmsLayoutVariant layoutVariant = BmsLayoutVariant.Bme7K,
        BmsTimingMap? timingMap = null
    )
    {
        activeSkin = new BmsEmbeddedSkinSource();

        IReadOnlyList<BmsHitObject> hitObjectsOrdered = hitObjects
            .OrderBy(h => h.StartTime).ThenBy(h => h.Column).ToArray();
        TotalColumns = Math.Max(1, totalColumns);
        LayoutVariant = layoutVariant;
        TimingMap = timingMap;

        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        RelativeSizeAxes = Axes.Both;

        judgementDrawablePool = new Container { Alpha = 0, RelativeSizeAxes = Axes.Both };

        Stage = new BmsStage(TotalColumns, LayoutVariant);

        KeySoundPlayer = new BmsKeySoundPlayer(hitObjectsOrdered, HitObjectContainer, () => Time.Current, TotalColumns);

        InternalChildren =
        [
            Stage,
            HitObjectContainer,
            KeySoundPlayer,
            judgementDrawablePool,
        ];
    }

    /// <inheritdoc />
    /// <summary>
    ///     Creates a playfield from a decoded <see cref="T:osu.Game.Rulesets.BmsRuleset.Beatmaps.BmsBeatmap">BmsBeatmap</see>.
    /// </summary>
    public BmsPlayfield(BmsBeatmap beatmap)
        : this(beatmap.HitObjects, beatmap.TotalColumns, beatmap.LayoutVariant, beatmap.TimingMap)
    {
        this.beatmap = beatmap;
        textEventManager = new BmsTextEventManager(beatmap.TextEvents);
    }

    #endregion

    #region Input

    public bool OnPressed(KeyBindingPressEvent<BmsAction> e)
    {
        switch (e.Action)
        {
            case BmsAction.IncreaseScrollSpeed:
                AdjustScrollSpeed(scroll_speed_delta);
                return true;

            case BmsAction.DecreaseScrollSpeed:
                AdjustScrollSpeed(-scroll_speed_delta);
                return true;
        }

        var column = BmsKeyBindingConfiguration.ActionToColumn(e.Action, LayoutVariant);

        if (column == null || column.Value >= TotalColumns)
            return false;

        pressedColumns.Add(column.Value);
        KeySoundPlayer.PlayKeySound(column.Value);

        // Pass 1: find a hittable note — earliest unjudged note whose timing falls within
        // a judgement window (PGREAT … BAD, or the POOR hit zone).  Picking by StartTime
        // ensures strict sequential ordering within a column.
        var target = HitObjectContainer.AliveObjects
            .OfType<DrawableBmsHitObject>()
            .Where(d => !d.Judged &&
                        !d.HitObject.IsMine &&
                        d.HitObject.Column == column.Value &&
                        d.HitObject.HitWindows is BmsHitWindows w &&
                        w.BmsResultFor(Time.Current - d.HitObject.StartTime) != HitResult.None)
            .MinBy(d => d.HitObject.StartTime);

        if (target?.TryHit() == true)
            return true;

        // Pass 2: no note was consumed — check whether the key press falls in the E-POOR
        // zone (outside the BAD window but within the EP boundary) of the nearest note.
        // If so, register an E-POOR; otherwise silently ignore (too early / too late).
        var nearestUnjudged = HitObjectContainer.AliveObjects
            .OfType<DrawableBmsHitObject>()
            .Where(d => !d.Judged &&
                        !d.HitObject.IsMine &&
                        d.HitObject.Column == column.Value &&
                        d.HitObject.HitWindows is BmsHitWindows)
            .MinBy(d => Math.Abs(Time.Current - d.HitObject.StartTime));

        if (nearestUnjudged?.HitObject.HitWindows is BmsHitWindows epoWindows &&
            epoWindows.IsEpoZone(Time.Current - nearestUnjudged.HitObject.StartTime))
        {
            registerEmptyPoor();
        }

        return false;
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

        if (!pressedColumns.Contains(column.Value))
            return;

        pressedColumns.Remove(column.Value);

        // Release: find the earliest held LN in this column and let it judge the key-up.
        // We must include LNs released before the tail window (an early release is a drop,
        // scored as POOR) — filtering by the release window here would leave the note
        // frozen at the judgement line until its tail time passed.
        HitObjectContainer.AliveObjects
            .OfType<DrawableBmsHitObject>()
            .Where(d => d.IsHoldingLongNote && d.HitObject.Column == column.Value)
            .MinBy(d => d.HitObject.EndTime)
            ?.TryRelease();
    }

    public bool IsColumnPressedForLandmine(int column) => pressedColumns.Contains(column);

    public void DetonateLandmine(BmsHitObject hitObject)
    {
        if (string.IsNullOrEmpty(hitObject.LandmineExplosionSamplePath))
            return;

        KeySoundPlayer.PlayLandmineSound(hitObject.LandmineExplosionSamplePath);
    }

    #endregion

    #region HUD

    public void SetScrollSpeed(double scrollSpeed)
    {
        ScrollSpeed = Math.Clamp(scrollSpeed, min_scroll_speed, max_scroll_speed);
        recalculateSpeedFields();
        BmsEventBus.OnScrollSpeedChangeEvent(scrollSpeed);
    }

    public void SetConfiguredScrollSpeed(double speed)
    {
        BmsPlayerShared.ConfiguredScrollSpeed = speed;
        ScrollSpeed = speed;
        recalculateSpeedFields();
    }

    public void AdjustScrollSpeed(double delta) => SetScrollSpeed(ScrollSpeed + delta);

    private void recalculateSpeedFields()
    {
        BaseScrollRange = BmsDrawableRuleset.ComputeScrollTime(default_scroll_speed);
        ScrollSpeedMultiplier = ScrollSpeed / default_scroll_speed;
        TimeRange = BaseScrollRange / ScrollSpeedMultiplier;
        ScrollRange = BaseScrollRange;
    }

    private void updateHud()
    {
        textEventManager.Update(Time.Current, BmsEventBus.OnTextEvent);
    }

    #endregion

    #region Lifecycle

    [BackgroundDependencyLoader(true)]
    private void load()
    {
        recalculateSpeedFields();

        RegisterPool<BmsHitObject, DrawableBmsHitObject>(32, 512);

        parentSkin.SourceChanged += updateEmbeddedSkinFallback;
        updateEmbeddedSkinFallback();
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        populateMeasureLines();

        NewResult += onNewResult;

        foreach (var result in BmsRuleset.STATIC_VALID_HIT_RESULTS)
        {
            var drawable = new SkinnableDrawable(
                new SkinComponentLookup<HitResult>(result))
            {
                RelativeSizeAxes = Axes.None,
                AutoSizeAxes = Axes.Both,
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
            };

            judgementDrawableCache[result] = drawable;
            judgementDrawablePool.Add(drawable);
        }
    }

    protected override void Update()
    {
        base.Update();

        CurrentScrollPosition = TimingMap?.GetScrollPositionAtTime(Time.Current) ?? Time.Current;
        ScrollRange = BaseScrollRange;

        updateHud();

        updateStageScale();
    }

    #endregion

    #region Layout

    private void populateMeasureLines()
    {
        if (TimingMap == null)
            return;

        Stage.MeasureLineArea.Clear();

        foreach (var measure in TimingMap.Measures.Where(m => m.Index > 0))
            Stage.MeasureLineArea.Add(new BmsMeasureLine(measure.StartTick, TimingMap, this, Stage));
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

        if (bmsHitObject.HitObject.IsMine)
        {
            showJudgement(HitResult.Meh);
            return;
        }

        if (result.IsHit)
        {
            var column = Math.Clamp(bmsHitObject.HitObject.Column, 0, Stage.Columns.Length - 1);
            Stage.Columns[column].HitExplosionArea.Add(new BmsHitExplosion(new BmsSkinComponentLookup(
                BmsSkinComponents.HitExplosion,
                LayoutVariant,
                column,
                bmsHitObject.HitObject.IsLongNote)));
        }

        showJudgement(result.Type);
    }

    private void registerEmptyPoor()
    {
        scoreProcessor?.RegisterEmptyPoor();
        healthProcessor?.RegisterEmptyPoor();
        showJudgement(HitResult.Miss);
    }

    /// <summary>
    ///     Moves the pre-built <see cref="SkinnableDrawable"/> for <paramref name="result"/> from
    ///     the hidden pool container into <see cref="BmsStage.JudgementArea"/> and replays its
    ///     animation.  Any previously shown drawable is returned to the pool container so it stays
    ///     loaded and ready for the next use.
    /// </summary>
    private void showJudgement(HitResult result)
    {
        if (!judgementDrawableCache.TryGetValue(result, out var drawable))
            return;

        if (result == HitResult.Meh && textEventManager.Mistake != null)
        {
            BmsEventBus.OnTextEvent(textEventManager.Mistake);
        }

        var evicted = Stage.JudgementArea.ToArray();
        Stage.JudgementArea.Clear(false);

        foreach (var child in evicted)
            judgementDrawablePool.Add(child);

        // Move the cached drawable into the display area and replay its animation.
        judgementDrawablePool.Remove(drawable, false);
        Stage.JudgementArea.Add(drawable);

        if (drawable.Drawable is IAnimatableJudgement animatable)
        {
            drawable.ResetAnimation();
            animatable.PlayAnimation();
        }
    }

    #endregion

}
