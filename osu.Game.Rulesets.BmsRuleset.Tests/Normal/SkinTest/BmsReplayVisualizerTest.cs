using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.SkinTest;

[TestFixture]
public class BmsReplayVisualizerTest
{
    [Test]
    public void TestAvailableInSkinEditorAndSettingsSurviveRoundTrip()
    {
        var visualizer = new BmsReplayVisualizer
        {
            Size = new Vector2(300, 450),
            Position = new Vector2(24, 40),
        };
        visualizer.TimeRange.Value = 6;
        visualizer.ShowNotes.Value = false;
        visualizer.ShowInputs.Value = false;
        visualizer.BackgroundOpacity.Value = 0.3f;
        var restored = (BmsReplayVisualizer)visualizer.CreateSerialisedInfo().CreateInstance();

        Assert.Multiple(() =>
        {
            Assert.That(SerialisedDrawableInfo.GetAllAvailableDrawables(new BmsRuleset().RulesetInfo), Does.Contain(typeof(BmsReplayVisualizer)));
            Assert.That(restored.Size, Is.EqualTo(visualizer.Size));
            Assert.That(restored.Position, Is.EqualTo(visualizer.Position));
            Assert.That(restored.TimeRange.Value, Is.EqualTo(6));
            Assert.That(restored.ShowNotes.Value, Is.False);
            Assert.That(restored.ShowInputs.Value, Is.False);
            Assert.That(restored.BackgroundOpacity.Value, Is.EqualTo(0.3f));
        });
    }

    [Test]
    public void TestVisibleNotesIncludeOverlappingLongNotesAndBoundaryTimes()
    {
        var lane = new BmsReplayVisualizer.NoteLane(
        [
            new BmsNote { StartTime = 50, Column = 1 },
            new BmsLongNote { StartTime = 100, Duration = 900, Column = 1 },
            new BmsNote { StartTime = 200, Column = 1 },
            new BmsNote { StartTime = 500, Column = 1 },
            new BmsNote { StartTime = 600, Column = 1 },
            new BmsNote { StartTime = 601, Column = 1 },
        ]);

        Assert.That(collect(lane.Visible(500, 600)).Select(note => note.StartTime), Is.EqualTo(new[] { 100d, 500, 600 }));
        Assert.That(collect(lane.Visible(1001, 2000)), Is.Empty);
        Assert.That(collect(lane.Visible(-100, 0)), Is.Empty);
    }

    [Test]
    public void TestInputHistoryKeepsShortPressesBetweenHudFramesAndBothScratchDirections()
    {
        var history = new BmsReplayVisualizer.InputHistory();
        history.Advance(0);
        history.Press(BmsAction.Scratch, 0, 10);
        history.Press(BmsAction.ScratchReverse, 0, 11);
        history.Release(BmsAction.Scratch, 12);
        history.Release(BmsAction.ScratchReverse, 13);
        history.Press(BmsAction.Scratch, 0, 14);
        history.Release(BmsAction.Scratch, 15);
        history.Advance(20);

        Assert.That(history.Strokes.Select(stroke => (stroke.StartTime, stroke.EndTime)),
            Is.EqualTo(new[] { (10d, 12d), (11d, 13d), (14d, 15d) }));
    }

    [Test]
    public void TestInputHistoryPrunesExpiredPressesButKeepsLongHolds()
    {
        var history = new BmsReplayVisualizer.InputHistory();
        history.Press(BmsAction.Key1, 1, 0);
        history.Press(BmsAction.Key2, 2, 1);
        history.Release(BmsAction.Key2, 2);
        history.Advance(20000);

        Assert.That(history.Strokes.Select(stroke => stroke.Action), Is.EqualTo(new[] { BmsAction.Key1 }));
        history.Release(BmsAction.Key1, 20001);
        history.Advance(30002);
        Assert.That(history.Strokes, Is.Empty);
    }

