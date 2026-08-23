using System;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal sealed class BmsGameplayScrollController(BmsTimingMap? timingMap)
{
    public const double MAX_TIME_RANGE = 11485;

    public BmsTimingMap? TimingMap { get; } = timingMap;

    public double ScrollRange => ComputeScrollTime(default_scroll_speed) * ScrollRangeScale * PlaybackRate;

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

    public bool ConstantScrollActive { get; set; }

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
    }

    public void SetConfiguredScrollSpeed(double speed)
    {
        configuredScrollSpeed = speed;
        setScrollSpeedFromMultiplierIndex(false);
    }

    public void SetPlaybackRate(double rate)
    {
        PlaybackRate = double.IsFinite(rate) && Math.Abs(rate) >= 0.001 ? Math.Abs(rate) : 1.0;
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
        CurrentScrollPosition = ConstantScrollActive
            ? currentTime
            : TimingMap?.GetScrollPositionAtTime(currentTime) ?? currentTime;

        ChartSpeedFactor = ConstantScrollActive
            ? 1.0
            : TimingMap?.GetSpeedFactorAtTime(currentTime) ?? 1.0;
    }

    public double GetVisualScrollPosition(double time, double mappedScrollPosition) =>
        ConstantScrollActive ? time : mappedScrollPosition;

    public float YForScrollProgress(double progress, double parentHeight, double hitTargetPosition, double noteHeight = 0)
        => (float)(parentHeight - hitTargetPosition - progress * ScrollCoordinateScale(parentHeight, hitTargetPosition) - noteHeight);

    public double ScrollCoordinateScale(double parentHeight, double hitTargetPosition)
    {
        var range = Math.Max(1.0, ScrollRange);
        var travelDistance = Math.Max(1f, (float)(parentHeight - hitTargetPosition));
        return ScrollSpeedMultiplier / range * travelDistance;
    }

    private void setScrollSpeedFromMultiplierIndex(bool fireEvent = true)
    {
        ScrollSpeed = configuredScrollSpeed * scroll_speed_multipliers[currentMultiplierIndex];

        if (fireEvent)
            ScrollSpeedChanged?.Invoke(scroll_speed_multipliers[currentMultiplierIndex]);
    }

    public event Action<double>? ScrollSpeedChanged;
}
