#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.Replays;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

/// <summary>
/// Replay-frame factories for the BMS visual test scenes:
/// perfect autoplay, autoplay with a fixed offset, an empty "watch only"
/// replay, and a deliberately varied replay that exercises every hit result.
/// </summary>
public static partial class BmsTestReplays
{
    /// <summary>Time appended to a press to produce its release in non-LN scenarios.</summary>
    public const double RELEASE_PADDING_MS = 20;

    /// <summary>How early the deliberate empty-POOR press fires before the first note.</summary>
    public const double EMPTY_POOR_LEAD_MS = 1200;

    /// <summary>Press offsets cycled per non-LN note to drive each hit result.</summary>
    private static readonly double[] judgement_offsets =
    [
        0,    // PGREAT
        25,   // GREAT
        70,   // GOOD
        150,  // BAD
        -500, // POOR
    ];

    private readonly record struct ActionPoint(double Time, BmsAction Action, bool Press);

    /// <summary>
    /// Generates perfect autoplay replay frames via <see cref="BmsAutoGenerator"/>.
    /// </summary>
    public static IList<ReplayFrame> CreateAutoPlayFrames(BmsBeatmap beatmap)
        => new BmsAutoGenerator(beatmap).Generate().Frames;

    /// <summary>
    /// Generates autoplay replay frames with a uniform time offset applied to every frame.
    /// Useful for producing non-perfect judgements from an otherwise perfect autoplay.
    /// </summary>
    public static IList<ReplayFrame> CreateOffsetAutoPlayFrames(BmsBeatmap beatmap, double offset)
        => new BmsAutoGenerator(beatmap).Generate().Frames
            .Select(ReplayFrame (f) =>
            {
                if (f is not BmsReplayFrame bms)
                    throw new InvalidOperationException($"BmsAutoGenerator produced unexpected frame type {f.GetType().Name}.");

                return new BmsReplayFrame(bms.Time + offset, bms.Actions.ToArray());
            })
            .ToList();

    /// <summary>
    /// Generates an empty replay so notes scroll without being hit — useful for
    /// visual inspection of the playfield and skin without gameplay interference.
    /// </summary>
    public static IList<ReplayFrame> CreateWatchOnlyFrames(BmsBeatmap beatmap)
        => new List<ReplayFrame> { new BmsReplayFrame(0) };

    /// <summary>
    /// Autoplays every note before <paramref name="idleFromTime"/> perfectly, then stops
    /// pressing so the remaining notes miss. Pre-filling every tracked gauge gives the
    /// groove tiers an HP buffer they lack from their 0.2 start, isolating the Auto Gauge
    /// cascade: survival tiers fail in order and the active gauge steps down toward Normal.
    /// </summary>
    public static IList<ReplayFrame> CreateAutoPlayThenIdleFrames(BmsBeatmap beatmap, double idleFromTime)
    {
        var actionPoints = new List<ActionPoint>();

        foreach (var hitObject in beatmap.HitObjects.OrderBy(h => h.StartTime))
        {
            if (hitObject.StartTime >= idleFromTime)
                break;

            // Mines are passive when left unpressed; skip them so the autoplay portion
            // doesn't detonate them and skew the gauge.
            if (hitObject is BmsLandmine)
                continue;

            if (BmsKeyBindingConfiguration.ActionForColumn(beatmap.LayoutVariant, hitObject.Column) is not { } action)
                continue;

            var releaseTime = (hitObject is BmsLongNote ln ? ln.EndTime : hitObject.StartTime) + RELEASE_PADDING_MS;
            actionPoints.Add(new ActionPoint(hitObject.StartTime, action, true));
            actionPoints.Add(new ActionPoint(releaseTime, action, false));
        }

        return materialise(actionPoints);
    }