    [Test]
    public void TestRewindRestoresHeldKeysAndRemovesFutureInput()
    {
        var history = new BmsReplayVisualizer.InputHistory();
        history.Press(BmsAction.Key1, 1, 100);
        history.Release(BmsAction.Key1, 200);
        history.Press(BmsAction.Key2, 2, 300);
        history.Advance(400);
        history.Advance(150);

        Assert.That(history.HeldActions, Is.EqualTo(new[] { BmsAction.Key1 }));
        Assert.That(history.Strokes, Has.Count.EqualTo(1));
        Assert.That(history.Strokes[0].EndTime, Is.EqualTo(double.PositiveInfinity));
        history.Release(BmsAction.Key1, 220);
        Assert.That(history.Strokes[0].EndTime, Is.EqualTo(220));
    }

    [Test]
    public void TestHistoryCompactionKeepsHeldIndicesAndExpiryBoundary()
    {
        var history = new BmsReplayVisualizer.InputHistory();
        history.Press(BmsAction.Key1, 1, 0);
        history.Release(BmsAction.Key1, 1);
        history.Press(BmsAction.Key2, 2, 2);
        history.Press(BmsAction.Key3, 3, 3);
        history.Release(BmsAction.Key3, 4);
        history.Advance(10001);
        Assert.That(history.Strokes, Has.Count.EqualTo(3));
        history.Advance(10002);
        Assert.That(history.Strokes, Has.Count.EqualTo(2));
        history.Release(BmsAction.Key2, 10003);
        Assert.That(history.Strokes[0].EndTime, Is.EqualTo(10003));
        history.Advance(10004);
        Assert.That(history.Strokes, Has.Count.EqualTo(2));
        history.Advance(10005);
        Assert.That(history.Strokes.Select(stroke => stroke.Action), Is.EqualTo(new[] { BmsAction.Key2 }));
    }

    [Test]
    public void TestVisibleQueriesAndInputMaintenanceReuseStorage()
    {
        var lane = new BmsReplayVisualizer.NoteLane(
        [
            new BmsLongNote { StartTime = 0, Duration = 1000 },
            new BmsNote { StartTime = 200 },
            new BmsNote { StartTime = 500 },
        ]);
        var inputLane = new BmsReplayVisualizer.InputLane([new(BmsAction.Key1, 1, 10, 20)]);
        var history = new BmsReplayVisualizer.InputHistory();
        var count = 0;
        exercise(0);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var repeat = 1; repeat <= 256; repeat++)
            exercise(repeat);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(count, Is.EqualTo(257 * 3));
        Assert.That(allocated, Is.LessThan(1024));
        Assert.That(history.Strokes, Is.Empty);

