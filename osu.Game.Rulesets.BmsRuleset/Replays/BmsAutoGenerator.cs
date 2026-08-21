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

            return Math.Max(endTime, current.StartTime + RELEASE_DELAY);
        }

        // Non-LN: prefer the default RELEASE_DELAY, but pull the release in to before
        // the next same-column object so we don't accidentally hold a key into a mine.
        var maxHoldUntil = nextObject is BmsLandmine mine
            ? mine.StartTime - 1
            : double.PositiveInfinity;

        return nextObject == null || nextObject.StartTime > endTime + RELEASE_DELAY
            ? Math.Min(endTime + RELEASE_DELAY, maxHoldUntil)
            : Math.Min(endTime + (nextObject.StartTime - endTime) * 0.9, maxHoldUntil);
    }

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
            yield return new ActionPoint(calculateReleaseTime(current, GetNextObject(i)), action, false);
        }
    }
}
