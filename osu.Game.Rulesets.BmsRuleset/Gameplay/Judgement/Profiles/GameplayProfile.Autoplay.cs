using System;
using System.Collections.Generic;
using System.Linq;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Patterns;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;

internal abstract partial class GameplayProfile
{
    internal virtual INoteRule AutoplayRule(INoteRule rule) => rule;

    internal virtual IEnumerable<BmsAutoGenerator.ActionPoint> GenerateAutoplayActions(BmsBeatmap beatmap, BmsEngineChart compiled)
    {
        // Keep source order for simultaneous inputs, and source precision for sub-microsecond holds.
        var notes = beatmap.HitObjects.Where(compiled.Ids.ContainsKey).ToArray();
        for (var i = 0; i < notes.Length; i++)
        {
            var current = notes[i];
            var rule = AutoplayRule(compiled.Chart.Notes[compiled.Ids[current]].Rule);
            if (rule is MineRule)
                continue;

            if (rule is not (TapRule { Configuration.OnRelease: false } or HoldRule { Configuration.RequiresRetrigger: false }))
                throw new NotSupportedException($"Autoplay for {rule.GetType().Name} must be supplied by the selected gameplay profile.");

            if (BmsKeyBindingConfiguration.ActionForColumn(beatmap.LayoutVariant, current.Column) is not { } action)
                continue;

            yield return new BmsAutoGenerator.ActionPoint(current.StartTime, action, true);

            var next = nextObject(i);
            var hold = rule as HoldRule;
            var releaseTime = hold == null
                ? tapReleaseTime(current.StartTime, next?.StartTime)
                : ((BmsLongNote)current).EndTime + (hold.Configuration.EndMode == HoldEndMode.Automatic
                    ? hold.Configuration.AutomaticEndOffset / 1000.0
                    : 0);

            if (hold != null && next != null && AutoplayRule(compiled.Chart.Notes[compiled.Ids[next]].Rule) is MineRule && next.StartTime <= releaseTime + 1)
                releaseTime = Math.Max(current.StartTime, next.StartTime - 1);

            yield return new BmsAutoGenerator.ActionPoint(releaseTime, action, false);

            if (hold?.Configuration.TailOnOppositeControl == true
                && BmsKeyBindingConfiguration.ReverseScratchAction(action) is { } reverseAction)
            {
                yield return new BmsAutoGenerator.ActionPoint(releaseTime, reverseAction, true);

                // A same-time head uses the forward direction. Release its predecessor's reversal
                // before the next tail so that the next opposite-control input is a fresh press.
                var nextReversalTime = next is BmsLongNote nextHold && nextHold.StartTime <= releaseTime
                    ? nextHold.EndTime
                    : next?.StartTime;
                yield return new BmsAutoGenerator.ActionPoint(tapReleaseTime(releaseTime, nextReversalTime), reverseAction, false);
            }
        }

        BmsHitObject? nextObject(int currentIndex)
        {
            for (var i = currentIndex + 1; i < notes.Length; i++)
            {
                if (notes[i].Column == notes[currentIndex].Column)
                    return notes[i];
            }

            return null;
        }
    }

    private static double tapReleaseTime(double pressTime, double? nextPressTime)
        => nextPressTime == null || nextPressTime <= pressTime
            ? pressTime + 10
            : Math.Min(pressTime + 10, pressTime + (nextPressTime.Value - pressTime) * 0.9);
}
