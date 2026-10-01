using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsAutoScratchEngineTest
{
    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void AutomaticEndpointsUseSimulationStepsWithoutPhysicalMineInput(BmsLongNoteMode mode)
    {
        var (engine, column, note, mine, output) = create(mode);
        var samples = 0;
        output.Feedback = _ => samples++;
        engine.ProcessFrame(new BmsFrameContext(1000, 1), 1);
        Assert.That(column.GetLongNoteState(note).HeadTime, Is.EqualTo(1000));
        Assert.That(column.IsAutomaticallyHeld(note), Is.True);
        Assert.That(output.BodyTicks, Is.Empty);
        Assert.That(samples, Is.EqualTo(1));

        engine.QueueInput(1201, 0, true, false);
        engine.QueueInput(1201, 0, true, true);
        engine.ProcessFrame(new BmsFrameContext(1201, 1), 201);
        Assert.That(column.PressedDirections, Is.Zero);
        Assert.That(column.GetLandmineState(mine).Detonated, Is.False);
        Assert.That(output.BodyTicks.Count, Is.EqualTo(mode == BmsLongNoteMode.HellChargeNote ? 1 : 0));
        Assert.That(output.BodyTicks.All(t => t.Holding && t.Time == 1201), Is.True);

        engine.ProcessFrame(new BmsFrameContext(2000, 1.5), 799);
        Assert.That(column.GetLongNoteState(note).TailResult, Is.EqualTo(HitResult.Perfect));
        Assert.That(column.IsAutomaticallyHeld(note), Is.False);
        Assert.That(samples, Is.EqualTo(2));
        var tail = mode == BmsLongNoteMode.LongNote ? output.GetEndpoints(note).Last() : output.SyntheticEndpoints.Single();
        Assert.That(tail.EventTime, Is.EqualTo(2000));
        Assert.That(tail.GameplayRate, Is.EqualTo(1.5));
        engine.ProcessFrame(new BmsFrameContext(2000, 1.5), 799);
        Assert.That(samples, Is.EqualTo(2));
        Assert.That(output.MainResults, Is.EqualTo(1));
        Assert.That(output.SyntheticEndpoints.Count, Is.EqualTo(mode == BmsLongNoteMode.LongNote ? 0 : 1));
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void RebindingResetsAutomaticHoldUntilTheNextStepAndRewindRestoresIt(BmsLongNoteMode mode)
    {
        var (engine, column, note, _, output) = create(mode);
        engine.ProcessFrame(new BmsFrameContext(1000, 1), 1);
        var state = column.GetLongNoteState(note);
        Assert.That(column.Schedule.TryGetCurrent(note, out var previous), Is.True);
        column.ResetAutomaticHold(note);
        column.Schedule.Unbind(previous);
        column.Schedule.Bind(note);
        engine.ProcessFrame(new BmsFrameContext(1000, 1), 0);
        Assert.That(column.IsAutomaticallyHeld(note), Is.False);
        Assert.That(column.GetLongNoteState(note), Is.EqualTo(state));

        engine.ProcessFrame(new BmsFrameContext(1201, 1), 201);
        Assert.That(column.IsAutomaticallyHeld(note), Is.True);
        Assert.That(output.BodyTicks.All(t => t.Holding), Is.True);
        engine.ProcessFrame(new BmsFrameContext(2000, 1), 799);
        engine.ProcessFrame(new BmsFrameContext(1201, 1), -799);
        output.RevertAfter(1201);
        output.RevertSyntheticAfter(1201);
        Assert.That(column.Schedule.BeginRewindStep(new BmsFrameContext(1201, 1)), Is.True);
        column.RestoreExecutionState(new BmsFrameContext(1201, 1));
        Assert.That(column.IsAutomaticallyHeld(note), Is.True);
        Assert.That(column.GetLongNoteState(note).TailJudged, Is.False);
        engine.ProcessFrame(new BmsFrameContext(2000, 1), 799);
        Assert.That(column.GetLongNoteState(note).TailResult, Is.EqualTo(HitResult.Perfect));
        Assert.That(mode == BmsLongNoteMode.LongNote ? output.GetEndpoints(note).Count : output.SyntheticEndpoints.Count,
            Is.EqualTo(mode == BmsLongNoteMode.LongNote ? 2 : 1));
    }

    [Test]
    public void ResumeLeadInFreezesAutomaticEndpoints()
    {
        var (engine, column, note, _, output) = create(BmsLongNoteMode.HellChargeNote);
        engine.ProcessFrame(new BmsFrameContext(1000, 1), 1, resumeRewinding: true);
        Assert.That(column.GetLongNoteState(note).HeadJudged, Is.False);
        Assert.That(output.MainResults, Is.Zero);
        engine.ProcessFrame(new BmsFrameContext(1000, 1), 0);
        Assert.That(column.GetLongNoteState(note).IsHolding, Is.True);
        engine.CompleteLongNotesAtPause(new BmsFrameContext(1100, 1));
        var completed = column.GetLongNoteState(note);
        engine.ProcessFrame(new BmsFrameContext(1500, 1), 400, resumeRewinding: true);
        Assert.That(column.GetLongNoteState(note), Is.EqualTo(completed));
        Assert.That(column.IsAutomaticallyHeld(note), Is.False);
        Assert.That(output.BodyTicks, Is.Empty);
    }

    private static (BmsJudgementEngine Engine, BmsColumnJudgementRuntime Column, BmsLongNote Note, BmsLandmine Mine, BmsRecordingJudgementOutput Output)
        create(BmsLongNoteMode mode)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2, LockedLongNoteMode = mode };
        var note = new BmsLongNote { Beatmap = beatmap, Column = 0, StartTime = 1000, Duration = 1000, SampleKey = 11, TailSampleKey = 12 };
        var mine = new BmsLandmine { Beatmap = beatmap, Column = 0, StartTime = 1201 };
        var output = new BmsRecordingJudgementOutput();
        var engine = new BmsJudgementEngine([note, mine], beatmap.LayoutVariant, 0, false, output);
        var column = engine.GetColumn(0);
        column.ActivateNote(note);
        column.Schedule.Bind(note);
        column.ActivateLandmine(mine);
        engine.EnableAutoScratch();
        return (engine, column, note, mine, output);
    }
}
