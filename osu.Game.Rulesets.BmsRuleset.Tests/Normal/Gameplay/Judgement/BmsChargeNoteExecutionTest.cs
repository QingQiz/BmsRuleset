using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsChargeNoteExecutionTest
{
    [TestCase(false)]
    [TestCase(true)]
    public void MainResultKeepsIndependentTailUntilReleaseOrPassive(bool passive)
    {
        var (context, output, note) = create();
        var checkpoints = new List<string>();
        output.StatePublished = (_, state, _) =>
        {
            if (!state.TailJudged)
            {
                checkpoints.Add("head.state");
                Assert.That(output.GetMainResult(note).HasResult, Is.False);
                Assert.That(context.GetPendingLongNoteTails(), Is.Empty);
                Assert.That(context.FirstPendingNote, Is.SameAs(note));
            }
        };
        output.BeforeResultCheckpoint = (_, _) =>
        {
            checkpoints.Add("head.main");
            Assert.That(context.GetPendingLongNoteTails(), Is.EqualTo(new[] { note }));
            Assert.That(context.GetLongNoteState(note).TailJudged, Is.False);
        };
        context.HandleNotePress(new BmsPressContext(3000, new BmsFrameContext(3003, 1.25)));
        Assert.That(output.MainResults, Is.EqualTo(1));
        Assert.That(context.GetNoteCandidates(), Is.Empty);
        Assert.That(context.GetPendingLongNoteTails(), Is.EqualTo(new[] { note }));
        Assert.That(context.GetHoldingNotes(), Is.EqualTo(new[] { note }));
        output.SyntheticEndpoint = (endpoint, after) =>
        {
            checkpoints.Add(after ? "tail.after" : "tail.before");
            Assert.That(endpoint.Kind, Is.EqualTo(BmsLongNoteEndpointKind.Tail));
            Assert.That(context.GetLongNoteState(note).TailJudged, Is.False);
            Assert.That(context.GetPendingLongNoteTails(), Is.EqualTo(new[] { note }));
            Assert.That(output.GetMainResult(note).HasResult, Is.True);
        };
        if (passive)
        {
            var deadline = note.EndTime + BmsJudgementProfileProvider.GetTable(note.Beatmap.LayoutVariant, note.Column, note.EffectiveJudgementRate, false).SlowWindowFor(HitResult.Ok);
            context.CheckRegisteredNotePassive(note, new BmsFrameContext(deadline, .75));
            Assert.That(output.SyntheticEndpoints, Is.Empty);
            context.CheckRegisteredNotePassive(note, new BmsFrameContext(deadline + 1, .75));
        }
        else
            context.HandleExecutionRelease(3400, new BmsFrameContext(3407, 1.5));
        context.CheckRegisteredNotePassive(note, new BmsFrameContext(5000, 1));
        context.UpdateRegisteredLongNote(note, new BmsFrameContext(5000, 1), 0, false);
        context.HandleExecutionRelease(5000, new BmsFrameContext(5000, 1));
        Assert.That(output.MainResults, Is.EqualTo(1));
        Assert.That(output.SyntheticEndpoints, Has.Count.EqualTo(1));
        Assert.That(context.GetPendingLongNoteTails(), Is.Empty);
        Assert.That(context.GetLongNoteState(note).TailJudged, Is.True);
        Assert.That(checkpoints, Is.EqualTo(new[] { "head.state", "head.main", "tail.before", "tail.after" }));
    }

    [Test]
    public void PassiveHeadPoorSubmitsMainThenTailWithoutSuccessfulHeadPublication()
    {
        var (context, output, note) = create();
        var checkpoints = new List<string>();
        output.BeforeResultCheckpoint = (_, _) => checkpoints.Add("main");
        output.SyntheticEndpoint = (_, after) =>
        {
            if (after) return;
            checkpoints.Add("tail");
            Assert.That(context.GetLongNoteState(note).TailJudged, Is.False);
            Assert.That(context.GetPendingLongNoteTails(), Is.EqualTo(new[] { note }));
            Assert.That(output.GetMainResult(note).Type, Is.EqualTo(HitResult.Meh));
        };
        output.StatePublished = (_, state, _) =>
        {
            checkpoints.Add("state");
            Assert.That(state.TailJudged, Is.True);
        };
        context.CheckRegisteredNotePassive(note, new BmsFrameContext(3300, 1));
        context.CheckRegisteredNotePassive(note, new BmsFrameContext(3301, 1));
        Assert.That(checkpoints, Is.EqualTo(new[] { "main", "tail", "state" }));
        Assert.That(output.MainResults, Is.EqualTo(1));
        Assert.That(output.SyntheticEndpoints, Has.Count.EqualTo(1));
        Assert.That(context.GetPendingLongNoteTails(), Is.Empty);
    }

    [Test]
    public void LastTailCheckCannotRestoreRemovedMembership()
    {
        var (context, output, note) = create();
        context.HandleNotePress(new BmsPressContext(3000, new BmsFrameContext(3000, 1)));
        context.BeginNoteRemoval(note);
        output.SyntheticEndpoint = (_, _) => Assert.That(context.GetPendingLongNoteTails(), Is.Empty);
        output.StatePublished = (_, _, _) => Assert.That(context.GetPendingLongNoteTails(), Is.Empty);
        context.CheckRegisteredNotePassive(note, new BmsFrameContext(5000, 1));
        context.CheckRegisteredNotePassive(note, new BmsFrameContext(5001, 1));
        Assert.That(output.SyntheticEndpoints, Has.Count.EqualTo(1));
        Assert.That(context.GetPendingLongNoteTails(), Is.Empty);
    }

    [Test]
    public void ScratchReleaseRetainsTailUntilReversePress()
    {
        var (context, output, note) = create(0);
        context.HandleNotePress(new BmsPressContext(3000, new BmsFrameContext(3000, 1)));
        context.HandleExecutionRelease(3990, new BmsFrameContext(3997, 1.5));
        Assert.That(output.SyntheticEndpoints, Is.Empty);
        Assert.That(context.GetPendingLongNoteTails(), Is.EqualTo(new[] { note }));
        context.HandleNotePress(new BmsPressContext(3990, new BmsFrameContext(3997, 1.5), true));
        Assert.That(output.SyntheticEndpoints.Single().EventTime, Is.EqualTo(3990));
        Assert.That(output.SyntheticEndpoints.Single().ApplicationTime, Is.EqualTo(3990));
        Assert.That(output.SyntheticApplicationTimes.Single(), Is.EqualTo(3997));
        Assert.That(context.GetPendingLongNoteTails(), Is.Empty);
        output.RevertSyntheticAfter(3997);
        Assert.That(output.SyntheticEndpoints, Has.Count.EqualTo(1));
        output.RevertSyntheticAfter(3996);
        Assert.That(output.SyntheticEndpoints, Is.Empty);
        Assert.That(output.GetMainResult(note).HasResult, Is.True);
    }

    private static (BmsColumnJudgementRuntime Context, BmsRecordingJudgementOutput Output, BmsLongNote Note) create(int column = 1)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, LockedLongNoteMode = BmsLongNoteMode.ChargeNote, Rank = 2 };
        var note = new BmsLongNote { Beatmap = beatmap, Column = column, StartTime = 3000, Duration = 1000 };
        var output = new BmsRecordingJudgementOutput();
        var context = new BmsJudgementEngine([note], beatmap.LayoutVariant, column, true, output).GetColumn(column);
        context.ActivateNote(note);
        return (context, output, note);
    }
}
