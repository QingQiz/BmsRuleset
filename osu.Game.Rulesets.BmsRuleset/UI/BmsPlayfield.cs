using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using osu.Game.Audio;
using osu.Game.Graphics;
using osu.Game.Rulesets.BmsRuleset.Audio;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Skinning;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI;

/// <inheritdoc cref="Playfield" />
/// <summary>
///     Native BMS playfield.  Manages the stage, hit-object container, key-sound playback,
///     judgement display, scroll-speed HUD, and input routing for all BMS layout variants.
/// </summary>
public sealed partial class BmsPlayfield : Playfield, IKeyBindingHandler<BmsAction>
{

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

    private const float health_display_gap = 24;
    private const float minimum_side_padding = 20;

    #endregion

    #region Public properties

    public int TotalColumns { get; }

    public BmsLayoutVariant LayoutVariant { get; }

    public BmsStage Stage { get; }

    public bool IsAutoplay { get; }

    public override Quad SkinnableComponentScreenSpaceDrawQuad => Stage.ScreenSpaceDrawQuad;

    public BmsTimingMap? TimingMap { get; }

    public double BaseScrollRange { get; private set; }

    public double ScrollSpeedMultiplier { get; private set; }

    public double TimeRange { get; private set; }

    public double ScrollSpeed { get; private set; } = default_scroll_speed;

    public double CurrentScrollPosition { get; private set; }

    public double ScrollRange { get; private set; }

    #endregion

    #region Scroll speed fields

    private readonly BindableDouble configuredScrollSpeed = new(default_scroll_speed);

    private readonly Container scrollSpeedHud;
    private SpriteText scrollSpeedText = null!;
    private SpriteText scrollSpeedArrow = null!;

    #endregion

    #region Judgement display fields

    private readonly Dictionary<HitResult, SkinnableDrawable> judgementDrawableCache = new();
    private readonly Container judgementDrawablePool;

    #endregion

    #region Key-sound fields

    private readonly BmsChartSampleSound keySound = new();
    private readonly BmsChartSampleSound landmineSound = new();
    private BmsKeySoundPlayer keySoundPlayer = null!;

    #endregion

    #region Input fields

    private readonly HashSet<int> pressedColumns = [];
    private readonly IBindable<bool> samplePlaybackDisabled = new Bindable<bool>();

    #endregion

    #region Mod fields

    public bool IsAutoScratch { get; }

    public bool HideScratch { get; }

    private readonly HashSet<DrawableBmsHitObject> autoScratchLnHeads = [];

    #endregion

    #region Skin / DI

    [Cached(typeof(ISkinSource))]
    private readonly BmsEmbeddedSkinSource activeSkin;

    private readonly IReadOnlyList<BmsHitObject> hitObjects;
    private readonly BmsBeatmap? beatmap;

