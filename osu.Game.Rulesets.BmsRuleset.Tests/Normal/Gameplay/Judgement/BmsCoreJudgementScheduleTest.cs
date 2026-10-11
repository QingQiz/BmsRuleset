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
public partial class BmsCoreJudgementScheduleTest
{
    [TestCase(0, false, false)]
    [TestCase(0, true, false)]
    [TestCase(1, false, false)]
    [TestCase(1, true, false)]
    [TestCase(0, false, true)]
    [TestCase(0, true, true)]
    [TestCase(1, false, true)]
    [TestCase(1, true, true)]
    public void AutomaticLongNoteTailRequiresPassingEndTime(int column, bool deferred, bool cached)
    {
        var context = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote, column: column);
        var schedule = context.Column.Schedule;
        var handle = schedule.Bind(context.Note);
        var currentOrder = order(cached, handle);
        Assert.That(context.Press(1080, HitResult.Good), Is.True);
        double[] times = [1499.999, 1500, 1500.001, 1501];
        var previousTime = 1080d;

        for (var i = 0; i < times.Length; i++)
        {
            var time = times[i];
            var frame = new BmsFrameContext(time, 1);
            schedule.BeginBatch(0, i + 1, i + 1);
            if (deferred)
                schedule.RunDeferred(frame, time - previousTime);
            else
                schedule.RunLayout(currentOrder, frame, time - previousTime, false);
            schedule.RunSubtree(currentOrder, frame);

            Assert.Multiple(() =>
            {
                Assert.That(context.State.TailJudged, Is.EqualTo(i >= 2), $"tail at {time}");
                Assert.That(context.MainResults, Has.Count.EqualTo(i >= 2 ? 1 : 0), $"results at {time}");
            });
            previousTime = time;
        }

