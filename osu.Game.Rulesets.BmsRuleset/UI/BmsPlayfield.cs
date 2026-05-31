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

/// <summary>
///     First native BMS playfield.
/// </summary>
/// <remarks>
///     This is intentionally a simple vertical lane field, not a copied mania stage. It provides the
///     minimum object pooling and layout surface needed to remove the mania dependency. Later phases
///     will split this into BMS columns, BGA layers, key beams, and native scroll timing.
/// </remarks>
public sealed partial class BmsPlayfield : Playfield, IKeyBindingHandler<BmsAction>
{

    public int TotalColumns { get; }

    public BmsLayoutVariant LayoutVariant { get; }

    public BmsStage Stage { get; }

    public bool IsAutoplay { get; }

    public override Quad SkinnableComponentScreenSpaceDrawQuad => Stage.ScreenSpaceDrawQuad;

    public double ConfiguredScrollSpeed => configuredScrollSpeed.Value;

    public BmsTimingMap? TimingMap { get; }

    public double BaseScrollRange { get; private set; }

    public double ScrollSpeedMultiplier { get; private set; }

    public double TimeRange { get; private set; }

    public double ScrollSpeed { get; private set; } = default_scroll_speed;

    public double CurrentScrollPosition { get; private set; }

    public double ScrollRange { get; private set; }

    private const double default_scroll_speed = BmsRulesetConfigManager.DEFAULT_SCROLL_SPEED;
    private const double min_scroll_speed = 1;
    private const double max_scroll_speed = BmsRulesetConfigManager.MAX_SCROLL_SPEED;
    private const double scroll_speed_delta = 1;

    private readonly BindableDouble configuredScrollSpeed = new(default_scroll_speed);

    private const float health_display_gap = 24;
    private const float minimum_side_padding = 20;

    private readonly IReadOnlyList<BmsHitObject> hitObjects;
    private readonly BmsBeatmap? beatmap;

    private readonly Dictionary<int, int> nextSoundIndexByColumn = new();
    private readonly Dictionary<int, double> lastSoundSearchTimeByColumn = new();
    private readonly HashSet<int> pressedColumns = [];

    private readonly BmsChartSampleSound keySound = new();
    private readonly BmsChartSampleSound landmineSound = new();

    // Pre-built SkinnableDrawable per HitResult — created once at load, reused on every judgement
    // display by removing from the pool container and adding to JudgementArea, then restoring on
    // the next clear. This avoids a full skin lookup + child construction on every hit.
    private readonly Dictionary<HitResult, SkinnableDrawable> judgementDrawableCache = new();

    private readonly Container scrollSpeedHud;
    private readonly SpriteText scrollSpeedText;
    private readonly SpriteText scrollSpeedArrow;

    // Off-screen container that keeps cached drawables loaded when not shown in JudgementArea.
    private readonly Container judgementDrawablePool;

    private readonly IBindable<bool> samplePlaybackDisabled = new Bindable<bool>();

    [Cached(typeof(ISkinSource))]
    private readonly BmsEmbeddedSkinSource activeSkin;

    private BmsHealthProcessor? healthProcessor => resolvedHealthProcessor as BmsHealthProcessor;

    private BmsScoreProcessor? scoreProcessor => resolvedScoreProcessor as BmsScoreProcessor;

    private BmsHealthDisplay? healthDisplay;

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

