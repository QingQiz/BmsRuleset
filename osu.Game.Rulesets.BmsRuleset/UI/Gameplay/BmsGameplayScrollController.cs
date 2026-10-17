using System;
using System.Linq;
using osu.Framework.Bindables;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal sealed class BmsGameplayScrollController(BmsTimingMap? timingMap)
{
    public const double MAX_TIME_RANGE = 11485;

    public BmsTimingMap? TimingMap { get; } = timingMap;

    // With forward-only scroll and a fixed SPEED, visibility has a single boundary. STOPs and
    // positive BPM/SCROLL changes preserve that ordering, so searching all earlier times is wasteful.
    internal bool HasMonotonicVisibility { get; } =
        timingMap != null
        && timingMap.SpeedEvents.Count == 0
        && timingMap.BpmEvents.All(e => double.IsFinite(e.Bpm) && e.Bpm > 0)
        && timingMap.ScrollEvents.All(e => double.IsFinite(e.Factor) && e.Factor >= 0);

    public double ScrollRange { get; private set; } = ComputeScrollTime(default_scroll_speed);

    public double ScrollSpeedMultiplier { get; private set; } = 1.0;

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

    public bool ConstantScrollActive { get; set; }

    public BindableBool SoftConstant { get; } = new();

    public BindableDouble SoftConstantFadeIn { get; } = new();

    public double DisplayTime { get; private set; }

    private bool softConstantActive;
    private bool softConstantHasFadeIn;
    private double softConstantAlphaEndTime;
    private double softConstantAlphaScale;

    internal bool SoftConstantActive => softConstantActive && !scrollSpeedMultiplierLocked;

    public float GetSoftConstantAlpha(double startTime)
    {
        if (!softConstantActive || scrollSpeedMultiplierLocked)
            return 1;

        // The reveal window is shared by every note in the frame. Precompute its
        // real-time boundaries once so dense charts avoid repeated divisions.
        if (!softConstantHasFadeIn)
            return startTime < softConstantAlphaEndTime ? 1 : 0;

        return (float)Math.Clamp((softConstantAlphaEndTime - startTime) * softConstantAlphaScale, 0, 1);
    }

    /// <summary>
    ///     Whether the in-play scroll speed multiplier is locked to its default value,
    ///     enforced by the course <c>no_speed</c> constraint.
    /// </summary>
    private bool scrollSpeedMultiplierLocked { get; set; }

    public void LockScrollSpeedMultiplier()
    {
        scrollSpeedMultiplierLocked = true;
        currentMultiplierIndex = default_multiplier_index;
        setScrollSpeedFromMultiplierIndex();
    }

    public double ScrollSpeed { get; private set; } = default_scroll_speed;

    public double CurrentScrollPosition { get; private set; }

    public double ChartSpeedFactor { get; private set; } = 1.0;

    public double ScrollRangeScale { get; private set; } = 1.0;

    public double PlaybackRate { get; private set; } = 1.0;

    // All columns share these values; refresh them at the state boundary so layout reads avoid division.
    private double scrollCoordinateScale = 1 / ComputeScrollTime(default_scroll_speed);

    private const int default_multiplier_index = 9;

    private const double default_scroll_speed = BmsRulesetConfigManager.DEFAULT_SCROLL_SPEED;

    private static readonly double[] scroll_speed_multipliers =
    [
        0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0,
        1.25, 1.5, 1.75, 2.0, 2.25, 2.5, 2.75,
        3.0, 3.5, 4.0, 4.5, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0,
    ];

    private double configuredScrollSpeed = default_scroll_speed;
    private int currentMultiplierIndex = default_multiplier_index;

    public static double ComputeScrollTime(double scrollSpeed) => MAX_TIME_RANGE / Math.Max(1, scrollSpeed);

    public void SetHitTargetPosition(float hitTargetPosition)
    {
        const float reference_scroll_distance = 768f - 124.8f;
        ScrollRangeScale = (768f - hitTargetPosition) / reference_scroll_distance;
        updateScrollRange();
    }

    public void SetConfiguredScrollSpeed(double speed)
    {
        configuredScrollSpeed = speed;
        setScrollSpeedFromMultiplierIndex(false);
    }

    public void SetPlaybackRate(double rate)
    {
        PlaybackRate = double.IsFinite(rate) && Math.Abs(rate) >= 0.001 ? Math.Abs(rate) : 1.0;
        updateScrollRange();
    }

    public void AdjustScrollSpeed(double delta)
    {
        if (scrollSpeedMultiplierLocked)
            return;

        var direction = delta > 0 ? 1 : -1;
        var newIndex = Math.Clamp(currentMultiplierIndex + direction, 0, scroll_speed_multipliers.Length - 1);

        if (newIndex == currentMultiplierIndex)
            return;

        currentMultiplierIndex = newIndex;
        setScrollSpeedFromMultiplierIndex();
    }

    public void Update(double currentTime)
    {
        DisplayTime = currentTime;
        CurrentScrollPosition = ConstantScrollActive
            ? currentTime
            : TimingMap?.GetScrollPositionAtTime(currentTime) ?? currentTime;

        ChartSpeedFactor = ConstantScrollActive
            ? 1.0
            : TimingMap?.GetSpeedFactorAtTime(currentTime) ?? 1.0;
        updateScrollSpeedMultiplier();

        // These values are shared by every alive note in the frame; cache them once to avoid
        // repeated bindable reads and scroll-time calculations during per-note layout.
        softConstantActive = SoftConstant.Value;
        if (softConstantActive)
        {
            var duration = ComputeScrollTime(ScrollSpeed) * ScrollRangeScale;
            var fadeIn = SoftConstantFadeIn.Value;
            var fadeStart = duration + Math.Max(0, fadeIn);
            softConstantHasFadeIn = fadeIn != 0;
            softConstantAlphaEndTime = DisplayTime + fadeStart * PlaybackRate;
            softConstantAlphaScale = softConstantHasFadeIn
                ? 1 / (Math.Abs(fadeIn) * PlaybackRate)
                : 0;
        }
    }

    public double GetVisualScrollPosition(double time, double mappedScrollPosition) =>
        ConstantScrollActive ? time : mappedScrollPosition;

    public void ApplyLongNoteTailVisualOffset(BmsLongNote longNote, double visualOffset)
    {
        if (visualOffset == 0)
        {
            longNote.VisualScrollPositionAtEndTime = GetVisualScrollPosition(longNote.EndTime, longNote.ScrollPositionAtEndTime);
            return;
        }

        var visualEndTime = Math.Max(longNote.StartTime, longNote.EndTime - visualOffset * PlaybackRate);
        longNote.VisualScrollPositionAtEndTime = ConstantScrollActive || TimingMap == null
            ? visualEndTime
            : TimingMap.GetScrollPositionAtTime(visualEndTime);
    }

    public float YForScrollProgress(double progress, double parentHeight, double hitTargetPosition, double noteHeight = 0)
        => (float)(parentHeight - hitTargetPosition - progress * ScrollCoordinateScale(parentHeight, hitTargetPosition) - noteHeight);

    public double ScrollCoordinateScale(double parentHeight, double hitTargetPosition)
    {
        var travelDistance = Math.Max(1f, (float)(parentHeight - hitTargetPosition));
        return scrollCoordinateScale * travelDistance;
    }

    private void setScrollSpeedFromMultiplierIndex(bool fireEvent = true)
    {
        ScrollSpeed = configuredScrollSpeed * scroll_speed_multipliers[currentMultiplierIndex];
        updateScrollSpeedMultiplier();

        if (fireEvent)
            ScrollSpeedChanged?.Invoke(scroll_speed_multipliers[currentMultiplierIndex]);
    }

    private void updateScrollRange()
    {
        ScrollRange = ComputeScrollTime(default_scroll_speed) * ScrollRangeScale * PlaybackRate;
        updateScrollCoordinateScale();
    }

    private void updateScrollSpeedMultiplier()
    {
        ScrollSpeedMultiplier = ScrollSpeed / default_scroll_speed * ChartSpeedFactor;
        updateScrollCoordinateScale();
    }

    private void updateScrollCoordinateScale() => scrollCoordinateScale = ScrollSpeedMultiplier / Math.Max(1.0, ScrollRange);

    public event Action<double>? ScrollSpeedChanged;
}
