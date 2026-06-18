using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.UI.Components;

public sealed partial class BmsColumnHitObjectContainer : HitObjectContainer
{

    private readonly BmsPlayfield playfield;

    public BmsColumnHitObjectContainer(BmsPlayfield playfield)
    {
        this.playfield = playfield;
        RelativeSizeAxes = Axes.Both;
    }


    #region Positioning

    protected override void UpdateAfterChildrenLife()
    {
        base.UpdateAfterChildrenLife();

        var currentScrollPos = playfield.CurrentScrollPosition;
        var hitTarget = playfield.Stage.HitTargetPosition;
        var scale = playfield.ScrollSpeedMultiplier / Math.Max(1.0, playfield.ScrollRange)
                    * Math.Max(1f, DrawHeight - hitTarget);

        // Position all alive entries in this column
        foreach (var entry in AliveEntries)
        {
            if (entry.Value is not DrawableBmsHitObject note)
                continue;

            var offset = (float)((note.HitObject!.ScrollPositionAtStartTime - currentScrollPos) * scale);
            var y = -(hitTarget + offset);

            note.Y = y;

            if (note is DrawableBmsLongNote ln)
            {
                var endOffset = (float)((note.HitObject.ScrollPositionAtEndTime - currentScrollPos) * scale);

                // Clamp the tail so it never extends in the "past" direction past the judgement line:
                //   normal scroll (positive factor) → tail should not go below the hit target
                //   reverse scroll (negative factor) → tail should not go above the hit target
                var clampedEndOffset = playfield.ChartScrollFactor >= 0
                    ? Math.Max(endOffset, 0)
                    : Math.Min(endOffset, 0);

                ln.UpdateBodyGeometry(y, -(hitTarget + clampedEndOffset));
            }
        }
    }

    #endregion

    #region Lifetime

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

    #region Lifetime state

    private readonly Dictionary<BmsHitObject, double> futureLifetimeCache = new();

    private double lastPastLifetime = double.NaN;
    private double lastUpdateTime = double.NaN;

    // Cached scroll state — when any of these change the future-lifetime cache is cleared.
    private double lastCachedScrollSpeed = double.NaN;
    private double lastCachedScrollRangeScale = double.NaN;
    private bool lastCachedConstantScrollActive;

    #endregion

    #region Lifetime management

    public override void Add(HitObjectLifetimeEntry entry)
    {
        updateEntryLifetime(entry, force: true);
        base.Add(entry);
    }

    protected override void Update()
    {
        base.Update();

        var pastLifetime = computePastLifetime();
        var cacheChanged = invalidateCacheIfScrollChanged();

        if (!cacheChanged
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

    #endregion


    #region Lifetime helpers

    private void updateEntryLifetime(HitObjectLifetimeEntry entry, bool force = false)
    {
        if (entry.HitObject is not BmsHitObject hitObject)
            return;

        var futureLifetime = computeFutureLifetime(hitObject);
        var pastLifetime = double.IsNaN(lastPastLifetime) ? computePastLifetime() : lastPastLifetime;

        var start = hitObject.StartTime - futureLifetime;
        var end = hitObject.IsMine
            ? hitObject.StartTime + mine_past_lifetime
            : hitObject.EndTime + Math.Max(pastLifetime, getLateWindow(hitObject) + lifetime_margin);

        if (force || Math.Abs(entry.LifetimeStart - start) >= 1)
            entry.LifetimeStart = start;

        if (!entry.Judged && (force || Math.Abs(entry.LifetimeEnd - end) >= 1))
            entry.LifetimeEnd = end;
    }

    private static double computePastLifetime() => default_past_lifetime + lifetime_margin;

    /// <summary>
    ///     The BAD (late) hit-window for this object.  The entry must stay alive at least this long
    ///     past its EndTime so the auto-miss path in <see cref="DrawableBmsHitObject.Update"/> can fire.
    /// </summary>
    private static double getLateWindow(BmsHitObject hitObject)
        => hitObject.HitWindows?.WindowFor(HitResult.Ok) ?? default_past_lifetime;

    /// <summary>
    ///     Compute the future (pre-hit) lifetime for <paramref name="hitObject"/>.
    ///     Results are cached in <see cref="futureLifetimeCache"/> and only recalculated when scroll
    ///     parameters change.
    /// </summary>
    private double computeFutureLifetime(BmsHitObject hitObject)
    {
        if (futureLifetimeCache.TryGetValue(hitObject, out var cached))
            return cached;

        var futureLifetime = computeUncachedFutureLifetime(hitObject);
        futureLifetimeCache[hitObject] = futureLifetime;
        return futureLifetime;
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
    ///     The visible scroll distance (in scroll-coordinate space) at a given <paramref name="time"/>.
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
        return Math.Max(minimum_future_lifetime, BmsDrawableRuleset.ComputeScrollTime(speed) * currentScrollRangeScale() + lifetime_margin);
    }

    /// <summary>
    ///     Check whether scroll parameters have changed and clear the future-lifetime cache if so.
    ///     Returns <c>true</c> when the cache was invalidated.
    /// </summary>
    private bool invalidateCacheIfScrollChanged()
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

    private double currentScrollRangeScale() => playfield.ScrollRangeScale > 0 ? playfield.ScrollRangeScale : 1;

    private BmsTimingMap? getTimingMap() => playfield.TimingMap;

    #endregion

}
