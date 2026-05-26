using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Replays;

namespace osu.Game.Rulesets.BmsRuleset.Replays;

public class BmsAutoGenerator(BmsBeatmap beatmap) : AutoGenerator<BmsReplayFrame>(beatmap)
{
    public const double RELEASE_DELAY = 20;

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

        if (current.IsLongNote)
            return current.Duration > 0 ? endTime : endTime + 1;

        return nextObject == null || nextObject.StartTime > endTime + RELEASE_DELAY
            ? endTime + RELEASE_DELAY
            : endTime + (nextObject.StartTime - endTime) * 0.9;
    }

    private IEnumerable<ActionPoint> generateActionPoints()
    {
        for (var i = 0; i < Beatmap.HitObjects.Count; i++)
        {
            var current = Beatmap.HitObjects[i];

            if (BmsKeyBindingConfiguration.ActionForColumn(Beatmap.LayoutVariant, current.Column) is not { } action)
                continue;

            yield return new ActionPoint(current.StartTime, action, true);
            yield return new ActionPoint(calculateReleaseTime(current, GetNextObject(i)), action, false);
        }
    }
}