    public BmsPlayfield(IReadOnlyList<BmsHitObject> hitObjects, int totalColumns, BmsLayoutVariant layoutVariant = BmsLayoutVariant.Bme7K, bool isAutoplay = false, BmsTimingMap? timingMap = null)
    {
        activeSkin = new BmsEmbeddedSkinSource();

        this.hitObjects = hitObjects.OrderBy(h => h.StartTime).ThenBy(h => h.Column).ToArray();
        TotalColumns = Math.Max(1, totalColumns);
        LayoutVariant = layoutVariant;
        IsAutoplay = isAutoplay;
        TimingMap = timingMap;

        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        RelativeSizeAxes = Axes.Both;

        InternalChildren =
        [
            Stage = new BmsStage(TotalColumns, LayoutVariant),
            HitObjectContainer,
            keySound,
            landmineSound,
            scrollSpeedHud = new Container
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
            },
            judgementDrawablePool = new Container { Alpha = 0, RelativeSizeAxes = Axes.Both },
        ];
    }

    public BmsPlayfield(BmsBeatmap beatmap, bool isAutoplay = false)
        : this(beatmap.HitObjects, beatmap.TotalColumns, beatmap.LayoutVariant, isAutoplay, beatmap.TimingMap)
    {
        this.beatmap = beatmap;
    }

    #region Disposal

    protected override void Dispose(bool isDisposing)
    {
        NewResult -= onNewResult;
        parentSkin.SourceChanged -= updateEmbeddedSkinFallback;
        activeSkin.DisposeEmbeddedSkins();
        base.Dispose(isDisposing);
    }

    #endregion

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
        playNextKeySound(column.Value);

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

        // No note was consumed. Check whether this press falls inside the Empty POOR zone:
        // a press earlier than −early_poor_window ms before every unjudged note in this
        // column (or when there are no remaining notes at all).
        // Empty POOR: combo break + gauge penalty, but no note is consumed.
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

        pressedColumns.Remove(column.Value);

        // Release: find the earliest LN in this column that is held and within the release window.
        HitObjectContainer.AliveObjects
            .OfType<DrawableBmsHitObject>()
            .Where(d => !d.Judged && d.HitObject.IsLongNote && d.HitObject.Column == column.Value
                        && d.HitObject.HitWindows.ResultFor(Time.Current - d.HitObject.EndTime) != HitResult.None)
            .MinBy(d => d.HitObject.EndTime)
            ?.TryRelease();
    }

    public bool IsColumnPressedForLandmine(int column) => pressedColumns.Contains(column);

    public void DetonateLandmine(BmsHitObject hitObject)
    {
        if (string.IsNullOrEmpty(hitObject.LandmineExplosionSamplePath))
            return;

        landmineSound.SampleInfo = new BmsSampleInfo(hitObject.LandmineExplosionSamplePath);
        landmineSound.Play();
    }

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

    protected override void LoadComplete()
    {
        base.LoadComplete();

        populateMeasureLines();

        NewResult += onNewResult;

        // Pre-build one SkinnableDrawable per result type so that showJudgement() never
        // allocates during gameplay.  HitResult.Miss is the empty-POOR display key.
        foreach (var result in BmsRuleset.STATIC_VALID_HIT_RESULTS)
        {
            var drawable = new SkinnableDrawable(
                new SkinComponentLookup<HitResult>(result))
            {
                RelativeSizeAxes = Axes.None,
                AutoSizeAxes = Axes.Both,
                // Centre horizontally within JudgementArea so the image lands on the
                // non-scratch column centre (JudgementArea itself is already positioned there).
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

        updateStageScale();
        updateHealthDisplayLayout();
    }

    private static bool isFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);

    private void recalculateSpeedFields()
    {
        BaseScrollRange = BmsDrawableRuleset.ComputeScrollTime(default_scroll_speed);
        ScrollSpeedMultiplier = ScrollSpeed / default_scroll_speed;
        TimeRange = BaseScrollRange / ScrollSpeedMultiplier;
        ScrollRange = BaseScrollRange;
    }

    private void showScrollSpeedText()
    {
        var configured = configuredScrollSpeed.Value;
        var delta = ScrollSpeed - configured;
        var colour = delta > 0 ? new Color4(255, 200, 0, 255)
            : delta < 0 ? new Color4(100, 180, 255, 255) : Color4.White;

        scrollSpeedArrow.Text = delta > 0 ? ">>" : delta < 0 ? "<<" : "";
        scrollSpeedArrow.Colour = colour;
        scrollSpeedText.Text = $"{ScrollSpeed:0.0}";
        scrollSpeedText.Colour = colour;
        scrollSpeedHud.ClearTransforms();
        scrollSpeedHud.FadeIn(80).Delay(1000).FadeOut(300);
    }

    [BackgroundDependencyLoader(true)]
    private void load(ISamplePlaybackDisabler? samplePlaybackDisabler)
    {
        recalculateSpeedFields();

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

    private void onNewResult(DrawableHitObject drawableHitObject, JudgementResult result)
    {
        if (drawableHitObject is not DrawableBmsHitObject bmsHitObject)
            return;

        if (bmsHitObject.HitObject.IsMine)
        {
            showJudgement(HitResult.Meh);
            return;
        }

        // Hit explosion only on hits (not misses).
        if (result.IsHit)
        {
            var column = Math.Clamp(bmsHitObject.HitObject.Column, 0, Stage.Columns.Length - 1);
            Stage.Columns[column].HitExplosionArea.Add(new BmsHitExplosion(new BmsSkinComponentLookup(
                BmsSkinComponents.HitExplosion,
                LayoutVariant,
                column,
                bmsHitObject.HitObject.IsLongNote)));
        }

        // Show judgement text for every result type (hits and misses).
        showJudgement(result.Type);
    }

    // ── EMPTY POOR ────────────────────────────────────────────────────────

    /// <summary>
    ///     Fires an Empty POOR: a keypress that found no note to consume.
    ///     Breaks combo and drains gauge by the same amount as a normal POOR,
    ///     but does not register a <see cref="JudgementResult"/> against any note.
    ///     Displays the POOR image (same as a note POOR) in the judgement area.
    /// </summary>
    private void registerEmptyPoor()
    {
        scoreProcessor?.RegisterEmptyPoor();
        healthProcessor?.RegisterEmptyPoor();

        // Show the POOR image. We look up HitResult.Miss, which the skin transformer
        // maps to the same mania-hit0 image as HitResult.Meh (POOR), so both note POORs
        // and empty POORs display identically.
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

        // Collect the current occupant(s) of JudgementArea before clearing, so we can return
        // them to the pool.  We must clear the container FIRST — a drawable can only belong to
        // one container, so Add-to-pool while still owned by JudgementArea would throw.
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

    // ── KEY SOUND ─────────────────────────────────────────────────────────

    private void playNextKeySound(int column)
    {
        if (samplePlaybackDisabled.Value)
            return;

        if (findNextSoundHitObject(column) is not { } hitObject || string.IsNullOrEmpty(hitObject.SamplePath))
            return;

        keySound.SampleInfo = new BmsSampleInfo(hitObject.SamplePath);
        keySound.Play();
    }

    private BmsHitObject? findNextSoundHitObject(int column)
    {
        var currentTime = Time.Current;

        var index = nextSoundIndexByColumn.GetValueOrDefault(column);

        if (!lastSoundSearchTimeByColumn.TryGetValue(column, out var lastSearchTime) || currentTime < lastSearchTime || currentTime - lastSearchTime > 5000)
            index = findFirstSoundCandidateIndex(currentTime - BmsHitWindows.BAD_WINDOW);

        lastSoundSearchTimeByColumn[column] = currentTime;

        // Skip notes that are definitely past all hit windows. Use the full BAD window (the widest
        // late window) so we never jump over a note that is still judgeable on a late keypress.
        while (index < hitObjects.Count && hitObjects[index].StartTime < currentTime - BmsHitWindows.BAD_WINDOW)
            index++;

        while (index < hitObjects.Count)
        {
            var hitObject = hitObjects[index];

            if (hitObject.Column != column || hasNoteFinished(hitObject))
            {
                index++;
                continue;
            }

            nextSoundIndexByColumn[column] = index;
            return hitObject;
        }

        nextSoundIndexByColumn[column] = index;
        return null;
    }

    private int findFirstSoundCandidateIndex(double time)
    {
        var low = 0;
        var high = hitObjects.Count;

        while (low < high)
        {
            var middle = low + (high - low) / 2;

            if (hitObjects[middle].StartTime < time)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private bool hasNoteFinished(BmsHitObject hitObject)
    {
        var drawable = HitObjectContainer.AliveObjects
            .OfType<DrawableBmsHitObject>()
            .FirstOrDefault(d => ReferenceEquals(d.HitObject, hitObject));

        if (drawable?.Judged == true)
            return true;

        if (Time.Current > hitObject.StartTime && drawable == null)
            return true;

        return Time.Current > hitObject.StartTime + hitObject.HitWindows.WindowFor(HitResult.Ok);
    }
}