    /// <summary>
    /// Generates replay frames with deliberate timing offsets so that every
    /// <see cref="BmsRuleset.STATIC_VALID_HIT_RESULTS"/> hit result is produced,
    /// including a deliberate Empty-POOR. Long notes use scenario-based timing
    /// (early/late press, mid release, no release, etc.).
    /// </summary>
    public static IList<ReplayFrame> CreateReplayFrames(BmsBeatmap beatmap)
    {
        var hitObjects = beatmap.HitObjects
            .OrderBy(h => h.StartTime)
            .ToArray();

        var actionPoints = new List<ActionPoint>();

        // One deliberate empty POOR before the first note's POOR window, so HitResult.Miss is visible too.
        addPress(actionPoints, hitObjects[0].StartTime - EMPTY_POOR_LEAD_MS, hitObjects[0]);

        for (var i = 0; i < hitObjects.Length; i++)
        {
            var hitObject = hitObjects[i];
            var action = BmsKeyBindingConfiguration.ActionForColumn(beatmap.LayoutVariant, hitObject.Column);

            if (action == null)
                continue;

            if (hitObject is BmsLongNote ln && tryAddLongNoteScenario(actionPoints, ln, action.Value))
                continue;

            var time = hitObject.StartTime + judgement_offsets[i % judgement_offsets.Length];
            var releaseTime = (hitObject is BmsLongNote ln2 ? ln2.EndTime : time) + RELEASE_PADDING_MS;

            actionPoints.Add(new ActionPoint(time, action.Value, true));
            actionPoints.Add(new ActionPoint(releaseTime, action.Value, false));
        }

        return materialise(actionPoints);
    }

    private static IList<ReplayFrame> materialise(List<ActionPoint> actionPoints)
    {
        var activeActions = new List<BmsAction>();
        var frames = new List<ReplayFrame> { new BmsReplayFrame(0) };

        foreach (var group in actionPoints.GroupBy(p => p.Time).OrderBy(g => g.Key))
        {
            // process releases before presses at the same instant so a tap+release pair is observable
            foreach (var point in group.OrderBy(p => p.Press))
            {
                if (point.Press)
                {
                    if (!activeActions.Contains(point.Action))
                        activeActions.Add(point.Action);
                }
                else
                    activeActions.Remove(point.Action);
            }

            frames.Add(new BmsReplayFrame(group.Key, activeActions.ToArray()));
        }

        return frames;
    }

    private static void addPress(List<ActionPoint> actionPoints, double time, BmsHitObject hitObject)
    {
        if (BmsKeyBindingConfiguration.ActionForColumn(BmsLayoutVariant.Bme7K, hitObject.Column) is not { } action)
            return;

        actionPoints.Add(new ActionPoint(time, action, true));
        actionPoints.Add(new ActionPoint(time + RELEASE_PADDING_MS, action, false));
    }

    private static bool tryAddLongNoteScenario(List<ActionPoint> actionPoints, BmsLongNote hitObject, BmsAction action)
    {
        var index = (int)Math.Round((hitObject.StartTime - BmsTestBeatmaps.LN_SCENARIO_START_TIME) / BmsTestBeatmaps.LN_SCENARIO_SPACING);

        if (!Enum.IsDefined(typeof(LnScenario), index))
            return false;

        var expectedStartTime = BmsTestBeatmaps.LN_SCENARIO_START_TIME + index * BmsTestBeatmaps.LN_SCENARIO_SPACING;

        if (Math.Abs(hitObject.StartTime - expectedStartTime) > 0.001)
            return false;

        switch ((LnScenario)index)
        {
            case LnScenario.EarlyPress:
                addHold(actionPoints, action, hitObject.StartTime - 150, hitObject.EndTime + RELEASE_PADDING_MS);
                break;

            case LnScenario.LatePress:
                addHold(actionPoints, action, hitObject.StartTime + 150, hitObject.EndTime + RELEASE_PADDING_MS);
                break;

            case LnScenario.NoPress:
                break;

            case LnScenario.MidRelease:
                addHold(actionPoints, action, hitObject.StartTime, hitObject.StartTime + hitObject.Duration / 2);
                break;

            case LnScenario.NoRelease:
                actionPoints.Add(new ActionPoint(hitObject.StartTime, action, true));
                break;

            case LnScenario.LateRelease:
                addHold(actionPoints, action, hitObject.StartTime, hitObject.EndTime + 150);
                break;

            case LnScenario.EarlyRelease:
                addHold(actionPoints, action, hitObject.StartTime, hitObject.EndTime - 150);
                break;
        }

        return true;
    }

    private static void addHold(List<ActionPoint> actionPoints, BmsAction action, double pressTime, double releaseTime)
    {
        actionPoints.Add(new ActionPoint(pressTime, action, true));
        actionPoints.Add(new ActionPoint(releaseTime, action, false));
    }

    public enum LnScenario
    {
        EarlyPress,
        LatePress,
        NoPress,
        MidRelease,
        NoRelease,
        LateRelease,
        EarlyRelease,
    }
}