        Assert.Multiple(() =>
        {
            Assert.That(context.Results, Is.EqualTo([HitResult.Good]));
            Assert.That(context.State.TailTime, Is.EqualTo(1500.001));
        });
    }

    [TestCase(BmsLongNoteMode.ChargeNote, false, false)]
    [TestCase(BmsLongNoteMode.ChargeNote, true, false)]
    [TestCase(BmsLongNoteMode.HellChargeNote, false, false)]
    [TestCase(BmsLongNoteMode.HellChargeNote, true, false)]
    [TestCase(BmsLongNoteMode.ChargeNote, false, true)]
    [TestCase(BmsLongNoteMode.ChargeNote, true, true)]
    [TestCase(BmsLongNoteMode.HellChargeNote, false, true)]
    [TestCase(BmsLongNoteMode.HellChargeNote, true, true)]
    public void StepObservationPrecedesDeferredTailAndRejectsAReboundPlan(BmsLongNoteMode mode, bool rebind, bool cached)
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
        schedule.RunLayout(order(cached, handle), new BmsFrameContext(3990, 1), 589, false);
        Assert.That(observations, Is.EqualTo(rebind ? new[] { "step" } : ["step", "tail"]));
        if (rebind)
        {
            context.Output.SimulationStep = (_, _) => observations.Add("replacement");
            schedule.BeginBatch(0, 1, 2);
            schedule.RunLayout(order(cached, handle), new BmsFrameContext(3990, 1), 589, false);
            Assert.That(observations, Is.EqualTo(new[] { "step", "replacement", "tail" }));
        }
        schedule.RunLayout(order(cached, handle), new BmsFrameContext(3990, 1), 589, false);
        Assert.That(context.Output.SyntheticEndpoints, Has.Count.EqualTo(1));
    }

    [Test]
    public void PassiveCallbackCannotReuseAnInvalidatedBindingForPostWork(
        [Values(BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote)] BmsLongNoteMode mode,
        [Values(0, 1, 2)] int invalidation,
        [Values(false, true)] bool cached)
    {
        var context = new BmsLongNoteTestContext(mode);
        var schedule = context.Column.Schedule;
        var original = schedule.Bind(context.Note);
        var replacement = original;
        var observations = 0;
        context.Output.SimulationStep = (_, _) => observations++;
        context.Output.BeforeResultCheckpoint = (note, _) =>
        {
            switch (invalidation)
            {
                case 0:
                    schedule.BeginRemoval(original);
                    break;

                case 1:
                    schedule.Unbind(original);
                    break;

                case 2:
                    replacement = schedule.Bind(note);
                    break;
            }
        };
        schedule.BeginBatch(0, 1, 1);
        schedule.RunLayout(order(cached, original), new BmsFrameContext(2000, 1), 1000, false);
        Assert.That(context.Output.MainResults, Is.EqualTo(1));
        Assert.That(observations, Is.Zero);
        if (invalidation == 2)
        {
            schedule.BeginBatch(0, 1, 2);
            schedule.RunLayout(order(cached, replacement), new BmsFrameContext(2000, 1), 1000, false);
            Assert.That(observations, Is.EqualTo(1));
        }
        else
            Assert.That(schedule.TryGetCurrent(context.Note, out _), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SynchronousRemovalOfCurrentItemKeepsTheNextItem(bool cached)
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
        schedule.RunSubtree(order(cached, handles[0], handles[1]), new BmsFrameContext(5000, 1));
        Assert.That(observed, Is.EqualTo(new[] { notes[0], notes[1] }));
        Assert.That(context.GetNoteCandidates(), Is.EqualTo(new[] { notes[2] }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RebindingInvalidatesTheQueuedOldGenerationAndOldUnbind(bool cached)
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
        schedule.RunSubtree(order(cached, handles[0], handles[1]), new BmsFrameContext(5000, 1));
        Assert.That(observed, Is.EqualTo(new[] { notes[0] }));
        schedule.BeginBatch(0, 2, 2);
        schedule.RunSubtree(order(cached, replacement), new BmsFrameContext(5000, 1));
        Assert.That(observed, Is.EqualTo(new[] { notes[0], notes[1] }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AReentrantNewEpochCannotResumeTheOverwrittenOuterPlan(bool cached)
    {
        var (notes, _, output, schedule, handles) = create();
        var observed = new List<BmsHitObject>();
        output.BeforeResultCheckpoint = (note, _) =>
        {
            observed.Add(note);
            if (!ReferenceEquals(note, notes[0])) return;
            schedule.BeginBatch(1, 2, 1);
            schedule.RunSubtree(order(cached, handles[2]), new BmsFrameContext(4000, 1));
        };
        schedule.RunSubtree(order(cached, handles[0], handles[1]), new BmsFrameContext(5000, 1));
        Assert.That(observed, Is.EqualTo(new[] { notes[0], notes[2] }));
        Assert.That(output.GetMainResult(notes[1]).HasResult, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AnEmptyReentrantLayoutCannotResumeTheOuterSubtreePlan(bool cached)
    {
        var (notes, _, output, schedule, handles) = create();
        var observed = new List<BmsHitObject>();
        output.BeforeResultCheckpoint = (note, _) =>
        {
            observed.Add(note);
            if (ReferenceEquals(note, notes[0]))
                schedule.RunLayout(order(cached), new BmsFrameContext(5000, 1), 1, false);
        };

        schedule.RunSubtree(order(cached, handles[0], handles[1]), new BmsFrameContext(5000, 1));

        Assert.That(observed, Is.EqualTo(new[] { notes[0] }));
        Assert.That(output.GetMainResult(notes[1]).HasResult, Is.False);
        schedule.BeginBatch(0, 2, 2);
        schedule.RunSubtree(order(cached, handles[1]), new BmsFrameContext(5000, 1));
        Assert.That(observed, Is.EqualTo(new[] { notes[0], notes[1] }));
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

    [TestCase(false)]
    [TestCase(true)]
    public void HistoryRewindInvalidatesAnExecutingPlanBeforeTheNextItem(bool cached)
    {
        var (notes, _, output, schedule, handles) = create();
        output.BeforeResultCheckpoint = (_, _) => schedule.Rewind();
        schedule.RunSubtree(order(cached, handles[0], handles[1]), new BmsFrameContext(5000, 1));
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

    [Test]
    public void DefaultAndForeignHandlesCannotChangeTheCurrentBinding()
    {
        var (notes, context, output, schedule, handles) = create();
        var foreignSchedule = new BmsCoreJudgementSchedule(context);
        var foreign = foreignSchedule.Bind(notes[0]);
        Assert.That(foreign.BindingToken, Is.EqualTo(handles[0].BindingToken));
        BmsScheduledObject[] invalid = [default, foreign];
        foreach (var item in invalid)
        {
            schedule.BeginRemoval(item);
            schedule.CompleteRemoval(item, new BmsFrameContext(5000, 1));
            schedule.Unbind(item);
        }
        schedule.RunLayout(invalid, new BmsFrameContext(5000, 1), 1, false);
        schedule.RunSubtree(invalid, new BmsFrameContext(5000, 1));
        Assert.That(output.MainResults, Is.Zero);
        Assert.That(schedule.TryGetCurrent(notes[0], out var current), Is.True);
        Assert.That(current, Is.EqualTo(handles[0]));
        Assert.That(foreignSchedule.TryGetCurrent(notes[0], out current), Is.True);
        Assert.That(current, Is.EqualTo(foreign));
        schedule.BeginBatch(0, 2, 2);
        schedule.RunSubtree([handles[0]], new BmsFrameContext(5000, 1));
        Assert.That(output.MainResults, Is.EqualTo(1));
    }

    [Test]
    public void RebuildingCachedOrderDuringRemovalKeepsOriginalSuccessors()
    {
        var (notes, context, output, schedule, handles) = create();
        var cachedOrder = (BmsScheduledOrder)order(true, handles[0], handles[1]);
        var observed = new List<BmsHitObject>();
        output.BeforeResultCheckpoint = (note, _) =>
        {
            observed.Add(note);
            if (!ReferenceEquals(note, notes[0])) return;
            context.BeginNoteRemoval(note);
            schedule.BeginRemoval(handles[0]);
            schedule.Unbind(handles[0]);
            cachedOrder.Clear();
            cachedOrder.Add(handles[2]);
        };

        schedule.RunSubtree(cachedOrder, new BmsFrameContext(5000, 1));
        Assert.That(observed, Is.EqualTo(new[] { notes[0], notes[1] }));
        schedule.RunSubtree(cachedOrder, new BmsFrameContext(5000, 1));
        Assert.That(observed, Is.EqualTo(new[] { notes[0], notes[1], notes[2] }));
    }

    [Test]
    public void ReenteringTheSameRebuiltCachedOrderCannotResumeTheOuterCursor()
    {
        var (notes, _, output, schedule, handles) = create();
        var cachedOrder = (BmsScheduledOrder)order(true, handles[0], handles[1]);
        var observed = new List<BmsHitObject>();
        output.BeforeResultCheckpoint = (note, _) =>
        {
            observed.Add(note);
            if (!ReferenceEquals(note, notes[0])) return;
            cachedOrder.Clear();
            cachedOrder.Add(handles[2]);
            schedule.BeginBatch(1, 2, 1);
            schedule.RunSubtree(cachedOrder, new BmsFrameContext(5000, 1));
        };

        schedule.RunSubtree(cachedOrder, new BmsFrameContext(5000, 1));
        Assert.That(observed, Is.EqualTo(new[] { notes[0], notes[2] }));
        Assert.That(output.GetMainResult(notes[1]).HasResult, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AChangedOrderWithTheSameIdentityIsVisibleInTheNextBatch(bool cached)
    {
        var (notes, _, output, schedule, handles) = create();
        var currentOrder = order(cached, handles[0]);
        schedule.RunSubtree(currentOrder, new BmsFrameContext(0, 1));
        if (currentOrder is BmsScheduledOrder cachedOrder)
        {
            cachedOrder.Clear();
            cachedOrder.Add(handles[2]);
            cachedOrder.Add(handles[1]);
        }
        else
        {
            var mutableOrder = (List<BmsScheduledObject>)currentOrder;
            mutableOrder.Clear();
            mutableOrder.Add(handles[2]);
            mutableOrder.Add(handles[1]);
        }
        var observed = new List<BmsHitObject>();
        output.BeforeResultCheckpoint = (note, _) => observed.Add(note);
        schedule.BeginBatch(0, 2, 2);
        schedule.RunSubtree(currentOrder, new BmsFrameContext(5000, 1));

        Assert.That(observed, Is.EqualTo(new[] { notes[2], notes[1] }));
        Assert.That(output.GetMainResult(notes[0]).HasResult, Is.False);
    }

    [Test]
    public void SkippedActiveNoteOrderKeepsAChangedCachedOrderForTheNextDeadline()
    {
        var (notes, _, output, schedule, handles) = create();
        var cachedOrder = (BmsScheduledOrder)order(true, handles[0]);

        schedule.RunSubtree(cachedOrder, new BmsFrameContext(0, 1));
        cachedOrder.Clear();
        cachedOrder.Add(handles[2]);

        var observed = new List<BmsHitObject>();
        output.BeforeResultCheckpoint = (note, _) => observed.Add(note);
        schedule.BeginBatch(0, 2, 2);
        schedule.RunSubtree(cachedOrder, new BmsFrameContext(5000, 1));

        Assert.That(observed, Is.EqualTo(new[] { notes[2] }));
    }

    private static IReadOnlyList<BmsScheduledObject> order(bool cached, params BmsScheduledObject[] items)
    {
        if (!cached) return new List<BmsScheduledObject>(items);
        var result = new BmsScheduledOrder();
        foreach (var item in items) result.Add(item);
        return result;
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
