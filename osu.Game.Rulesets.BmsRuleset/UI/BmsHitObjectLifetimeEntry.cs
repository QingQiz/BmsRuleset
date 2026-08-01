using System;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.Objects;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI;

internal sealed class BmsHitObjectLifetimeEntry(
    HitObject hitObject,
    BmsScrollController scrollController,
    Func<double> getVisualOffset)
    : HitObjectLifetimeEntry(hitObject)
{

    /// <summary>
    ///     Set to true once <see cref="RefreshLifetime"/> has run.
    ///     Guards against framework code overwriting our
    ///     scroll-speed-aware computed values with blanket defaults.
    /// </summary>
    private bool lifetimeComputed;

    private double? lifetimeStartWithoutVisualOffset;

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
    public void RefreshLifetime(double? currentTime = null)
    {
        if (HitObject is not BmsHitObject hitObject)
            return;

        var futureLifetime = computeFutureLifetime(hitObject);
        var pastLifetime = computePastLifetime();
        var slowWindow = getSlowWindow(hitObject);
        var baseLifetimeStart = hitObject.StartTime - futureLifetime;
        lifetimeStartWithoutVisualOffset = baseLifetimeStart;
        var lifetimeStart = computeLifetimeStartWithVisualOffset(baseLifetimeStart);

        if (currentTime is double now && now >= LifetimeStart && now <= LifetimeEnd)
            lifetimeStart = Math.Min(lifetimeStart, now);

        lifetimeComputed = false;

        // Set LifetimeEnd before LifetimeStart.  Setting LifetimeStart first
        // with the current LifetimeEnd would produce an intermediate
        // MaxValue that the framework may latch on to before the follow-up
        // LifetimeEnd set corrects it.
        LifetimeEnd = hitObject is BmsLandmine
            ? hitObject.StartTime + mine_past_lifetime
            : hitObject.GetEndTime() + Math.Max(pastLifetime, slowWindow + lifetime_margin);
        LifetimeStart = lifetimeStart;

        lifetimeComputed = true;
    }

    public void ApplyVisualOffset()
    {
        if (lifetimeStartWithoutVisualOffset is not { } baseLifetimeStart)
        {
            RefreshLifetime();
            return;
        }

        var lifetimeStart = Math.Min(LifetimeStart, computeLifetimeStartWithVisualOffset(baseLifetimeStart));

        lifetimeComputed = false;
        LifetimeStart = lifetimeStart;
        lifetimeComputed = true;
    }

    #endregion

    #region Guard overrides: protect computed lifetimes from framework overwrites

    /// <summary>
    ///     Once <see cref="RefreshLifetime"/> has computed a scroll-speed-aware
    ///     value, reject subsequent overwrites from
    ///     <c>HitObjectLifetimeEntry.SetInitialLifetime</c>
    ///     (triggered by <c>DefaultsApplied</c> / <c>StartTimeBindable</c>)
    ///     that would reset <c>LifetimeStart</c> to the default offset.
    /// </summary>
    protected override void SetLifetimeStart(double start)
    {
        if (!lifetimeComputed)
            base.SetLifetimeStart(start);
    }

    /// <summary>
    ///     <c>DrawableHitObject.UpdateState</c> unconditionally sets
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
        // E-POOR is timed against the next note without consuming it, so the candidate must exist
        // throughout the earlier miss row as well as the hittable BAD window.
        var floor = Math.Max(getFastInputWindow(hitObject), minimum_future_lifetime);
        var timingMap = getTimingMap();

        if (useConstantScrollFallback(timingMap))
            return Math.Max(floor, computeConstantScrollFutureLifetime());

        var visibleTime = findEarliestVisibleWindowStart(hitObject, timingMap!);

        return !double.IsFinite(visibleTime)
            ? Math.Max(floor, computeConstantScrollFutureLifetime())
            : Math.Max(floor, hitObject.StartTime - visibleTime + lifetime_margin);
    }

    /// <summary>
    ///     The furthest fast-side input window for this object's head judgement. This includes the
    ///     non-consuming E-POOR row so it can still be associated with the upcoming note.
    /// </summary>
    private static double getFastInputWindow(BmsHitObject hitObject)
    {
        if (hitObject is BmsLandmine) return 0;

        var table = BmsJudgementProfileProvider.GetTable(hitObject.Beatmap.LayoutVariant, hitObject.Column, hitObject.EffectiveJudgementRate, tail: false);
        return Math.Max(table.FastWindowFor(HitResult.Ok), table.FastWindowFor(HitResult.Miss));
    }

    private bool useConstantScrollFallback(BmsTimingMap? timingMap) => scrollController.ConstantScrollActive || timingMap == null;

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
        // Fast notes can enter during gameplay lead-in, while the maximum supported scroll window
        // keeps the search bounded when a stationary timing segment remains visible indefinitely.
        var earliestSearchTime = Math.Min(0, hitObject.StartTime
                                             - BmsScrollController.MAX_TIME_RANGE * currentScrollRangeScale() * scrollController.PlaybackRate);

        for (var probeTime = hitObject.StartTime; probeTime > earliestSearchTime;)
        {
            var nextProbeTime = Math.Max(earliestSearchTime, probeTime - visible_window_search_step);
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

        return BmsScrollController.ComputeScrollTime(BmsRulesetConfigManager.DEFAULT_SCROLL_SPEED)
               * currentScrollRangeScale()
               * scrollController.PlaybackRate
               / Math.Max(0.001, scrollController.ScrollSpeed / BmsRulesetConfigManager.DEFAULT_SCROLL_SPEED * speedFactor);
    }

    /// <summary>
    ///     Fallback lifetime used in constant-scroll mode or when the timing map is unavailable.
    ///     Based purely on the scroll speed without any timing-map segment lookup.
    /// </summary>
    private double computeConstantScrollFutureLifetime()
    {
        var speed = Math.Max(0.001, scrollController.ScrollSpeed);
        return BmsScrollController.ComputeScrollTime(speed) * currentScrollRangeScale() * scrollController.PlaybackRate + lifetime_margin;
    }

    #endregion

    #region Past lifetime (how long after EndTime the entry stays alive)

    /// <summary>
    ///     Baseline past lifetime: note stays visible for this long after the judgement line.
    /// </summary>
    private static double computePastLifetime() => default_past_lifetime + lifetime_margin;

    /// <summary>
    ///     The BAD (slow) hit-window for this object. The entry must stay alive beyond this boundary
    ///     so its final drawable update can apply the passive result before lifetime removal begins.
    /// </summary>
    private static double getSlowWindow(BmsHitObject hitObject)
    {
        if (hitObject is BmsLongNote)
        {
            var tailTable = BmsJudgementProfileProvider.GetTable(hitObject.Beatmap.LayoutVariant, hitObject.Column, hitObject.EffectiveJudgementRate, tail: true);
            return tailTable.SlowWindowFor(HitResult.Ok) + passive_poor_lifetime_margin;
        }

        var table = BmsJudgementProfileProvider.GetTable(hitObject.Beatmap.LayoutVariant, hitObject.Column, hitObject.EffectiveJudgementRate, tail: false);
        return table.SlowWindowFor(HitResult.Ok) + passive_poor_lifetime_margin;
    }

    #endregion

    #region Playfield helpers

    private double currentScrollRangeScale() => scrollController.ScrollRangeScale > 0 ? scrollController.ScrollRangeScale : 1;

    private BmsTimingMap? getTimingMap() => scrollController.TimingMap;

    private double computeLifetimeStartWithVisualOffset(double baseLifetimeStart) =>
        baseLifetimeStart - Math.Max(0, getVisualOffset()) * scrollController.PlaybackRate;

    #endregion

}
