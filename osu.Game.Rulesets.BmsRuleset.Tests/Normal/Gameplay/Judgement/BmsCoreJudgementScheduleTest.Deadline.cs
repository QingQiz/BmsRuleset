using System;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

public partial class BmsCoreJudgementScheduleTest
{
    [Test]
    public void PassiveDeadlinePreservesTheRulesFloatingPointBoundary(
        [Values(null, BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)] BmsLongNoteMode? mode,
        [Values(-1000.1, -.1, 1000, 1000.1, 1073741824.1)] double start,
        [Values(.25, .75, 1.33)] double rate)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2, LockedLongNoteMode = mode ?? BmsLongNoteMode.LongNote };
        BmsHitObject note = mode == null ? new BmsNote() : new BmsLongNote { Duration = 1000 };
        note.Beatmap = beatmap;
        note.Column = 1;
        note.StartTime = start;
        note.JudgementRate = rate;
        var output = new BmsRecordingJudgementOutput();
        var column = new BmsJudgementEngine([note], beatmap.LayoutVariant, 1, true, output).GetColumn(1);
        var referenceOutput = new BmsRecordingJudgementOutput();
        var reference = new BmsJudgementEngine([note], beatmap.LayoutVariant, 1, true, referenceOutput).GetColumn(1);
        column.ActivateNote(note);
        reference.ActivateNote(note);
        var schedule = column.Schedule;
        var currentOrder = order(true, schedule.Bind(note));
        var referenceOrder = order(true, reference.Schedule.Bind(note));
        var offset = BmsJudgementProfileProvider.GetTable(beatmap.LayoutVariant, 1, rate, false).SlowWindowFor(HitResult.Ok);
        var deadline = start + offset;
        double[] times = [Math.BitDecrement(deadline), deadline, Math.BitIncrement(deadline), deadline + 1];
        for (var i = 0; i < times.Length; i++)
        {
            var time = times[i];
            schedule.BeginBatch(0, i, i);
            reference.Schedule.BeginBatch(0, i, i);
            schedule.RunSubtree(currentOrder, new BmsFrameContext(time, 1));
            reference.Schedule.RunSubtree(referenceOrder, new BmsFrameContext(time, 1));
            if (note is BmsLongNote longNote)
            {
                Assert.That(column.GetLongNoteState(longNote).HeadJudged, Is.EqualTo(time - start > offset), $"head at {time:R}");
                Assert.That(column.GetLongNoteState(longNote), Is.EqualTo(reference.GetLongNoteState(longNote)), $"state at {time:R}");
            }
            else
                Assert.That(output.GetMainResult(note).HasResult, Is.EqualTo(time - start > offset), $"at {time:R}");
            Assert.That(output.GetMainResult(note), Is.EqualTo(referenceOutput.GetMainResult(note)), $"result at {time:R}");
            Assert.That(output.SyntheticEndpoints, Is.EqualTo(referenceOutput.SyntheticEndpoints), $"endpoints at {time:R}");
        }
        Assert.That(output.MainResults, Is.EqualTo(mode == BmsLongNoteMode.HellChargeNote ? 0 : 1));
    }

    [Test]
    public void ANewlyActivatedEarlierHeadInvalidatesTheCachedBound()
    {
        var (notes, column, output, schedule, handles) = create();
        column.BeginNoteRemoval(notes[0]);
        column.RefreshPassiveDeadline(0);
        schedule.RunSubtree(order(true, handles[1], handles[2]), new BmsFrameContext(0, 1));
        Assert.That(column.NextPassiveUpdateTime, Is.GreaterThan(2000));
        column.ActivateNote(notes[0]);
        schedule.BeginBatch(0, 2, 2);
        schedule.RunSubtree(order(true, handles[0]), new BmsFrameContext(1500, 1));
        Assert.That(output.GetMainResult(notes[0]).HasResult, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TimingOrConfigurationChangesInvalidateTheCachedBound(bool configuration)
    {
        var (notes, column, output, schedule, handles) = create();
        var currentOrder = order(true, handles[0]);
        column.RefreshPassiveDeadline(0);
        schedule.RunSubtree(currentOrder, new BmsFrameContext(0, 1));
        var oldDeadline = column.NextPassiveUpdateTime;
        if (configuration)
        {
            notes[0].JudgementRate = .25;
            column.RefreshNoteConfiguration(notes[0]);
        }
        else
        {
            notes[0].StartTime = 500;
            column.RefreshNoteTiming(notes[0]);
        }
        var newDeadline = notes[0].StartTime + column.NoteWindows[column.GetNoteSlot(notes[0])].PassivePoorOffset;
        Assert.That(newDeadline, Is.LessThan(oldDeadline));
        schedule.BeginBatch(0, 2, 2);
        schedule.RunSubtree(currentOrder, new BmsFrameContext((oldDeadline + newDeadline) / 2, 1));
        Assert.That(output.GetMainResult(notes[0]).HasResult, Is.True);
    }

    [Test]
    public void LongNoteTimingAndConfigurationInvalidateTheHeadBound(
        [Values(BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)] BmsLongNoteMode mode,
        [Values(false, true)] bool configuration)
    {
        var context = new BmsLongNoteTestContext(mode);
        var schedule = context.Column.Schedule;
        var currentOrder = order(true, schedule.Bind(context.Note));
        context.Column.RefreshPassiveDeadline(0);
        schedule.RunSubtree(currentOrder, new BmsFrameContext(0, 1));
        var oldDeadline = context.Column.NextPassiveUpdateTime;
        if (configuration)
        {
            context.Note.JudgementRate = .25;
            context.Column.RefreshNoteConfiguration(context.Note);
        }
        else
        {
            context.Note.StartTime = 500;
            context.Column.RefreshNoteTiming(context.Note);
        }
        schedule.BeginBatch(0, 1, 1);
        schedule.RunSubtree(currentOrder, new BmsFrameContext(oldDeadline - 1, 1));
        Assert.That(context.State.HeadJudged, Is.True);
    }

    [Test]
    public void ArbitraryOrdersKeepTheirPassiveCheckOutsideTheActiveIndex()
    {
        var (notes, column, output, schedule, handles) = create();
        column.BeginNoteRemoval(notes[0]);
        column.RefreshPassiveDeadline(0);
        Assert.That(column.NextPassiveUpdateTime, Is.GreaterThan(2000));
        schedule.RunSubtree(order(true, handles[0]), new BmsFrameContext(1500, 1));
        Assert.That(output.GetMainResult(notes[0]).HasResult, Is.True);
    }

    [Test]
    public void RestoringAnUnjudgedHeadInvalidatesAnInfiniteBound()
    {
        var (notes, column, output, schedule, handles) = create();
        column.BeginNoteRemoval(notes[1]);
        column.BeginNoteRemoval(notes[2]);
        var currentOrder = order(true, handles[0]);
        schedule.RunSubtree(currentOrder, new BmsFrameContext(5000, 1));
        column.RefreshPassiveDeadline(5000);
        Assert.That(column.NextPassiveUpdateTime, Is.EqualTo(double.PositiveInfinity));
        output.RevertAfter(500);
        schedule.Rewind();
        column.RestoreExecutionState(new BmsFrameContext(500, 1, true));
        schedule.BeginBatch(1, 2, 2);
        schedule.RunSubtree(currentOrder, new BmsFrameContext(5000, 1, true));
        Assert.That(output.GetMainResult(notes[0]).HasResult, Is.False);
        schedule.BeginBatch(1, 3, 3);
        schedule.RunSubtree(currentOrder, new BmsFrameContext(5000, 1));
        Assert.That(output.GetMainResult(notes[0]).HasResult, Is.True);
        Assert.That(output.MainResults, Is.EqualTo(2));
    }

    [Test]
    public void ADeadlineSkipKeepsTheBatchStampAndBindingInvalidation()
    {
        var (notes, _, output, schedule, handles) = create();
        var currentOrder = order(true, handles[0]);
        schedule.RunSubtree(currentOrder, new BmsFrameContext(0, 1));
        schedule.RunSubtree(currentOrder, new BmsFrameContext(5000, 1));
        Assert.That(output.MainResults, Is.Zero);
        schedule.InvalidateOrder();
        schedule.RunSubtree(currentOrder, new BmsFrameContext(5000, 1));
        Assert.That(output.GetMainResult(notes[0]).HasResult, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ASkippedReentrantSubtreeInterruptsTheOuterPlan(bool cached)
    {
        var (notes, _, output, schedule, handles) = create();
        output.BeforeResultCheckpoint = (note, _) =>
        {
            if (!ReferenceEquals(note, notes[0])) return;
            schedule.InvalidateOrder();
            schedule.RunSubtree(order(cached, handles[2]), new BmsFrameContext(0, 1));
        };
        schedule.RunSubtree(order(cached, handles[0], handles[1]), new BmsFrameContext(5000, 1));
        Assert.That(output.MainResults, Is.EqualTo(1));
        Assert.That(output.GetMainResult(notes[1]).HasResult, Is.False);
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void AnActiveTailCannotUseTheHeadDeadline(BmsLongNoteMode mode)
    {
        var context = new BmsLongNoteTestContext(mode);
        var schedule = context.Column.Schedule;
        var currentOrder = order(true, schedule.Bind(context.Note));
        Assert.That(context.Press(1000, HitResult.Perfect), Is.True);
        context.Column.RefreshPassiveDeadline(1000);
        Assert.That(context.Column.NextPassiveUpdateTime, Is.EqualTo(double.PositiveInfinity));
        schedule.BeginBatch(0, 1, 1);
        schedule.RunSubtree(currentOrder, new BmsFrameContext(1500, 1));
        Assert.That(context.State.TailJudged, Is.False);
        schedule.BeginBatch(0, 2, 2);
        schedule.RunSubtree(currentOrder, new BmsFrameContext(1500.001, 1));
        if (mode != BmsLongNoteMode.LongNote)
        {
            Assert.That(context.State.TailJudged, Is.False);
            schedule.RunLayout(currentOrder, new BmsFrameContext(2000, 1), 499.999, false);
        }
        Assert.That(context.State.TailJudged, Is.True);
    }

    [Test]
    public void RestoredStatePublicationCannotBorrowThePreRebuildActivityBound()
    {
        var context = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote);
        var schedule = context.Column.Schedule;
        var currentOrder = order(true, schedule.Bind(context.Note));
        Assert.That(context.Press(1000, HitResult.Perfect), Is.True);
        schedule.RunLayout(currentOrder, new BmsFrameContext(2000, 1), 1000, false);
        Assert.That(context.State.TailJudged, Is.True);
        context.Column.RetireInactiveLongNoteAt(0);
        context.Column.RefreshPassiveDeadline(2000);
        Assert.That(context.Column.NextPassiveUpdateTime, Is.EqualTo(double.PositiveInfinity));
        var observed = false;
        context.Output.StatePublished = (_, state, frame) =>
        {
            if (!frame.IsRewinding || !state.HeadJudged || state.TailJudged) return;
            observed = true;
            Assert.That(context.Column.ActiveLongNotes, Is.Empty, "Observers still precede the activity checkpoint.");
            schedule.InvalidateOrder();
            schedule.RunSubtree(currentOrder, new BmsFrameContext(1501, 1));
            Assert.That(context.State.TailJudged, Is.True);
        };
        context.Rewind(1200);
        Assert.That(observed, Is.True);
    }
}
