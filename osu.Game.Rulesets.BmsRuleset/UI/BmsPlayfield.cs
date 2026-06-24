using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
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
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
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
public sealed partial class BmsPlayfield : Playfield, IKeyBindingHandler<BmsAction>
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

        var hitObjectsOrdered = beatmap.HitObjects
            .OrderBy(h => h.StartTime).ThenBy(h => h.Column).ToArray();
        TotalColumns = Math.Max(1, beatmap.TotalColumns);
        LayoutVariant = beatmap.LayoutVariant;
        TimingMap = beatmap.TimingMap;

        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        RelativeSizeAxes = Axes.Both;

        Stage = new BmsStage(this);

        KeySoundPlayer = new BmsKeySoundPlayer(hitObjectsOrdered, Stage.Columns.Select(c => c.HitObjectContainer).ToArray(), () => Time.Current, TotalColumns);

        InternalChildren =
        [
            Stage,
            KeySoundPlayer,
        ];
    }

    #endregion

    #region Disposal

    protected override void Dispose(bool isDisposing)
    {
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

    public BmsKeySoundPlayer KeySoundPlayer { get; }

    private readonly BmsTextEventManager textEventManager;

    private void triggerEvents()
    {
        textEventManager.Update(Time.Current, BmsEventBus.OnTextEvent);
    }

    #endregion

    #region Public properties

    public int TotalColumns { get; }

    public BmsLayoutVariant LayoutVariant { get; }

    public BmsStage Stage { get; }

    public override Quad SkinnableComponentScreenSpaceDrawQuad => Stage.ScreenSpaceDrawQuad;

    public BmsTimingMap? TimingMap { get; }

    public bool ConstantScrollActive { get; set; }

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

    #endregion

    #region Input

    private readonly HashSet<int> pressedColumns = [];

    public bool OnPressed(KeyBindingPressEvent<BmsAction> e)
    {
        switch (e.Action)
        {
            case BmsAction.IncreaseScrollSpeed:
                AdjustScrollSpeed(1);
                return true;

            case BmsAction.DecreaseScrollSpeed:
                AdjustScrollSpeed(-1);
                return true;
        }

        var column = BmsKeyBindingConfiguration.ActionToColumn(e.Action, LayoutVariant);

        if (column == null || column.Value >= TotalColumns)
            return false;

        pressedColumns.Add(column.Value);

        var columnContainer = Stage.Columns[column.Value].HitObjectContainer;
        var candidates = new List<(DrawableBmsHitObject Drawable, BmsJudgementCandidate Candidate)>();

        foreach (var alive in columnContainer.AliveEntries.Values)
        {
            if (alive is not DrawableBmsHitObject d
                || d.Judged
                || d.HitObject.IsMine)
            {
                continue;
            }

            candidates.Add((d, new BmsJudgementCandidate(
                d.HitObject.StartTime,
                d.HitObject.EndTime,
                d.HitObject.Column,
                d.HitObject.BmsRank,
                d.HitObject.IsLongNote)));
        }

        var selection = BmsJudgementSelector.SelectPress(LayoutVariant, column.Value, candidates.Select(c => c.Candidate), Time.Current);

        if (selection.Candidate is { } selectedCandidate)
        {
            var target = candidates.First(c => c.Candidate.Equals(selectedCandidate)).Drawable;
            if (target.TryHit(selection.Result))
            {
                KeySoundPlayer.PlaySample(selectedCandidate.Column, target.HitObject.SamplePath);
                return true;
            }
        }

        KeySoundPlayer.PlayKeySound(column.Value);

        if (selection.IsEmptyPoor)
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
        var columnContainer = Stage.Columns[column.Value].HitObjectContainer;
        DrawableBmsHitObject? heldNote = null;

        foreach (var alive in columnContainer.AliveEntries.Values)
        {
            if (alive is not DrawableBmsHitObject d) continue;
            if (d is not ILongNoteHolder ln || !ln.IsHoldingLongNote)
                continue;

            if (heldNote == null || d.HitObject.EndTime < heldNote.HitObject.EndTime)
                heldNote = d;
        }

        if (heldNote is ILongNoteHolder ln2)
        {
            var tailTable = BmsJudgementProfileProvider.GetTable(LayoutVariant, heldNote.HitObject.Column, heldNote.HitObject.BmsRank, tail: true);
            var releaseOffset = Time.Current - heldNote.HitObject.EndTime;

            if (ln2.TryRelease(releaseOffset, tailTable))
            {
                KeySoundPlayer.PlaySample(column.Value, heldNote.HitObject.TailSamplePath);
            }
        }
    }

    public bool IsColumnPressedForLandmine(int column) => pressedColumns.Contains(column);

    public void DetonateLandmine(BmsHitObject hitObject)
    {
        if (string.IsNullOrEmpty(hitObject.LandmineExplosionSamplePath))
            return;

        KeySoundPlayer.PlayLandmineSound(hitObject.LandmineExplosionSamplePath);
    }

    #endregion

    #region Scroll Speed

    /// <summary>
    ///     Final applied scroll speed = <see cref="configuredScrollSpeed"/> × multiplier preset.
    /// </summary>
    public double ScrollSpeed { get; private set; } = default_scroll_speed;

    /// <summary>
    ///     The judgement line's current position on the scroll-coordinate axis.
    ///     In tick mode this advances at a rate proportional to the active BPM;
    ///     in constant-scroll mode this is simply Time.Current (linear).
    ///     When a note's scroll position equals this value, the note is at the
    ///     judgement line and its Y = <c>parentHeight - HitTargetPosition</c>.
    /// </summary>
    public double CurrentScrollPosition { get; private set; }

    /// <summary>
    ///     Active SPEED factor from the chart's <c>#SPEEDxx</c> / channel <c>SP</c> events.
    ///     Multiplies <see cref="ScrollSpeedMultiplier"/> like a user speed adjustment.
    ///     1.0 = normal, 2.0 = scroll advances 2× faster.
    /// </summary>
    public double ChartSpeedFactor { get; private set; } = 1.0;

    /// <summary>
    ///     Visible scroll window size in tick-based scroll units.
    ///     A note this far ahead of <see cref="CurrentScrollPosition"/> sits at
    ///     the top edge of the playfield (i.e. Y ≈ 0).
    /// </summary>
    public double ScrollRange => baseScrollRange * ScrollRangeScale;

    /// <summary>
    ///     Normalises the scroll range so the visual speed at the default scroll
    ///     speed matches osu!mania's baseline (computed once from the stage's
    ///     <see cref="BmsStage.HIT_TARGET_POSITION"/>).
    /// </summary>
    public double ScrollRangeScale { get; private set; }

    /// <summary>
    ///     Scroll range at default speed.  Visible window = this ÷ SpeedMultiplier.
    /// </summary>
    private static double baseScrollRange => BmsDrawableRuleset.ComputeScrollTime(default_scroll_speed);

    /// <summary>
    ///     Ratio of the current scroll speed to the default.
    ///     Scales the pixel-per-scroll-unit mapping in <see cref="YForScrollProgress"/>.
    /// </summary>
    /// <summary>
    ///     Effective scroll speed ratio = user speed × chart SPEED factor.
    /// </summary>
    public double ScrollSpeedMultiplier => ScrollSpeed / default_scroll_speed * ChartSpeedFactor;

    public double MeasureLineFutureWindow
    {
        get
        {
            var multiplier = Math.Abs(ScrollSpeedMultiplier);

            if (!double.IsFinite(multiplier) || multiplier < 0.001)
                return 30000;

            return Math.Max(500, ScrollRange / multiplier);
        }
    }

    /// <summary>
    ///     Converts a scroll progress value (distance from the judgement line in scroll
    ///     coordinate space) into a Y pixel position relative to the container's top.
    ///     The progress is zero when the object is at the judgement line, positive when
    ///     above it (yet to be hit), and negative when below (already past).
    /// </summary>
    /// <param name="progress">
    ///     Distance from the judgement line in scroll-coordinate space.
    ///     0 = at judgement line, positive = above (yet to hit), negative = below (past).
    /// </param>
    /// <param name="parentHeight">
    ///     Total pixel height of the column container. The returned Y is relative to this.
    /// </param>
    /// <param name="noteHeight">
    ///     Pixel height of the note. Subtracted so the note sits on rather than covers the judgement line.
    /// </param>
    public float YForScrollProgress(double progress, double parentHeight, double noteHeight = 0)
        => (float)(parentHeight - Stage.HitTargetPosition - progress * scrollCoordinateScale(parentHeight) - noteHeight);

    private double scrollCoordinateScale(double parentHeight)
    {
        var range = Math.Max(1.0, ScrollRange);
        var hitTarget = Stage.HitTargetPosition;
        var travelDistance = Math.Max(1f, (float)(parentHeight - hitTarget));
        return ScrollSpeedMultiplier / range * travelDistance;
    }

    /// <summary>
    ///     BMS default scroll speed (≈8.0ms of visible time per pixel at 1.0×).
    ///     Used as the normalisation denominator for <see cref="ScrollSpeedMultiplier"/>.
    /// </summary>
    private const double default_scroll_speed = BmsRulesetConfigManager.DEFAULT_SCROLL_SPEED;

    /// <summary>
    ///     In-game scroll speed multiplier presets cycled by
    ///     <see cref="AdjustScrollSpeed"/> via Up/Down keys.
    ///     Index 9 is the base 1.0× (configured speed).
    /// </summary>
    private static readonly double[] scroll_speed_multipliers =
    [
        0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0,
        1.25, 1.5, 1.75, 2.0, 2.25, 2.5, 2.75,
        3.0, 3.5, 4.0, 4.5, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0,
    ];

    private const int default_multiplier_index = 9; // 1.0×

    /// <summary>
    ///     Scroll speed configured in the settings (before the in-game multiplier preset is applied).
    /// </summary>
    private double configuredScrollSpeed = default_scroll_speed;

    /// <summary>
    ///     Index into <see cref="scroll_speed_multipliers"/> for the current in-game preset.
    /// </summary>
    private int currentMultiplierIndex = default_multiplier_index;

    public void SetConfiguredScrollSpeed(double speed)
    {
        configuredScrollSpeed = speed;
        setScrollSpeedFromMultiplierIndex(false);
    }

    /// <summary>
    ///     Cycles the scroll speed through <see cref="scroll_speed_multipliers"/>
    ///     presets relative to the configured base speed.
    ///     <paramref name="delta"/> is treated as direction (positive = faster, negative = slower).
    /// </summary>
    public void AdjustScrollSpeed(double delta)
    {
        var direction = delta > 0 ? 1 : -1;
        var newIndex = Math.Clamp(currentMultiplierIndex + direction, 0, scroll_speed_multipliers.Length - 1);

        if (newIndex != currentMultiplierIndex)
        {
            currentMultiplierIndex = newIndex;
            setScrollSpeedFromMultiplierIndex();
        }
    }

    private void setScrollSpeedFromMultiplierIndex(bool fireEvent = true)
    {
        ScrollSpeed = configuredScrollSpeed * scroll_speed_multipliers[currentMultiplierIndex];
        if (fireEvent)
            BmsEventBus.OnScrollSpeedChangeEvent(scroll_speed_multipliers[currentMultiplierIndex]);
        RefreshAllLifetimes();
    }

    internal void RefreshAllLifetimes()
    {
        foreach (var column in Stage.Columns)
        {
            if (column.HitObjectContainer is BmsColumnHitObjectContainer container)
                container.RefreshAllEntries();
        }
    }

    #endregion

    #region Lifecycle

    [BackgroundDependencyLoader(true)]
    private void load()
    {
        // Compute the mania-matching scroll-range scale once at load.
        // HitTargetPosition is constant per layout variant, so this never changes.
        const float reference_scroll_distance = 768f - 124.8f; // 768 - legacy DEFAULT_HIT_POSITION
        ScrollRangeScale = (768f - Stage.HitTargetPosition) / reference_scroll_distance;

        parentSkin.SourceChanged += updateEmbeddedSkinFallback;
        updateEmbeddedSkinFallback();
    }

    protected override HitObjectLifetimeEntry CreateLifetimeEntry(HitObject hitObject) => new BmsHitObjectLifetimeEntry(hitObject, this);

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
    }

    protected override void Update()
    {
        CurrentScrollPosition = ConstantScrollActive
            ? Time.Current
            : TimingMap?.GetScrollPositionAtTime(Time.Current) ?? Time.Current;

        ChartSpeedFactor = ConstantScrollActive
            ? 1.0
            : TimingMap?.GetSpeedFactorAtTime(Time.Current) ?? 1.0;

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

        Stage.MeasureLineArea.SetTimingMap(TimingMap, this, Stage);
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
            requestJudgementDisplay(HitResult.Meh);
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
            BmsEventBus.OnTextEvent(textEventManager.Mistake);

        BmsEventBus.OnJudgementDisplayEvent(result);
    }

    /// <summary>
    ///     Registers an HCN head judgement that should not end the drawable yet.
    /// </summary>
    internal void RegisterLongNoteHead(DrawableBmsHitObject drawable, double eventTime, HitResult result)
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
    internal void RegisterLongNoteEndpoint(DrawableBmsHitObject drawable, double endpointTime, double eventTime, HitResult result)
    {
        if (drawable.HitObject == null)
            return;

        var scoreResult = scoreProcessor?.ApplySyntheticLongNoteEndpoint(drawable.HitObject, endpointTime, eventTime, result);

        if (scoreResult != null)
            healthProcessor?.ApplySyntheticLongNoteEndpoint(scoreResult);

        if (result.IsHit())
        {
            var column = Math.Clamp(drawable.HitObject.Column, 0, Stage.Columns.Length - 1);
            Stage.Columns[column].HitExplosionArea.Add(new BmsHitExplosion(new BmsSkinComponentLookup(
                BmsSkinComponents.HitExplosion,
                LayoutVariant,
                column,
                drawable.HitObject.IsLongNote)));
        }

        requestJudgementDisplay(result);
    }

    /// <summary>
    ///     Whether the specified column is currently pressed.
    /// </summary>
    internal bool IsColumnPressed(int column) => pressedColumns.Contains(column);

    /// <summary>
    ///     Applies a HellChargeNote body gauge tick for the currently pressed column.
    /// </summary>
    internal void ApplyHellChargeTick(bool holding, double scale = 0.5) => healthProcessor?.ApplyHellChargeTick(holding, scale);

    #endregion

}
