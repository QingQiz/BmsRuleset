using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Replays;

namespace osu.Game.Rulesets.BmsRuleset.Replays;

public class BmsAutoGenerator(BmsBeatmap beatmap) : AutoGenerator<BmsReplayFrame>(beatmap)
{
    public const double RELEASE_DELAY = 10;

    // ReSharper disable once InconsistentNaming
    private new BmsBeatmap Beatmap => (BmsBeatmap)base.Beatmap;

    private readonly record struct ActionPoint(double Time, BmsAction Action, bool Press);

    protected override void GenerateFrames()
    {
        var active = new List<BmsAction>();

        foreach (var group in generateActionPoints().GroupBy(p => p.Time).OrderBy(g => g.Key))
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

            Frames.Add(new BmsReplayFrame(group.Key, active.ToArray()));
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

        return calculateTapReleaseTime(endTime, nextObject);
    }

    private static double calculateTapReleaseTime(double pressTime, HitObject? nextObject)
        // A tail reversal is a tap too: it must stop before the next head or mine, even
        // for sub-millisecond gaps, so the following reversal can form a new key-down.
        => nextObject == null
            ? pressTime + RELEASE_DELAY
            : Math.Min(pressTime + RELEASE_DELAY, pressTime + (nextObject.StartTime - pressTime) * 0.9);

    private IEnumerable<ActionPoint> generateActionPoints()
    {
        for (var i = 0; i < Beatmap.HitObjects.Count; i++)
        {
            var current = Beatmap.HitObjects[i];

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
                yield return new ActionPoint(calculateTapReleaseTime(releaseTime, nextObject), reverseAction, false);
            }
        }
    }
}
