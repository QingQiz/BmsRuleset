using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Replays;

namespace osu.Game.Rulesets.BmsRuleset.Replays;

public class BmsAutoGenerator(BmsBeatmap beatmap, bool scratchOnly = false) : AutoGenerator<BmsReplayFrame>(beatmap)
{
    public const double RELEASE_DELAY = 10;

    // ReSharper disable once InconsistentNaming
    private new BmsBeatmap Beatmap => (BmsBeatmap)base.Beatmap;

    internal readonly record struct ActionPoint(double Time, BmsAction Action, bool Press);

    protected override void GenerateFrames()
    {
        var active = new List<BmsAction>();

        foreach (var group in GenerateActionPoints().GroupBy(p => p.Time).OrderBy(g => g.Key))
        {
            foreach (var point in group.OrderBy(p => p.Press))
            {
                if (point.Press)
                {
                    if (!active.Contains(point.Action))
                        active.Add(point.Action);
                }
                else
                    active.Remove(point.Action);
            }

            Frames.Add(new BmsReplayFrame(group.Key, active.ToArray()) { IncludesGeneratedInputs = Frames.Count == 0 });
        }
    }

    protected override HitObject? GetNextObject(int currentIndex)
    {
        var column = Beatmap.HitObjects[currentIndex].Column;

        for (var i = currentIndex + 1; i < Beatmap.HitObjects.Count; i++)
        {
            if (Beatmap.HitObjects[i].Column == column)
                return Beatmap.HitObjects[i];
        }

        return null;
    }

    private static double calculateReleaseTime(BmsHitObject current, HitObject? nextObject)
    {
        var endTime = current.GetEndTime();

        if (current is BmsLongNote)
        {
            // LN body must be held to endTime exactly; if a mine in the same column fires
            // at or right after the tail, release a moment earlier so the column isn't pressed.
            if (nextObject is BmsLandmine mineAfterLn && mineAfterLn.StartTime <= endTime + 1)
                return Math.Max(current.StartTime, mineAfterLn.StartTime - 1);

            // IN can create sub-millisecond holds. Extending them by the tap release delay
            // swallows the next press in the same column and also mistimes charge-note tails.
            return endTime;
        }

        return calculateTapReleaseTime(endTime, nextObject?.StartTime);
    }

    private static double calculateTapReleaseTime(double pressTime, double? nextPressTime)
        // A tail reversal is a tap too: it must stop before the next head or mine, even
        // for sub-millisecond gaps, so the following reversal can form a new key-down.
        => nextPressTime == null || nextPressTime <= pressTime
            ? pressTime + RELEASE_DELAY
            : Math.Min(pressTime + RELEASE_DELAY, pressTime + (nextPressTime.Value - pressTime) * 0.9);

    internal IEnumerable<ActionPoint> GenerateActionPoints()
    {
        for (var i = 0; i < Beatmap.HitObjects.Count; i++)
        {
            var current = Beatmap.HitObjects[i];

            if (scratchOnly && !BmsLayout.IsScratchColumn(current.Column, Beatmap.LayoutVariant))
                continue;

            // Autoplay must never press a mine: doing so detonates it (POOR judgement).
            // Mines are passive — leave the column untouched at their StartTime.
            if (current is BmsLandmine)
                continue;

            if (BmsKeyBindingConfiguration.ActionForColumn(Beatmap.LayoutVariant, current.Column) is not { } action)
                continue;

            yield return new ActionPoint(current.StartTime, action, true);

            var nextObject = GetNextObject(i);
            var releaseTime = calculateReleaseTime(current, nextObject);
            yield return new ActionPoint(releaseTime, action, false);

            if (current is BmsLongNote
                && Beatmap.LockedLongNoteMode is BmsLongNoteMode.ChargeNote or BmsLongNoteMode.HellChargeNote
                && BmsKeyBindingConfiguration.ReverseScratchAction(action) is { } reverseAction)
            {
                yield return new ActionPoint(releaseTime, reverseAction, true);
                // A same-time head uses the forward direction; release the reversal before its tail,
                // rather than at its head where release-before-press ordering would leave it stuck.
                var nextReversalTime = nextObject is BmsLongNote nextHold && nextHold.StartTime <= releaseTime
                    ? nextHold.EndTime : nextObject?.StartTime;
                yield return new ActionPoint(calculateTapReleaseTime(releaseTime, nextReversalTime), reverseAction, false);
            }
        }
    }
}
