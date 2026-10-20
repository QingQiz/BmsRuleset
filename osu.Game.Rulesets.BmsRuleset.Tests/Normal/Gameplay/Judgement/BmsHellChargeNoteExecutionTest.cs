using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsHellChargeNoteExecutionTest
{
    [TestCase(false)]
    [TestCase(true)]
    public void HeadPoorPublishesBodyBeforeBothSyntheticEndpoints(bool pressed)
    {
        var (context, output, notes) = create();
        var note = notes[0];
        var checkpoints = new List<string>();
        output.StatePublished = (_, state, _) =>
        {
            checkpoints.Add(state.TailJudged ? "finished" : "head");
            Assert.That(state.HasActiveBody, Is.True);
            Assert.That(output.GetMainResult(note).HasResult, Is.False);
            if (!state.TailJudged)
            {
                Assert.That(context.GetActiveBodies(), Is.Empty);
                Assert.That(context.FirstPendingNote, Is.SameAs(note));
            }
        };
        output.SyntheticEndpoint = (endpoint, after) =>
        {
            checkpoints.Add($"{endpoint.Kind}:{after}");
            Assert.That(context.GetActiveBodies(), Is.EqualTo(new[] { note }));
            Assert.That(context.GetPendingLongNoteTails(), Is.EqualTo(new[] { note }));
            Assert.That(context.GetLongNoteState(note).TailJudged, Is.False);
            Assert.That(context.FirstPendingNote, Is.Null);
            Assert.That(output.GetMainResult(note).HasResult, Is.False);
        };
        if (pressed)
            Assert.That(context.TryPressRegisteredNote(note, HitResult.Meh, new BmsPressContext(3300, new BmsFrameContext(3301, 1.5))), Is.True);
        else
            context.CheckRegisteredNotePassive(note, new BmsFrameContext(3301, 1.5));
        Assert.That(checkpoints, Is.EqualTo(new[] { "head", "Head:False", "Head:True", "Tail:False", "Tail:True", "finished" }));
        Assert.That(output.MainResults, Is.Zero);
        Assert.That(context.GetPendingLongNoteTails(), Is.Empty);
        Assert.That(context.GetActiveBodies(), Is.EqualTo(new[] { note }));
        context.UpdateRegisteredBodies(new BmsFrameContext(3301, 1.5), 301, true);
        Assert.That(output.BodyTicks, Is.Empty, "a head cannot add body time at its own timestamp");
        context.UpdateRegisteredBodies(new BmsFrameContext(3502, 1.5), 201, true);
        Assert.That(output.BodyTicks.Single(), Is.EqualTo((note, true, .5, System.Math.BitIncrement(3501))));
        context.UpdateRegisteredBodies(new BmsFrameContext(3502, 1.5), 201, true);
        context.CheckRegisteredNotePassive(note, new BmsFrameContext(3502, 1.5));
        Assert.That(output.BodyTicks, Has.Count.EqualTo(1));
        Assert.That(output.SyntheticEndpoints, Has.Count.EqualTo(2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SuccessfulTailRetainsIndependentBodyWithoutContributingAtItsTimestamp(bool scratch)
    {
        var (context, output, notes) = create(column: scratch ? 0 : 1);
        var note = notes[0];
        context.HandleNotePress(new BmsPressContext(3000, new BmsFrameContext(3000, 1)));
        if (scratch)
            context.HandleNotePress(new BmsPressContext(3980, new BmsFrameContext(3980, 1), true));
        else
            context.HandleExecutionRelease(3980, new BmsFrameContext(3980, 1));
        Assert.That(output.SyntheticEndpoints.Single().Result, Is.AnyOf(HitResult.Perfect, HitResult.Great, HitResult.Good));
        Assert.That(context.GetPendingLongNoteTails(), Is.Empty);
        Assert.That(context.GetHoldingNotes(), Is.Empty);
        Assert.That(context.GetActiveBodies(), Is.EqualTo(new[] { note }));
        Assert.That(output.BodyTicks, Has.Count.EqualTo(4));
        Assert.That(output.BodyTicks.All(tick => tick.Holding), Is.True);
        context.UpdateRegisteredBodies(new BmsFrameContext(3980, 1), 201, false);
        context.UpdateRegisteredBodies(new BmsFrameContext(3981, 1), 1, false);
        Assert.That(output.BodyTicks, Has.Count.EqualTo(4));
        context.UpdateRegisteredBodies(new BmsFrameContext(4000, 1), 19, false);
        Assert.That(context.GetActiveBodies(), Is.Empty);
        Assert.That(context.GetLongNoteState(note).TailJudged, Is.True);
        context.UpdateRegisteredLongNote(note, new BmsFrameContext(4000, 1), 19, false);
        Assert.That(context.GetLongNoteState(note).LongNoteStarted, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LargeElapsedSettlesAllTicksWithoutRepeatingThem(bool holding)
    {
        var (context, output, notes) = create();
        context.HandleNotePress(new BmsPressContext(3000, new BmsFrameContext(3000, 1)));
        context.UpdateRegisteredBodies(new BmsFrameContext(3601, 1), 601, holding);
        Assert.That(output.BodyTicks, Has.Count.EqualTo(3));
        context.UpdateRegisteredBodies(new BmsFrameContext(3601, 1), 601, holding);
        context.UpdateRegisteredLongNote(notes[0], new BmsFrameContext(3601, 1), 601, holding);
        Assert.That(output.BodyTicks, Has.Count.EqualTo(3));
        context.UpdateRegisteredBodies(new BmsFrameContext(3602, 1), 1, holding);
        Assert.That(output.BodyTicks, Has.Count.EqualTo(3));
        context.UpdateRegisteredBodies(new BmsFrameContext(3603, 1), 1, holding);
        Assert.That(output.BodyTicks.Select(t => t.Time), Is.EqualTo(new[]
        {
            System.Math.BitIncrement(3200), System.Math.BitIncrement(3400), System.Math.BitIncrement(3600),
        }));
        Assert.That(output.BodyTicks.All(t => t.Holding == holding && t.Scale == .5), Is.True);
        context.UpdateRegisteredBodies(new BmsFrameContext(3604, 1), 0, holding);
        Assert.That(output.BodyTicks, Has.Count.EqualTo(3));
    }

    [Test]
    public void EqualTimeObjectsKeepSeparateAccumulators()
    {
        var (context, output, notes) = create(2);
        var first = notes[0];
        var second = notes[1];
        context.TryPressRegisteredNote(first, HitResult.Perfect, new BmsPressContext(3000, new BmsFrameContext(3000, 1)));
        context.UpdateRegisteredBodies(new BmsFrameContext(3100, 1), 100, true);
        context.TryPressRegisteredNote(second, HitResult.Good, new BmsPressContext(3100, new BmsFrameContext(3100, 1)));
        context.UpdateRegisteredBodies(new BmsFrameContext(3201, 1), 101, true);
        Assert.That(context.GetLongNoteState(first).HeadTime, Is.EqualTo(3000));
        Assert.That(context.GetLongNoteState(second).HeadTime, Is.EqualTo(3100));
        Assert.That(output.BodyTicks.Select(t => t.Note), Is.EqualTo(new[] { first }));
        context.UpdateRegisteredBodies(new BmsFrameContext(3251, 1), 50, true);
        Assert.That(output.BodyTicks.Select(t => t.Note), Is.EqualTo(new[] { first }));

        context.UpdateRegisteredLongNote(first, new BmsFrameContext(4500, 1), 0, true);
        Assert.That(context.GetActiveBodies(), Is.EqualTo(new[] { second }));
        output.BodyTicks.Clear();
        context.UpdateRegisteredBodies(new BmsFrameContext(3351, 1), 100, true);
        Assert.That(output.BodyTicks.Select(t => t.Note), Is.EqualTo(new[] { second }));
    }

    [Test]
    public void EndingFirstBodyDoesNotSkipNextMember()
    {
        var (context, output, notes) = create(2);
        notes[1].Duration = 2000;
        foreach (var note in notes)
            context.TryPressRegisteredNote(note, HitResult.Perfect, new BmsPressContext(3000, new BmsFrameContext(3000, 1)));
        context.UpdateRegisteredBodies(new BmsFrameContext(4000, 1), 1000, true);
        Assert.That(context.GetActiveBodies(), Is.EqualTo(new[] { notes[1] }));
        Assert.That(output.BodyTicks.Count(tick => tick.Note == notes[0]), Is.EqualTo(4));
        Assert.That(output.BodyTicks.Count(tick => tick.Note == notes[1]), Is.EqualTo(4));
    }

    [Test]
    public void ScratchEndpointRetainsInputTimeWhenTheRuleIsCalledDirectly()
    {
        var (context, output, notes) = create(column: 0);
        context.HandleNotePress(new BmsPressContext(3000, new BmsFrameContext(3000, 1)));
        context.HandleNotePress(new BmsPressContext(3990, new BmsFrameContext(3997, 1.5), true));
        var endpoint = output.SyntheticEndpoints.Single();
        Assert.That(endpoint.EventTime, Is.EqualTo(3990));
        Assert.That(endpoint.ApplicationTime, Is.EqualTo(3990));
        Assert.That(output.SyntheticApplicationTimes.Single(), Is.EqualTo(3997));
        Assert.That(context.GetActiveBodies(), Is.EqualTo(notes));
    }

    [Test]
    public void BodyOrderUsesStableChartOrder()
    {
        var (context, output, notes) = create(2);
        foreach (var note in notes.Reverse())
            context.TryPressRegisteredNote(note, HitResult.Perfect, new BmsPressContext(3000, new BmsFrameContext(3000, 1)));
        context.UpdateRegisteredBodies(new BmsFrameContext(3201, 1), 201, true);
        Assert.That(output.BodyTicks.Select(t => t.Note), Is.EqualTo(notes));
        output.BodyTicks.Clear();
        context.UpdateRegisteredBodies(new BmsFrameContext(3402, 1), 201, true);
        Assert.That(output.BodyTicks.Select(t => t.Note), Is.EqualTo(notes));
    }

    private static (BmsColumnJudgementRuntime Context, BmsRecordingJudgementOutput Output, BmsLongNote[] Notes) create(int count = 1, int column = 1)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, LockedLongNoteMode = BmsLongNoteMode.HellChargeNote, Rank = 2 };
        var notes = Enumerable.Range(0, count).Select(_ => new BmsLongNote { Beatmap = beatmap, Column = column, StartTime = 3000, Duration = 1000 }).ToArray();
        var output = new BmsRecordingJudgementOutput();
        var context = new BmsJudgementEngine(notes, beatmap.LayoutVariant, column, output).GetColumn(column);

        return (context, output, notes);
    }
}
