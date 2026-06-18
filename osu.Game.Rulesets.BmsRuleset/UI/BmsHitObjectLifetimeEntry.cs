using System;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI;

/// <summary>
///     Custom <see cref="HitObjectLifetimeEntry" /> used by <see cref="BmsPlayfield" />
///     to give BMS hit objects scroll-aware lifetimes: notes appear early enough to enter
///     from the top of the playfield (or exit cleanly at the bottom) at any scroll speed.
/// </summary>
internal sealed class BmsHitObjectLifetimeEntry(HitObject hitObject, BmsPlayfield playfield)
    : HitObjectLifetimeEntry(hitObject)
{

    #region Constants

    /// <summary>
    ///     Minimum visible window — no note should appear for less than this many ms before its hit time.
    /// </summary>
    private const double minimum_future_lifetime = 750;

    /// <summary>
    ///     Extra margin added to computed lifetimes so the note is fully visible (not clipped at the
    ///     container edge) when it enters the playfield.
    /// </summary>
    private const double lifetime_margin = 500;

    /// <summary>
    ///     How long a note stays alive after it passes the judgement line (or after its EndTime).
    /// </summary>
    private const double default_past_lifetime = 1000;

    /// <summary>
    ///     Step-size used when probing backwards from StartTime to find the earliest visible frame.
    ///     100 ms = balance between precision and search depth.
    /// </summary>
    private const double visible_window_search_step = 100;

    /// <summary>
    ///     Binary-search refinement stops when the window shrinks to this width (≈ 1 ms).
    /// </summary>
    private const double visible_window_binary_precision = 1;

    /// <summary>
    ///     Mines only need a single frame to check whether the column is pressed; after that they
    ///     can die immediately.  10 ms past-lifetime ensures they are alive on exactly one check.
    /// </summary>
    private const double mine_past_lifetime = 10;

    #endregion

    #region Cache state

    private double? cachedFutureLifetime;

    // Cached scroll state — when any of these change the future-lifetime cache is cleared.
    private double lastCachedScrollSpeed = double.NaN;
    private double lastCachedScrollRangeScale = double.NaN;
    private bool lastCachedConstantScrollActive;

    #endregion

    #region Entry lifecycle

    /// <summary>
    ///     Base framework offset (fallback lifetime used before the scroll-aware computation kicks in).
    /// </summary>
    protected override double InitialLifetimeOffset => 2500;

    /// <summary>
    ///     Re-compute and apply this entry's lifetime from current scroll state.
    ///     Called by <see cref="Components.BmsColumnHitObjectContainer" /> on <c>Add</c>
    ///     and periodically in <c>Update</c> so lifetimes track scroll-speed changes.
    /// </summary>
    public void RefreshLifetime(bool force = false)
    {
        if (HitObject is not BmsHitObject hitObject)
            return;

        invalidateCacheIfScrollChanged();

        var futureLifetime = computeFutureLifetime(hitObject);
        var pastLifetime = computePastLifetime();
        var lateWindow = getLateWindow(hitObject);

        var start = hitObject.StartTime - futureLifetime;
        var end = hitObject.IsMine
            ? hitObject.StartTime + mine_past_lifetime
            : hitObject.EndTime + Math.Max(pastLifetime, lateWindow + lifetime_margin);

        if (force || Math.Abs(LifetimeStart - start) >= 1)
            LifetimeStart = start;

        if (!Judged && (force || Math.Abs(LifetimeEnd - end) >= 1))
            LifetimeEnd = end;
    }

    private void invalidateCacheIfScrollChanged()
    {
        var scrollRangeScale = currentScrollRangeScale();

        if (Math.Abs(playfield.ScrollSpeed - lastCachedScrollSpeed) < 0.001
            && Math.Abs(scrollRangeScale - lastCachedScrollRangeScale) < 0.001
            && playfield.ConstantScrollActive == lastCachedConstantScrollActive)
        {
            return;
        }

        cachedFutureLifetime = null;
        lastCachedScrollSpeed = playfield.ScrollSpeed;
        lastCachedScrollRangeScale = scrollRangeScale;
        lastCachedConstantScrollActive = playfield.ConstantScrollActive;
    }

    #endregion

    #region Future lifetime (how far before StartTime the entry becomes alive)

    private double computeFutureLifetime(BmsHitObject hitObject)
    {
        if (cachedFutureLifetime.HasValue)
            return cachedFutureLifetime.Value;

        var value = computeUncachedFutureLifetime(hitObject);
        cachedFutureLifetime = value;
        return value;
    }

    private double computeUncachedFutureLifetime(BmsHitObject hitObject)
    {
        var timingMap = getTimingMap();

        if (useConstantScrollFallback(hitObject, timingMap))
            return computeConstantScrollFutureLifetime();

        var visibleTime = findEarliestVisibleWindowStart(hitObject, timingMap!);

        if (!double.IsFinite(visibleTime))
            return computeConstantScrollFutureLifetime();

        return Math.Max(minimum_future_lifetime, hitObject.StartTime - visibleTime + lifetime_margin);
    }

    private bool useConstantScrollFallback(BmsHitObject hitObject, BmsTimingMap? timingMap)
    {
        if (playfield.ConstantScrollActive || timingMap == null)
            return true;

        // Notes with Tick == EndTick == 0 and a non-zero StartTime have no meaningful scroll
        // segment data — use the simpler time-based projection.
        return hitObject.TickInfo.Tick == hitObject.TickInfo.EndTick
               && hitObject.TickInfo.Tick == 0
               && hitObject.StartTime != 0;
    }

    /// <summary>
    ///     Probe backwards from the hit object's <c>StartTime</c> to find the earliest time
    ///     at which this note is still visible on screen.  Uses a linear coarse pass (100 ms steps)
    ///     followed by binary refinement across the transition boundary.
    /// </summary>
    private double findEarliestVisibleWindowStart(BmsHitObject hitObject, BmsTimingMap timingMap)
    {
        var earliestVisibleTime = hitObject.StartTime;
        var laterTime = hitObject.StartTime;
        var laterVisible = true;

        for (var probeTime = hitObject.StartTime; probeTime > 0;)
        {
            var nextProbeTime = Math.Max(0, probeTime - visible_window_search_step);
            var nextVisible = isVisibleAt(hitObject, timingMap, nextProbeTime);

            if (nextVisible)
            {
                earliestVisibleTime = nextProbeTime;
            }
            else if (laterVisible)
            {
                earliestVisibleTime = refineVisibleWindowStart(hitObject, timingMap, nextProbeTime, laterTime);
            }

            probeTime = nextProbeTime;
            laterTime = nextProbeTime;
            laterVisible = nextVisible;
        }

        return earliestVisibleTime;
    }

    /// <summary>
    ///     Binary refinement: narrow the [hiddenTime, visibleTime] interval to find the exact
    ///     boundary where the note transitions from not-visible to visible.
    /// </summary>
    private double refineVisibleWindowStart(BmsHitObject hitObject, BmsTimingMap timingMap,
                                            double hiddenTime, double visibleTime)
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

    /// <summary>
    ///     The visible scroll distance (in scroll-coordinate space) at a given <paramref name="time" />.
    ///     This is the scroll-equivalent of "how far from the judgement line can a note be and still
    ///     be on screen".
    /// </summary>
    private double visibleScrollDistanceAt(BmsTimingMap timingMap, double time)
    {
        var speedFactor = Math.Abs(timingMap.GetSpeedFactorAtTime(time));

        if (!double.IsFinite(speedFactor) || speedFactor < 0.001)
            return double.PositiveInfinity;

        return BmsDrawableRuleset.ComputeScrollTime(BmsRulesetConfigManager.DEFAULT_SCROLL_SPEED)
               * currentScrollRangeScale()
               / Math.Max(0.001, playfield.ScrollSpeed / BmsRulesetConfigManager.DEFAULT_SCROLL_SPEED * speedFactor);
    }

    /// <summary>
    ///     Fallback lifetime used in constant-scroll mode or when the timing map is unavailable.
    ///     Based purely on the scroll speed without any timing-map segment lookup.
    /// </summary>
    private double computeConstantScrollFutureLifetime()
    {
        var speed = Math.Max(0.001, playfield.ScrollSpeed);
        return Math.Max(minimum_future_lifetime,
            BmsDrawableRuleset.ComputeScrollTime(speed) * currentScrollRangeScale() + lifetime_margin);
    }

    #endregion

    #region Past lifetime (how long after EndTime the entry stays alive)

    /// <summary>
    ///     Baseline past lifetime: note stays visible for this long after the judgement line.
    /// </summary>
    private static double computePastLifetime() => default_past_lifetime + lifetime_margin;

    /// <summary>
    ///     The BAD (late) hit-window for this object.  The entry must stay alive at least this long
    ///     past its EndTime so the auto-miss path in <see cref="Objects.Drawables.DrawableBmsHitObject.Update" /> can fire.
    /// </summary>
    private static double getLateWindow(BmsHitObject hitObject)
        => hitObject.HitWindows?.WindowFor(HitResult.Ok) ?? default_past_lifetime;

    #endregion

    #region Playfield helpers

    private double currentScrollRangeScale() => playfield.ScrollRangeScale > 0 ? playfield.ScrollRangeScale : 1;

    private BmsTimingMap? getTimingMap() => playfield.TimingMap;

    #endregion

}