    private BmsHealthDisplay? healthDisplay;

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
        bool isAutoplay = false, BmsTimingMap? timingMap = null, bool isAutoScratch = false, bool hideScratch = false
    )
    {
        activeSkin = new BmsEmbeddedSkinSource();

        this.hitObjects = hitObjects.OrderBy(h => h.StartTime).ThenBy(h => h.Column).ToArray();
        TotalColumns = Math.Max(1, totalColumns);
        LayoutVariant = layoutVariant;
        IsAutoplay = isAutoplay;
        IsAutoScratch = isAutoScratch;
        HideScratch = hideScratch;
        TimingMap = timingMap;

        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        RelativeSizeAxes = Axes.Both;

        scrollSpeedHud = createScrollSpeedHud();
        judgementDrawablePool = new Container { Alpha = 0, RelativeSizeAxes = Axes.Both };

        InternalChildren =
        [
            Stage = new BmsStage(TotalColumns, LayoutVariant, HideScratch),
            HitObjectContainer,
            keySound,
            landmineSound,
            scrollSpeedHud,
            judgementDrawablePool,
        ];
    }

    /// <inheritdoc />
    /// <summary>
    ///     Creates a playfield from a decoded <see cref="T:osu.Game.Rulesets.BmsRuleset.Beatmaps.BmsBeatmap">BmsBeatmap</see>.
    /// </summary>
    public BmsPlayfield(BmsBeatmap beatmap, bool isAutoplay = false, bool isAutoScratch = false, bool hideScratch = false)
        : this(beatmap.HitObjects, beatmap.TotalColumns, beatmap.LayoutVariant, isAutoplay, beatmap.TimingMap, isAutoScratch, hideScratch)
    {
        this.beatmap = beatmap;
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

        if (IsAutoScratch && BmsSkinComponentLookup.IsScratchColumn(column.Value, LayoutVariant))
            return false;

        pressedColumns.Add(column.Value);
        keySoundPlayer.PlayKeySound(column.Value);

        // Use the earliest unjudged note in this column that is within a hit window.
        // Picking by StartTime (not by distance) ensures strict sequential ordering:
        // a later note can never be hit before an earlier one in the same column.
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

        if (!IsAutoplay)
            registerEmptyPoor();

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

        if (IsAutoScratch && BmsSkinComponentLookup.IsScratchColumn(column.Value, LayoutVariant))
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

        keySoundPlayer.PlayLandmineSound(hitObject.LandmineExplosionSamplePath);
    }

    private void processAutoScratch()
    {
        if (!IsAutoScratch)
            return;

        var now = Time.Current;

        autoScratchLnHeads.RemoveWhere(d => d.Judged);

        foreach (var drawable in HitObjectContainer.AliveObjects.OfType<DrawableBmsHitObject>())
        {
            if (drawable.Judged || drawable.HitObject.IsMine)
                continue;

            if (!BmsSkinComponentLookup.IsScratchColumn(drawable.HitObject.Column, LayoutVariant))
                continue;

            var note = drawable.HitObject;

            if (note.IsLongNote)
            {
                // not press ln head yet
                if (!autoScratchLnHeads.Contains(drawable))
                {
                    if (now >= note.StartTime)
                    {
                        playScratchSample(note);
                        if (drawable.TryHit())
                            autoScratchLnHeads.Add(drawable);
                    }
                }
                else if (now >= note.EndTime)
                {
                    drawable.TryRelease();
                }
            }
            else
            {
                if (now >= note.StartTime)
                {
                    playScratchSample(note);
                    drawable.TryHit();
                }
            }
        }

        return;

        void playScratchSample(BmsHitObject note)
        {
            if (string.IsNullOrEmpty(note.SamplePath))
                return;

            keySound.SampleInfo = new BmsSampleInfo(note.SamplePath);
            keySound.Play();
        }
    }

    #endregion

    #region Scroll speed

    public void SetScrollSpeed(double scrollSpeed)
    {
        ScrollSpeed = Math.Clamp(scrollSpeed, min_scroll_speed, max_scroll_speed);
        recalculateSpeedFields();
        showScrollSpeedText();
    }

    public void SetConfiguredScrollSpeed(double speed)
    {
        configuredScrollSpeed.Value = speed;
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

    #endregion

    #region Lifecycle

    [BackgroundDependencyLoader(true)]
    private void load(ISamplePlaybackDisabler? samplePlaybackDisabler)
    {
        recalculateSpeedFields();

        keySoundPlayer = new BmsKeySoundPlayer(hitObjects, HitObjectContainer, () => Time.Current, samplePlaybackDisabled, keySound, landmineSound);

        RegisterPool<BmsHitObject, DrawableBmsHitObject>(32, 512);

        if (healthProcessor != null)
        {
            AddInternal(healthDisplay = new BmsHealthDisplay
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopCentre,
            });
        }

        if (samplePlaybackDisabler != null)
            samplePlaybackDisabled.BindTo(samplePlaybackDisabler.SamplePlaybackDisabled);

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

        processAutoScratch();

        updateStageScale();
        updateHealthDisplayLayout();
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

        var healthReserve = Math.Max(64, (healthDisplay?.IsLoaded == true ? healthDisplay.DrawWidth : 0) + health_display_gap + minimum_side_padding);
        var availableWidth = Math.Max(1, DrawWidth - healthReserve * 2);
        var scale = Math.Min(1, availableWidth / Stage.DrawWidth);

        if (float.IsFinite(scale) && scale > 0)
            Stage.Scale = new Vector2(scale, 1);
    }

    private void updateHealthDisplayLayout()
    {
        if (!Stage.IsLoaded || healthDisplay?.IsLoaded != true)
            return;

        var stageQuad = Stage.ScreenSpaceDrawQuad;

        if (!isFinite(stageQuad.TopRight) || !isFinite(stageQuad.BottomRight))
            return;

        var stageTopRight = ToLocalSpace(stageQuad.TopRight);
        var stageHeight = (stageQuad.BottomRight - stageQuad.TopRight).Length;
        var healthScale = Math.Min(1, stageHeight / Math.Max(1, healthDisplay.DrawHeight));
        var position = new Vector2(stageTopRight.X + health_display_gap, stageTopRight.Y);

        if (!isFinite(stageTopRight) || !float.IsFinite(stageHeight) ||
            stageHeight <= 0 || !float.IsFinite(healthScale) ||
            healthScale <= 0 || !isFinite(position))
            return;

        healthDisplay.Scale = new Vector2(healthScale);
        healthDisplay.Position = position;
    }

    private static bool isFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);

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

    #region Scroll speed HUD

    private Container createScrollSpeedHud() => new()
    {
        Anchor = Anchor.TopCentre,
        Origin = Anchor.TopCentre,
        Y = 36,
        AutoSizeAxes = Axes.Both,
        Alpha = 0,
        Children =
        [
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.Black.Opacity(0.55f),
            },
            new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Padding = new MarginPadding { Horizontal = 10, Vertical = 4 },
                Children =
                [
                    scrollSpeedArrow = new SpriteText
                    {
                        Font = OsuFont.Default.With(size: 24, weight: FontWeight.Bold),
                        Colour = Color4.White,
                    },
                    scrollSpeedText = new SpriteText
                    {
                        Font = OsuFont.Default.With(size: 24, weight: FontWeight.Bold),
                        Colour = Color4.White,
                    },
                ],
            },
        ],
    };

    private void showScrollSpeedText()
    {
        var configured = configuredScrollSpeed.Value;
        var delta = ScrollSpeed - configured;
        var colour = delta > 0 ? new Color4(255, 200, 0, 255)
            : delta < 0 ? new Color4(100, 180, 255, 255)
            : Color4.White;

        scrollSpeedArrow.Text = delta > 0 ? ">>" : delta < 0 ? "<<" : "";
        scrollSpeedArrow.Colour = colour;
        scrollSpeedText.Text = $"{ScrollSpeed:0.0}";
        scrollSpeedText.Colour = colour;
        scrollSpeedHud.ClearTransforms();
        scrollSpeedHud.FadeIn(80).Delay(1000).FadeOut(300);
    }

    #endregion

}