        void exercise(int repeat)
        {
            foreach (var _ in lane.Visible(500, 600)) count++;
            foreach (var _ in inputLane.Visible(15, 20)) count++;
            foreach (var _ in inputLane.Visible(21, 30)) count++;
            var time = repeat * 30000d;
            history.Press(BmsAction.Key1, 1, time);
            history.Press(BmsAction.Key2, 2, time);
            history.Release(BmsAction.Key1, time + 1);
            history.Advance(time + 10002);
            history.Release(BmsAction.Key2, time + 10003);
            history.Advance(time + 20004);
        }
    }

    [Test]
    public void TestReplayIndexKeepsReleasedAndHeldInputsAndCanBeCancelled()
    {
        BmsReplayFrame[] frames =
        [
            new(0),
            new(10, BmsAction.Key1, BmsAction.Scratch, BmsAction.ScratchReverse),
            new(11, BmsAction.Key1, BmsAction.ScratchReverse),
            new(12),
            new(20, BmsAction.Key2),
        ];
        var lanes = BmsReplayVisualizer.BuildReplayInputs(frames, BmsLayoutVariant.Bme7K, 8);
        Assert.That(collect(lanes[0].Visible(11, 12)).Select(stroke => (stroke.StartTime, stroke.EndTime)),
            Is.EqualTo(new[] { (10d, 11d), (10d, 12d) }));
        Assert.That(collect(lanes[1].Visible(0, 30)).Single().EndTime, Is.EqualTo(12));
        Assert.That(collect(lanes[2].Visible(20000, 30000)).Single().EndTime, Is.EqualTo(double.PositiveInfinity));
        Assert.That(collect(lanes[2].Visible(0, 19)), Is.Empty);
        Assert.Throws<OperationCanceledException>(() =>
            BmsReplayVisualizer.BuildReplayInputs(frames, BmsLayoutVariant.Bme7K, 8, new CancellationToken(true)));
    }

    [Test]
    public void TestJudgementsTrackRewindAndReplacementWithoutRevealingFutureResults()
    {
        var cursor = new BmsReplayVisualizer.JudgementCursor();
        var first = judgement(100, HitResult.Great);
        var second = judgement(200, HitResult.Good);
        var events = new List<BmsJudgementEvent> { first, second };
        cursor.Update(events);

        Assert.That(cursor.At(1, 100, 99), Is.Null);
        Assert.That(cursor.ForInput(new(BmsAction.Key1, 1, 110, 115), 109), Is.Null);
        Assert.That(cursor.TimingForInput(new(BmsAction.Key1, 1, 110, 115), false, 109), Is.Null);
        Assert.That(cursor.At(1, 100, 110)?.Result, Is.EqualTo(HitResult.Great));
        Assert.That(cursor.ForInput(new(BmsAction.Key1, 1, 110, 115), 110), Is.EqualTo(HitResult.Great));
        Assert.That(cursor.TimingForInput(new(BmsAction.Key1, 1, 110, 115), false, 110)?.ExpectedTime, Is.EqualTo(100));
        Assert.That(cursor.At(2, 100, 1000), Is.Null);
        events.RemoveAt(1);
        cursor.Update(events);
        Assert.That(cursor.At(1, 200, 1000), Is.Null);
        Assert.That(cursor.ForInput(new(BmsAction.Key1, 1, 210, 220), 1000), Is.Null);
        Assert.That(cursor.TimingForInput(new(BmsAction.Key1, 1, 210, 220), false, 1000), Is.Null);
        events[0] = judgement(100, HitResult.Perfect);
        cursor.Update(events);
        Assert.That(cursor.At(1, 100, 1000)?.Result, Is.EqualTo(HitResult.Perfect));
        Assert.That(cursor.ForInput(new(BmsAction.Key1, 1, 110, 115), 1000), Is.EqualTo(HitResult.Perfect));
        cursor.Update(null);
        Assert.That(cursor.At(1, 100, 1000), Is.Null);
        Assert.That(cursor.ForInput(new(BmsAction.Key1, 1, 110, 115), 1000), Is.Null);
        Assert.That(cursor.TimingForInput(new(BmsAction.Key1, 1, 110, 115), false, 1000), Is.Null);
    }

    [Test]
    public void TestNonConsumingJudgementsPreserveNoteResultsAndSuppressedMissesRemainVisible()
    {
        var cursor = new BmsReplayVisualizer.JudgementCursor();
        cursor.Update(
        [
            judgement(100, HitResult.Great),
            new BmsJudgementEvent(new BmsJudgementSource(100, 1, BmsJudgementSourceKind.NonConsumingBad), HitResult.Ok,
                [new BmsTimingObservation(BmsTimingObservationKind.Note, 100, 120, 1, HitResult.Ok)]),
            new BmsJudgementEvent(new BmsJudgementSource(100, 1, BmsJudgementSourceKind.Note), HitResult.Meh,
                [new BmsTimingObservation(BmsTimingObservationKind.Note, 100, 130, 1, HitResult.Meh)], suppressPenalty: true),
            new BmsJudgementEvent(new BmsJudgementSource(300, 1, BmsJudgementSourceKind.Note), HitResult.Meh,
                [new BmsTimingObservation(BmsTimingObservationKind.Note, 300, 330, 1, HitResult.Meh)], suppressPenalty: true),
        ]);

        Assert.That(cursor.At(1, 100, 200)?.Result, Is.EqualTo(HitResult.Great));
        Assert.That(cursor.ForInput(new(BmsAction.Key1, 1, 120, 125), 200), Is.EqualTo(HitResult.Ok));
        Assert.That(cursor.TimingForInput(new(BmsAction.Key1, 1, 120, 125), false, 200), Is.Null);
        Assert.That(cursor.ForInput(new(BmsAction.Key1, 1, 130, 135), 200), Is.Null);
        Assert.That(cursor.At(1, 300, 330)?.Result, Is.EqualTo(HitResult.Meh));
        Assert.That(cursor.ForInput(new(BmsAction.Key1, 1, 330, 335), 400), Is.Null);
    }

    [Test]
    public void TestEmptyPoorAndMissUseDifferentColoursWithoutRecolouringNearbyNote()
    {
        var cursor = new BmsReplayVisualizer.JudgementCursor();
        cursor.Update(
        [
            new BmsJudgementEvent(new BmsJudgementSource(80, 1, BmsJudgementSourceKind.EmptyPoor), HitResult.Miss,
                [new BmsTimingObservation(BmsTimingObservationKind.Note, 100, 80, 1, HitResult.Miss)]),
            judgement(100, HitResult.Meh),
        ]);

        Assert.That(cursor.At(1, 100, 80), Is.Null);
        Assert.That(cursor.TimingForInput(new(BmsAction.Key1, 1, 80, 85), false, 200), Is.Null);
        Assert.That(BmsReplayVisualizer.ColourFor(cursor.ForInput(new(BmsAction.Key1, 1, 80, 85), 200)),
            Is.EqualTo((Colour4)BmsHitResultColours.ForHitResult(HitResult.Miss)));
        Assert.That(BmsReplayVisualizer.ColourFor(BmsReplayVisualizer.NoteResult(cursor.At(1, 100, 200), null)),
            Is.EqualTo((Colour4)BmsHitResultColours.ForHitResult(HitResult.Meh)));
        Assert.That(BmsReplayVisualizer.ColourFor(cursor.ForInput(new(BmsAction.Key1, 1, 81, 85), 200)), Is.EqualTo(Colour4.White));
        Assert.That(BmsReplayVisualizer.ColourFor(BmsReplayVisualizer.NoteResult(null, null)), Is.EqualTo(Colour4.White));
        Assert.That(BmsReplayVisualizer.ColourFor(BmsReplayVisualizer.NoteResult(
            new(BmsTimingObservationKind.Note, 100, 110, 1, HitResult.Miss), null)),
            Is.EqualTo((Colour4)BmsHitResultColours.ForHitResult(HitResult.Meh)));
    }

    [Test]
    public void TestLongNoteHeadAndTailColourTheWholeInputWithoutRevealingFutureTail()
    {
        var cursor = new BmsReplayVisualizer.JudgementCursor();
        var head = new BmsTimingObservation(BmsTimingObservationKind.LongNoteHead, 100, 110, 1, HitResult.Perfect);
        var tail = new BmsTimingObservation(BmsTimingObservationKind.LongNoteTail, 200, 210, 1, HitResult.Good);
        cursor.Update([new BmsJudgementEvent(new BmsJudgementSource(100, 1, BmsJudgementSourceKind.LongNote, 100), HitResult.Good, [head, tail])]);
        var stroke = new BmsReplayVisualizer.InputStroke(BmsAction.Key1, 1, 110, 210);

        Assert.That(cursor.ForInput(stroke, 150), Is.EqualTo(HitResult.Perfect));
        Assert.That(cursor.TimingForInput(stroke, false, 150), Is.EqualTo(head));
        Assert.That(cursor.TimingForInput(stroke, true, 150), Is.Null);
        Assert.That(cursor.ForInput(stroke, 210), Is.EqualTo(HitResult.Good));
        Assert.That(cursor.TimingForInput(stroke, true, 210), Is.EqualTo(tail));
        Assert.That(BmsReplayVisualizer.NoteResult(head, tail), Is.EqualTo(HitResult.Good));
        Assert.That(cursor.ForInput(new(BmsAction.Key2, 2, 110, 210), 210), Is.Null);
        Assert.That(cursor.ForInput(new(BmsAction.Key1, 1, 105, 110), 210), Is.Null);

        cursor.Update(null);
        cursor.ObserveLongNote(1, head, null, 200);
        Assert.That(cursor.ForInput(stroke, 150), Is.EqualTo(HitResult.Perfect));
        Assert.That(cursor.TimingForInput(stroke, false, 150), Is.EqualTo(head));
        cursor.ObserveLongNote(1, head, tail with { Result = HitResult.Meh }, 200);
        Assert.That(cursor.ForInput(stroke, 210), Is.EqualTo(HitResult.Meh));
        cursor.Update(null);
        Assert.That(cursor.ForInput(stroke, 210), Is.Null);
    }

    [Test]
    public void TestAutomaticMissedTailColoursAnUnreleasedHold()
    {
        var cursor = new BmsReplayVisualizer.JudgementCursor();
        cursor.Update(
        [
            new BmsJudgementEvent(new BmsJudgementSource(100, 1, BmsJudgementSourceKind.LongNote, 100), HitResult.Perfect,
                [new BmsTimingObservation(BmsTimingObservationKind.LongNoteHead, 100, 100, 1, HitResult.Perfect)]),
            new BmsJudgementEvent(new BmsJudgementSource(200, 1, BmsJudgementSourceKind.Note), HitResult.Meh,
                [new BmsTimingObservation(BmsTimingObservationKind.LongNoteTail, 200, 400, 1, HitResult.Meh)]),
        ]);
        var stroke = new BmsReplayVisualizer.InputStroke(BmsAction.Key1, 1, 100);
        Assert.That(cursor.ForInput(stroke, 300), Is.EqualTo(HitResult.Perfect));
        Assert.That(cursor.ForInput(stroke, 400), Is.EqualTo(HitResult.Meh));
        Assert.That(cursor.ForInput(new(BmsAction.Key1, 1, 400, 410), 410), Is.Null);
    }

    [TestCase(BmsAction.Scratch)]
    [TestCase(BmsAction.ScratchReverse)]
    [TestCase(BmsAction.P2Scratch)]
    [TestCase(BmsAction.P2ScratchReverse)]
    public void TestScratchReversalPressUsesTailJudgement(BmsAction action)
    {
        var column = action is BmsAction.P2Scratch or BmsAction.P2ScratchReverse ? 8 : 0;
        var cursor = new BmsReplayVisualizer.JudgementCursor();
        cursor.Update(
        [
            new BmsJudgementEvent(new BmsJudgementSource(200, column, BmsJudgementSourceKind.Note), HitResult.Good,
                [new BmsTimingObservation(BmsTimingObservationKind.LongNoteTail, 200, 210, 1, HitResult.Good)]),
        ]);
        var stroke = new BmsReplayVisualizer.InputStroke(action, column, 210, 220);
        Assert.That(cursor.ForInput(stroke, 209), Is.Null);
        Assert.That(cursor.ForInput(stroke, 220), Is.EqualTo(HitResult.Good));
        Assert.That(cursor.TimingForInput(stroke, false, 220)?.Kind, Is.EqualTo(BmsTimingObservationKind.LongNoteTail));
    }

    [Test]
    public void TestInputJudgementMatchingReusesStorage()
    {
        var cursor = new BmsReplayVisualizer.JudgementCursor();
        BmsJudgementEvent[] events = [judgement(100, HitResult.Great)];
        var stroke = new BmsReplayVisualizer.InputStroke(BmsAction.Key1, 1, 110, 210);
        var head = new BmsTimingObservation(BmsTimingObservationKind.LongNoteHead, 100, 110, 1, HitResult.Perfect);
        var tail = new BmsTimingObservation(BmsTimingObservationKind.LongNoteTail, 200, 210, 1, HitResult.Good);
        exercise();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 256; i++)
            exercise();
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(1024));
        Assert.That(cursor.ForInput(stroke, 210), Is.EqualTo(HitResult.Good));

        void exercise()
        {
            cursor.Update(events);
            cursor.ObserveLongNote(1, head, tail, 200);
            _ = cursor.ForInput(stroke, 210);
            _ = cursor.TimingForInput(stroke, false, 210);
            _ = cursor.TimingForInput(stroke, true, 210);
        }
    }

    private static BmsJudgementEvent judgement(double time, HitResult result) => new(
        new BmsJudgementSource(time, 1, BmsJudgementSourceKind.Note), result,
        [new BmsTimingObservation(BmsTimingObservationKind.Note, time, time + 10, 1, result)]);

    private static List<T> collect<T>(BmsReplayVisualizer.VisibleRange<T> range) where T : struct, BmsReplayVisualizer.ITimelineItem
    {
        var result = new List<T>();
        foreach (var item in range)
            result.Add(item);
        return result;
    }
}
