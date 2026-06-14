using System;
using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.UI;

public partial class BmsHitObjectContainer(BmsPlayfield playfield) : HitObjectContainer
{
    private const double minimum_future_lifetime = 750;
    private const double lifetime_margin = 500;
    private const double default_past_lifetime = 1000;
    private const double visible_window_search_step = 100;
    private const double visible_window_binary_precision = 1;

    private readonly Dictionary<BmsHitObject, DrawableBmsHitObject> aliveDrawableMap = new();
    private readonly Dictionary<BmsHitObject, double> futureLifetimeCache = new();

    private double lastPastLifetime = double.NaN;
    private double lastUpdateTime = double.NaN;
    private double lastCachedScrollSpeed = double.NaN;
    private double lastCachedScrollRangeScale = double.NaN;
    private bool lastCachedConstantScrollActive;

    // ReSharper disable once UnusedMethodReturnValue.Global
    public bool TryGetAliveDrawable(BmsHitObject hitObject, out DrawableBmsHitObject? drawable)
        => aliveDrawableMap.TryGetValue(hitObject, out drawable);

    public override void Add(HitObjectLifetimeEntry entry)
    {
        updateEntryLifetime(entry, force: true);
        base.Add(entry);
    }

    protected override void AddDrawable(HitObjectLifetimeEntry entry, DrawableHitObject drawable)
    {
        base.AddDrawable(entry, drawable);

        if (entry.HitObject is BmsHitObject hitObject && drawable is DrawableBmsHitObject bmsDrawable)
            aliveDrawableMap[hitObject] = bmsDrawable;
    }

    protected override void RemoveDrawable(HitObjectLifetimeEntry entry, DrawableHitObject drawable)
    {
        if (entry.HitObject is BmsHitObject hitObject)
            aliveDrawableMap.Remove(hitObject);

        base.RemoveDrawable(entry, drawable);
    }

    protected override void Update()
    {
        base.Update();

        var pastLifetime = computePastLifetime();
        var futureLifetimeCacheChanged = ensureFutureLifetimeCacheValid();

        if (!futureLifetimeCacheChanged
            && Math.Abs(pastLifetime - lastPastLifetime) < 1
            && Math.Abs(Time.Current - lastUpdateTime) < 250)
        {
            return;
        }

        lastPastLifetime = pastLifetime;
        lastUpdateTime = Time.Current;

        foreach (var entry in Entries)
            updateEntryLifetime(entry);
    }

    private static bool usesLinearTimeProjection(BmsHitObject hitObject)
        => hitObject.TickInfo.Tick == hitObject.TickInfo.EndTick && hitObject.TickInfo.Tick == 0 && hitObject.StartTime != 0;

    private static double computePastLifetime() => default_past_lifetime + lifetime_margin;

    private static double getLateWindow(BmsHitObject hitObject)
        => hitObject.HitWindows?.WindowFor(HitResult.Ok) ?? default_past_lifetime;

    private void updateEntryLifetime(HitObjectLifetimeEntry entry, bool force = false)
    {
        if (entry.HitObject is not BmsHitObject hitObject)
            return;

        var futureLifetime = computeFutureLifetime(hitObject);
        var pastLifetime = double.IsNaN(lastPastLifetime) ? computePastLifetime() : lastPastLifetime;

        var start = hitObject.StartTime - futureLifetime;
        var end = hitObject.EndTime + Math.Max(pastLifetime, getLateWindow(hitObject) + lifetime_margin);

        if (force || Math.Abs(entry.LifetimeStart - start) >= 1)
            entry.LifetimeStart = start;

        if (!entry.Judged && (force || Math.Abs(entry.LifetimeEnd - end) >= 1))
            entry.LifetimeEnd = end;
    }

    private double computeFutureLifetime(BmsHitObject hitObject)
    {
        ensureFutureLifetimeCacheValid();

        if (futureLifetimeCache.TryGetValue(hitObject, out var cached))
            return cached;

        var futureLifetime = computeUncachedFutureLifetime(hitObject);
        futureLifetimeCache[hitObject] = futureLifetime;
        return futureLifetime;
    }

