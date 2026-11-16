using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using System;
using System.Collections.Generic;
using System.Linq;
using RhythmEngine.Profiles.Bms;
using RhythmEngine.Profiles.Common;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

internal readonly record struct BmsJudgementCandidate(
    double StartTime,
    double EndTime,
    int Column,
    double JudgementRate,
    bool IsLongNote,
    bool IsJudged = false,
    bool HasMistake = false);

internal readonly record struct BmsJudgementSelection(BmsJudgementCandidate? Candidate, HitResult Result, bool IsEmptyPoor);

internal static class BmsTestJudgementSelector
{
    internal static BmsJudgementSelection SelectPress(BmsLayoutVariant layout, int column,
                                                      IEnumerable<BmsJudgementCandidate> candidates, double inputTime, BmsJudgementAlgorithm? algorithm = BmsJudgementAlgorithm.Combo,
                                                      BmsJudgementModifiers modifiers = BmsJudgementModifiers.None)
    {
        var ordered = candidates.OrderBy(c => c.StartTime).ThenBy(c => c.Column).ToArray();
        var beatmap = new BmsBeatmap { LayoutVariant = layout };
        var objects = ordered.Select(c =>
        {
            BmsHitObject note = c.IsLongNote ? new BmsLongNote { Duration = c.EndTime - c.StartTime } : new BmsNote();
            note.StartTime = c.StartTime;
            note.Column = c.Column;
            note.ExRank = c.JudgementRate * 100 / BmsEngineRules.RateForRank(layout, 2);
            note.Beatmap = beatmap;
            return note;
        }).ToArray();
        var output = new BmsRecordingJudgementOutput();
        var engine = BmsEngineTestAccess.CreateEngine(objects, layout, column, output, new BmsGameplayProfile(modifiers));

        foreach (var candidate in ordered.Where(c => c.Column == column && (c.IsJudged || c.HasMistake)))
        {
            var time = candidate.IsJudged ? candidate.StartTime : candidate.StartTime - 180;
            engine.QueueInput(time, column, true, false);
            engine.QueueInput(time + .001, column, false, false);
            engine.ProcessFrame(new BmsFrameContext(time + .001, 1), 0, algorithm);
        }

        var selection = new BmsJudgementSelection(null, HitResult.None, false);
        output.AfterResultApplied = (note, frame) =>
        {
            if (Math.Abs(frame.SimulationTime - inputTime) < .0005)
                selection = new BmsJudgementSelection(ordered[Array.IndexOf(objects, note)], output.GetMainResult(note).Type, false);
        };
        output.NonConsumingJudgement = (expected, lane, result) =>
            selection = new BmsJudgementSelection(ordered.First(c => c.StartTime == expected && c.Column == lane), result, result == HitResult.Miss);
        output.StatePublished = (note, state, _) =>
        {
            if (note.Kind == KeyNoteKind.Hold && engine.GetColumn(column).GetNote(note).Parts[0].Time / 1000.0 == inputTime && !state.TailJudged)
                selection = new BmsJudgementSelection(ordered[Array.IndexOf(objects, note)], state.HeadResult, false);
        };
        engine.QueueInput(inputTime, column, true, false);
        engine.ProcessFrame(new BmsFrameContext(inputTime, 1), 0, algorithm);
        return selection;
    }
}
