using System;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI;

internal sealed class BmsHitObjectLifetimeEntry(HitObject hitObject, BmsPlayfield playfield)
    : HitObjectLifetimeEntry(hitObject)
{

    /// <summary>
    ///     Set to true once <see cref="RefreshLifetime"/> has run.
    ///     Guards against framework code overwriting our
    ///     scroll-speed-aware computed values with blanket defaults.
    /// </summary>
    private bool lifetimeComputed;

    #region Constants

    /// <summary>
    ///     Minimum visible window — no note should appear for less than this many ms before its hit time.
    /// </summary>
    private const double minimum_future_lifetime = 100;

    /// <summary>
    ///     Extra margin added to computed lifetimes so the note is fully visible (not clipped at the
    ///     container edge) when it enters the playfield.
    /// </summary>
    private const double lifetime_margin = 0;

    /// <summary>
    ///     How long a note stays alive after it passes the judgement line (or after its EndTime).
    /// </summary>
    private const double default_past_lifetime = 0;

    private const double passive_poor_lifetime_margin = 100;

    /// <summary>
    ///     Mines only need a single frame to check whether the column is pressed; after that they
    ///     can die immediately.  10 ms past-lifetime ensures they are alive on exactly one check.
    /// </summary>
    private const double mine_past_lifetime = 0;

    /// <summary>
    ///     Step-size used when probing backwards from StartTime to find the earliest visible frame.
    ///     100 ms = balance between precision and search depth.
    /// </summary>
    private const double visible_window_search_step = 100;

    /// <summary>
    ///     Binary-search refinement stops when the window shrinks to this width (≈ 1 ms).
    /// </summary>
    private const double visible_window_binary_precision = 1;

    #endregion

    #region Entry lifecycle

    protected override double InitialLifetimeOffset => 0;

    /// <summary>
    ///     Re-compute and apply this entry's lifetime from current scroll state.
    ///     Called once on <c>Add</c> (during loading) and again whenever the user
    ///     adjusts the scroll speed in-game.
    /// </summary>
    public void RefreshLifetime()
    {
        if (HitObject is not BmsHitObject hitObject)
            return;

        var futureLifetime = computeFutureLifetime(hitObject);
        var pastLifetime = computePastLifetime();
        var lateWindow = getLateWindow(hitObject);

        lifetimeComputed = false;

        // Set LifetimeEnd before LifetimeStart.  Setting LifetimeStart first
        // with the current LifetimeEnd would produce an intermediate
        // MaxValue that the framework may latch on to before the follow-up
        // LifetimeEnd set corrects it.
        LifetimeEnd = hitObject is BmsLandmine
            ? hitObject.StartTime + mine_past_lifetime
            : hitObject.GetEndTime() + Math.Max(pastLifetime, lateWindow + lifetime_margin);
        LifetimeStart = hitObject.StartTime - futureLifetime;

        lifetimeComputed = true;
    }

    #endregion

    #region Guard overrides: protect computed lifetimes from framework overwrites

    /// <summary>
    ///     Once <see cref="RefreshLifetime"/> has computed a scroll-speed-aware
    ///     value, reject subsequent overwrites from
    ///     <see cref="HitObjectLifetimeEntry.SetInitialLifetime"/>
    ///     (triggered by <c>DefaultsApplied</c> / <c>StartTimeBindable</c>)
    ///     that would reset <c>LifetimeStart</c> to the default offset.
    /// </summary>
    protected override void SetLifetimeStart(double start)
    {
        if (!lifetimeComputed)
            base.SetLifetimeStart(start);
    }

    /// <summary>
    ///     <see cref="DrawableHitObject{TObject}.UpdateState"/> unconditionally sets
    ///     <c>LifetimeEnd = double.MaxValue</c> on every state transition.
    ///     For entries whose state stays Idle until hit — such
    ///     as mines — the follow-up conditional at line 487 does not fire,
    ///     leaving the entry alive indefinitely.  Reject the blanket MaxValue
    ///     when we already hold a correct finite value.
    /// </summary>
    protected override void SetLifetimeEnd(double end)
    {
        if (!lifetimeComputed || end < double.MaxValue - 1)
            base.SetLifetimeEnd(end);
    }

    #endregion

    #region Future lifetime (how far before StartTime the entry becomes alive)

    private double computeFutureLifetime(BmsHitObject hitObject)
    {
        // Floor the future lifetime at the early BAD (Ok) window so the entry is alive before the
        // earliest moment a press can be judged.
        var floor = Math.Max(getEarlyBadWindow(hitObject), minimum_future_lifetime);
        var timingMap = getTimingMap();

        if (useConstantScrollFallback(hitObject, timingMap))
            return Math.Max(floor, computeConstantScrollFutureLifetime());

        var visibleTime = findEarliestVisibleWindowStart(hitObject, timingMap!);

        return !double.IsFinite(visibleTime)
            ? Math.Max(floor, computeConstantScrollFutureLifetime())
            : Math.Max(floor, hitObject.StartTime - visibleTime + lifetime_margin);
    }

    /// <summary>
    ///     The early BAD (Ok) hit window for this object's head judgement — the furthest ahead of
    ///     <see cref="HitObject.StartTime" /> at which a press can still be judged. Used as the
    ///     minimum future lifetime so the entry is alive for any hittable press.
    /// </summary>
    private static double getEarlyBadWindow(BmsHitObject hitObject)
    {
        var table = BmsJudgementProfileProvider.GetTable(hitObject.Beatmap.LayoutVariant, hitObject.Column, hitObject.Beatmap.Rank, tail: false);
        return Math.Abs(table.EarlyWindowFor(HitResult.Ok));
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
        if (isVisibleAtPosition(hitObject.ScrollPositionAtStartTime, timingMap, time))
            return true;

        // An LN's body spans from the head's to the tail's scroll position. Under negative scroll
        // the tail is the leading edge and appears before the head, so the body is on screen — and
        // the entry must already be alive — while the tail is visible even if the head isn't yet.
        if (hitObject is BmsLongNote ln)
            return isVisibleAtPosition(ln.ScrollPositionAtEndTime, timingMap, time);

        return false;
    }

    private bool isVisibleAtPosition(double scrollPosition, BmsTimingMap timingMap, double time)
    {
        var progress = scrollPosition - timingMap.GetScrollPositionAtTime(time);
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
        return BmsDrawableRuleset.ComputeScrollTime(speed) * currentScrollRangeScale() + lifetime_margin;
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
    {
        if (hitObject is BmsLongNote)
        {
            var tailTable = BmsJudgementProfileProvider.GetTable(hitObject.Beatmap.LayoutVariant, hitObject.Column, hitObject.Beatmap.Rank, tail: true);
            return tailTable.LateWindowFor(HitResult.Ok) + passive_poor_lifetime_margin;
        }

        return hitObject.HitWindows?.WindowFor(HitResult.Ok) ?? default_past_lifetime;
    }

    #endregion

    #region Playfield helpers

    private double currentScrollRangeScale() => playfield.ScrollRangeScale > 0 ? playfield.ScrollRangeScale : 1;

    private BmsTimingMap? getTimingMap() => playfield.TimingMap;

    #endregion

}