    private double computeUncachedFutureLifetime(BmsHitObject hitObject)
    {
        var timingMap = playfield.TimingMap;

        if (playfield.ConstantScrollActive || timingMap == null || usesLinearTimeProjection(hitObject))
            return computeConstantScrollFutureLifetime();

        var visibleTime = findFinalVisibleWindowStart(hitObject, timingMap);

        if (!double.IsFinite(visibleTime))
            return computeConstantScrollFutureLifetime();

        return Math.Max(minimum_future_lifetime, hitObject.StartTime - visibleTime + lifetime_margin);
    }

    private bool ensureFutureLifetimeCacheValid()
    {
        var scrollRangeScale = currentScrollRangeScale();

        if (Math.Abs(playfield.ScrollSpeed - lastCachedScrollSpeed) < 0.001
            && Math.Abs(scrollRangeScale - lastCachedScrollRangeScale) < 0.001
            && playfield.ConstantScrollActive == lastCachedConstantScrollActive)
        {
            return false;
        }

        futureLifetimeCache.Clear();
        lastCachedScrollSpeed = playfield.ScrollSpeed;
        lastCachedScrollRangeScale = scrollRangeScale;
        lastCachedConstantScrollActive = playfield.ConstantScrollActive;
        return true;
    }

    private double findFinalVisibleWindowStart(BmsHitObject hitObject, BmsTimingMap timingMap)
    {
        var visibleTime = hitObject.StartTime;

        for (var probeTime = hitObject.StartTime; probeTime > 0;)
        {
            var nextProbeTime = Math.Max(0, probeTime - visible_window_search_step);

            if (!isVisibleAt(hitObject, timingMap, nextProbeTime))
                return refineVisibleWindowStart(hitObject, timingMap, nextProbeTime, probeTime);

            visibleTime = nextProbeTime;
            probeTime = nextProbeTime;
        }

        return visibleTime;
    }

    private double refineVisibleWindowStart(BmsHitObject hitObject, BmsTimingMap timingMap, double hiddenTime, double visibleTime)
    {
        while (visibleTime - hiddenTime > visible_window_binary_precision)
        {
            var midpoint = (hiddenTime + visibleTime) / 2;

            if (isVisibleAt(hitObject, timingMap, midpoint))
                visibleTime = midpoint;
            else
                hiddenTime = midpoint;
        }

        return visibleTime;
    }

    private bool isVisibleAt(BmsHitObject hitObject, BmsTimingMap timingMap, double time)
    {
        var progress = hitObject.ScrollPositionAtStartTime - timingMap.GetScrollPositionAtTime(time);
        return progress <= visibleScrollDistanceAt(timingMap, time);
    }

    private double visibleScrollDistanceAt(BmsTimingMap timingMap, double time)
    {
        var speedFactor = Math.Abs(timingMap.GetSpeedFactorAtTime(time));

        if (!double.IsFinite(speedFactor) || speedFactor < 0.001)
            return double.PositiveInfinity;

        return BmsDrawableRuleset.ComputeScrollTime(BmsRulesetConfigManager.DEFAULT_SCROLL_SPEED)
               * currentScrollRangeScale()
               / Math.Max(0.001, playfield.ScrollSpeed / BmsRulesetConfigManager.DEFAULT_SCROLL_SPEED * speedFactor);
    }

    private double computeConstantScrollFutureLifetime()
    {
        var speed = Math.Max(0.001, playfield.ScrollSpeed);
        return Math.Max(minimum_future_lifetime, BmsDrawableRuleset.ComputeScrollTime(speed) * currentScrollRangeScale() + lifetime_margin);
    }

    private double currentScrollRangeScale() => playfield.ScrollRangeScale > 0 ? playfield.ScrollRangeScale : 1;
}

internal sealed class BmsHitObjectLifetimeEntry(HitObject hitObject) : HitObjectLifetimeEntry(hitObject)
{
    protected override double InitialLifetimeOffset => 2500;
}
