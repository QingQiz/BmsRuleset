using System;
using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI;

internal readonly record struct BmsHitObjectLifetimePlan(double LifetimeStart, double LifetimeEnd);

internal sealed class BmsHitObjectLifetimePlanner(BmsPlayfield playfield)
{
    internal const double MINIMUM_FUTURE_LIFETIME = 750;
    internal const double LIFETIME_MARGIN = 500;
    internal const double DEFAULT_PAST_LIFETIME = 1000;

    /// <summary>
    /// Lifetime past a mine's <see cref="osu.Game.Rulesets.Objects.HitObject.StartTime"/>, in ms.
    /// Mines only need a single frame to check whether the column is pressed.
    /// </summary>
    private const double mine_past_lifetime = 10;

    private readonly Dictionary<BmsHitObject, BmsHitObjectLifetimePlan> plans = new();

    private double lastCachedScrollSpeed = double.NaN;
    private double lastCachedScrollRangeScale = double.NaN;
    private bool lastCachedConstantScrollActive;

    public BmsHitObjectLifetimePlan CreatePlan(BmsHitObject hitObject)
    {
        RefreshIfNeeded();
        return CreatePlanForCurrentSettings(hitObject);
    }

    public BmsHitObjectLifetimePlan CreatePlanForCurrentSettings(BmsHitObject hitObject)
    {
        if (plans.TryGetValue(hitObject, out var cached))
            return cached;

        var futureLifetime = computeFutureLifetime(hitObject);
        var pastLifetime = computePastLifetime();
        var start = hitObject.StartTime - futureLifetime;
        var end = hitObject.IsMine
            ? hitObject.StartTime + mine_past_lifetime
            : hitObject.EndTime + Math.Max(pastLifetime, getLateWindow(hitObject) + LIFETIME_MARGIN);

        return plans[hitObject] = new BmsHitObjectLifetimePlan(start, end);
    }

    public bool RefreshIfNeeded()
    {
        var scrollRangeScale = currentScrollRangeScale();

        if (Math.Abs(playfield.ScrollSpeed - lastCachedScrollSpeed) < 0.001
            && Math.Abs(scrollRangeScale - lastCachedScrollRangeScale) < 0.001
            && playfield.ConstantScrollActive == lastCachedConstantScrollActive)
        {
            return false;
        }

        plans.Clear();
        lastCachedScrollSpeed = playfield.ScrollSpeed;
        lastCachedScrollRangeScale = scrollRangeScale;
        lastCachedConstantScrollActive = playfield.ConstantScrollActive;
        return true;
    }

    private static bool usesLinearTimeProjection(BmsHitObject hitObject)
        => hitObject.TickInfo.Tick == hitObject.TickInfo.EndTick && hitObject.TickInfo.Tick == 0 && hitObject.StartTime != 0;

    private static double computePastLifetime() => DEFAULT_PAST_LIFETIME + LIFETIME_MARGIN;

    private static double getLateWindow(BmsHitObject hitObject)
        => hitObject.HitWindows?.WindowFor(HitResult.Ok) ?? DEFAULT_PAST_LIFETIME;

    private double computeFutureLifetime(BmsHitObject hitObject)
    {
        var timingMap = playfield.TimingMap;

        if (playfield.ConstantScrollActive || timingMap == null || usesLinearTimeProjection(hitObject))
            return computeConstantScrollFutureLifetime();

        var visibleTime = findEarliestVisibleWindowStart(hitObject, timingMap);

        return Math.Max(MINIMUM_FUTURE_LIFETIME, hitObject.StartTime - visibleTime + LIFETIME_MARGIN);
    }

    private double findEarliestVisibleWindowStart(BmsHitObject hitObject, BmsTimingMap timingMap)
    {
        foreach (var segment in timingMap.GetScrollTimingSegments())
        {
            if (segment.Time > hitObject.StartTime)
                break;

            var segmentStart = Math.Max(0, segment.Time);
            var segmentEnd = Math.Min(hitObject.StartTime, segment.NextTime);

            if (segmentEnd < segmentStart)
                continue;

            var visibleDistance = visibleScrollDistanceForSpeedFactor(segment.SpeedFactor);

            if (isVisibleAtSegmentTime(hitObject, segment, segmentStart, visibleDistance))
                return segmentStart;

            if (segment.ScrollVelocity <= 0)
                continue;

            var timeVisible = segment.Time + (hitObject.ScrollPositionAtStartTime - segment.ScrollPosition - visibleDistance) / segment.ScrollVelocity;
            var candidate = Math.Max(segmentStart, timeVisible);

            if (candidate <= segmentEnd && isVisibleAtSegmentTime(hitObject, segment, candidate, visibleDistance))
                return candidate;
        }

        return hitObject.StartTime;
    }

    private static bool isVisibleAtSegmentTime(BmsHitObject hitObject, BmsScrollTimingSegment segment, double time, double visibleDistance)
    {
        var scrollPosition = segment.ScrollPosition + segment.ScrollVelocity * (time - segment.Time);
        var progress = hitObject.ScrollPositionAtStartTime - scrollPosition;
        return progress <= visibleDistance;
    }

    private double visibleScrollDistanceForSpeedFactor(double speedFactor)
    {
        speedFactor = Math.Abs(speedFactor);

        if (!double.IsFinite(speedFactor) || speedFactor < 0.001)
            return double.PositiveInfinity;

        return BmsDrawableRuleset.ComputeScrollTime(BmsRulesetConfigManager.DEFAULT_SCROLL_SPEED)
               * currentScrollRangeScale()
               / Math.Max(0.001, playfield.ScrollSpeed / BmsRulesetConfigManager.DEFAULT_SCROLL_SPEED * speedFactor);
    }

    private double computeConstantScrollFutureLifetime()
    {
        var speed = Math.Max(0.001, playfield.ScrollSpeed);
        return Math.Max(MINIMUM_FUTURE_LIFETIME, BmsDrawableRuleset.ComputeScrollTime(speed) * currentScrollRangeScale() + LIFETIME_MARGIN);
    }

    private double currentScrollRangeScale() => playfield.ScrollRangeScale > 0 ? playfield.ScrollRangeScale : 1;
}
