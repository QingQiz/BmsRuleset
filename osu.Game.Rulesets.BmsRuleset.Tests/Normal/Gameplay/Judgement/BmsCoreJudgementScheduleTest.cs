using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using System;
using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsCoreJudgementScheduleTest
{
    [TestCase(BmsLongNoteMode.ChargeNote, false)]
    [TestCase(BmsLongNoteMode.ChargeNote, true)]
    [TestCase(BmsLongNoteMode.HellChargeNote, false)]
    [TestCase(BmsLongNoteMode.HellChargeNote, true)]
    public void StepObservationPrecedesDeferredTailAndRejectsAReboundPlan(BmsLongNoteMode mode, bool rebind)
    {
        var context = new BmsLongNoteTestContext(mode, start: 3000, duration: 1002, layout: BmsLayoutVariant.Pms9K);
        var schedule = context.Column.Schedule;
        var handle = schedule.Bind(context.Note);
        Assert.That(context.Press(3000, HitResult.Perfect), Is.True);
        context.Release(3401);
        Assert.That(context.State.TailJudged, Is.False);
        var observations = new List<string>();
        context.Output.SimulationStep = (note, frame) =>
        {
            Assert.That(note, Is.SameAs(context.Note));
            Assert.That(frame.SimulationTime, Is.EqualTo(3990));
            Assert.That(context.State.TailJudged, Is.False);
            observations.Add("step");
            if (rebind) handle = schedule.Bind(note);
        };
        context.Output.SyntheticEndpoint = (_, after) => { if (!after) observations.Add("tail"); };
        schedule.BeginBatch(0, 1, 1);
        schedule.RunLayout([handle], new BmsFrameContext(3990, 1), 589, false);
        Assert.That(observations, Is.EqualTo(rebind ? new[] { "step" } : ["step", "tail"]));
        if (rebind)
        {
            context.Output.SimulationStep = (_, _) => observations.Add("replacement");
            schedule.BeginBatch(0, 1, 2);
            schedule.RunLayout([handle], new BmsFrameContext(3990, 1), 589, false);
            Assert.That(observations, Is.EqualTo(new[] { "step", "replacement", "tail" }));
        }
        schedule.RunLayout([handle], new BmsFrameContext(3990, 1), 589, false);
        Assert.That(context.Output.SyntheticEndpoints, Has.Count.EqualTo(1));
    }

    [Test]
    public void SynchronousRemovalOfCurrentItemKeepsTheNextItem()
    {
        var (notes, context, output, schedule, handles) = create();
        var observed = new List<BmsHitObject>();
        output.BeforeResultCheckpoint = (note, _) =>
        {
            observed.Add(note);
            context.BeginNoteRemoval(note);
            var item = handles[Array.IndexOf(notes, note)];
            schedule.BeginRemoval(item);
            schedule.Unbind(item);
        };
        schedule.RunSubtree([handles[0], handles[1]], new BmsFrameContext(5000, 1));
        Assert.That(observed, Is.EqualTo(new[] { notes[0], notes[1] }));
        Assert.That(context.GetNoteCandidates(), Is.EqualTo(new[] { notes[2] }));
    }

    [Test]
    public void RebindingInvalidatesTheQueuedOldGenerationAndOldUnbind()
    {
        var (notes, _, output, schedule, handles) = create();
        var replacement = handles[1];
        var observed = new List<BmsHitObject>();
        output.BeforeResultCheckpoint = (note, _) =>
        {
            observed.Add(note);
            if (!ReferenceEquals(note, notes[0])) return;
            replacement = schedule.Bind(notes[1]);
            schedule.Unbind(handles[1]);
        };
        schedule.RunSubtree([handles[0], handles[1]], new BmsFrameContext(5000, 1));
        Assert.That(observed, Is.EqualTo(new[] { notes[0] }));
        schedule.BeginBatch(0, 2, 2);
        schedule.RunSubtree([replacement], new BmsFrameContext(5000, 1));
        Assert.That(observed, Is.EqualTo(new[] { notes[0], notes[1] }));
    }

    [Test]
    public void AReentrantNewEpochCannotResumeTheOverwrittenOuterPlan()
    {
        var (notes, _, output, schedule, handles) = create();
        var observed = new List<BmsHitObject>();
        output.BeforeResultCheckpoint = (note, _) =>
        {
            observed.Add(note);
            if (!ReferenceEquals(note, notes[0])) return;
            schedule.BeginBatch(1, 2, 1);
            schedule.RunSubtree([handles[2]], new BmsFrameContext(4000, 1));
        };
        schedule.RunSubtree([handles[0], handles[1]], new BmsFrameContext(5000, 1));
        Assert.That(observed, Is.EqualTo(new[] { notes[0], notes[2] }));
        Assert.That(output.GetMainResult(notes[1]).HasResult, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReplacementWithTheSameLocalGenerationCannotReuseTheOldBinding(bool oldUnbind)
    {
        var (notes, _, output, schedule, handles) = create();
        var replacement = handles[1];
        var observed = new List<BmsHitObject>();
        output.BeforeResultCheckpoint = (note, _) =>
        {
            observed.Add(note);
            if (!ReferenceEquals(note, notes[0])) return;
            replacement = schedule.Bind(notes[1]);
            Assert.That(replacement.BindingToken, Is.Not.EqualTo(handles[1].BindingToken));
            schedule.BeginRemoval(handles[1]);
            schedule.CompleteRemoval(handles[1], new BmsFrameContext(5000, 1));
            if (oldUnbind) schedule.Unbind(handles[1]);
        };
        schedule.RunSubtree([handles[0], handles[1], handles[2]], new BmsFrameContext(5000, 1));
        Assert.That(observed, Is.EqualTo(new[] { notes[0], notes[2] }));
        schedule.BeginBatch(0, 2, 2);
        schedule.RunSubtree([replacement], new BmsFrameContext(5000, 1));
        Assert.That(observed, Is.EqualTo(new[] { notes[0], notes[2], notes[1] }));
    }

    [Test]
    public void HistoryRewindInvalidatesAnExecutingPlanBeforeTheNextItem()
    {
        var (notes, _, output, schedule, handles) = create();
        output.BeforeResultCheckpoint = (_, _) => schedule.Rewind();
        schedule.RunSubtree([handles[0], handles[1]], new BmsFrameContext(5000, 1));
        Assert.That(output.MainResults, Is.EqualTo(1));
        Assert.That(output.GetMainResult(notes[1]).HasResult, Is.False);
    }

    [Test]
    public void LastCheckUsesTheRemovingGenerationExactlyOnce()
    {
        var (notes, context, output, schedule, handles) = create();
        var replacement = schedule.Bind(notes[0]);
        context.BeginNoteRemoval(notes[0]);
        schedule.BeginRemoval(replacement);
        schedule.CompleteRemoval(handles[0], new BmsFrameContext(5000, 1));
        Assert.That(output.MainResults, Is.Zero);
        schedule.CompleteRemoval(replacement, new BmsFrameContext(5000, 1));
        schedule.CompleteRemoval(replacement, new BmsFrameContext(5001, 1));
        Assert.That(output.MainResults, Is.EqualTo(1));
        Assert.That(context.IsNoteCandidate(notes[0]), Is.False);
    }

    private static (BmsHitObject[] Notes, BmsColumnJudgementRuntime Context, BmsRecordingJudgementOutput Output, BmsCoreJudgementSchedule Schedule, BmsScheduledObject[] Handles) create()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 2 };
        BmsHitObject[] notes = [new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 },
            new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 2000 }, new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 3000 }];
        var output = new BmsRecordingJudgementOutput();
        var context = new BmsJudgementEngine(notes, BmsLayoutVariant.Bme7K, 1, true, output).GetColumn(1);
        var schedule = new BmsCoreJudgementSchedule(context);
        var handles = new BmsScheduledObject[notes.Length];
        for (var i = 0; i < notes.Length; i++) { context.ActivateNote(notes[i]); handles[i] = schedule.Bind(notes[i]); }
        schedule.BeginBatch(0, 1, 1);
        return (notes, context, output, schedule, handles);
    }
}
