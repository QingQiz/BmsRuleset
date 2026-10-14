using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsOperationSequenceTest
{
    [Test]
    public void ResultsAndGaugeDoNotDependOnUpdatePartition(
        [Values(BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)] BmsLongNoteMode mode,
        [Values(BmsLayoutVariant.Bme7K, BmsLayoutVariant.Pms9K)] BmsLayoutVariant layout,
        [Values(1, 1000.0 / 144, 137, 5000)] double interval)
    {
        var expected = run(mode, layout, 5000);
        var actual = run(mode, layout, interval);
        Assert.That(actual.Events, Is.EqualTo(expected.Events));
        Assert.That(actual.Gauges, Is.EqualTo(expected.Gauges));
        Assert.That(actual.Failed, Is.EqualTo(expected.Failed));
        Assert.That(actual.Events.Select(item => item.Time), Is.Ordered);
    }

    [Test]
    public void PassiveHeadsKeepTheirStrictSubtractionBoundary(
        [Values(null, BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)] BmsLongNoteMode? mode,
        [Values(-1000.1, -.1, 1000, 1000.1, 1073741824.1)] double start,
        [Values(.25, .75, 1.33)] double rate)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, LockedLongNoteMode = mode ?? BmsLongNoteMode.LongNote };
        BmsHitObject note = mode == null ? new BmsNote() : new BmsLongNote { Duration = 1000 };
        note.Beatmap = beatmap;
        note.Column = 1;
        note.StartTime = start;
        note.JudgementRate = rate;
        var output = new BmsRecordingJudgementOutput();
        var engine = new BmsJudgementEngine([note], beatmap.LayoutVariant, 1, output);
        var offset = BmsJudgementProfileProvider.GetTable(beatmap.LayoutVariant, 1, rate, false).SlowWindowFor(HitResult.Ok);
        var deadline = start + offset;
        double[] times = [Math.BitDecrement(deadline), deadline, Math.BitIncrement(deadline), deadline + 1];
        foreach (var time in times)
        {
            engine.ProcessFrame(new BmsFrameContext(time, 1), 0);
            var judged = note is BmsLongNote hold ? engine.GetColumn(1).GetLongNoteState(hold).HeadJudged : output.GetMainResult(note).HasResult;
            Assert.That(judged, Is.EqualTo(time - start > offset), $"at {time:R}");
        }
    }

    [TestCase(1000, true)]
    [TestCase(1000.001, false)]
    public void MineUsesItsOwnTimeEvenWhenInputIsDeliveredMuchLater(double pressed, bool detonated)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K };
        var mine = new BmsLandmine { Beatmap = beatmap, Column = 1, StartTime = 1000 };
        var output = new BmsRecordingJudgementOutput();
        var engine = new BmsJudgementEngine([mine], beatmap.LayoutVariant, 1, output);
        engine.QueueInput(pressed, 1, true, false);
        engine.ProcessFrame(new BmsFrameContext(5000, 1), 5000);
        var state = engine.GetColumn(1).GetLandmineState(mine);
        Assert.That(state.HandledTime, Is.EqualTo(1000));
        Assert.That(state.Detonated, Is.EqualTo(detonated));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SameTimeMineSeesTheFinalInputState(bool finalPressed)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K };
        var mine = new BmsLandmine { Beatmap = beatmap, Column = 1, StartTime = 1000 };
        var engine = new BmsJudgementEngine([mine], beatmap.LayoutVariant, 1, new BmsRecordingJudgementOutput());
        engine.QueueInput(1000, 1, true, false);
        engine.QueueInput(1000, 1, false, false);
        if (finalPressed) engine.QueueInput(1000, 1, true, false);
        engine.ProcessFrame(new BmsFrameContext(2000, 1), 2000);
        Assert.That(engine.GetColumn(1).GetLandmineState(mine).Detonated, Is.EqualTo(finalPressed));
    }

    [Test]
    public void AllSameTimeInputsPrecedeAutomaticEventsInColumnAndChartOrder()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2 };
        var first = new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 };
        var second = new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 };
        var inputTime = BmsJudgementWindowTable.TimeAfter(1000,
            BmsJudgementProfileProvider.GetTable(beatmap.LayoutVariant, 1, 2, false).SlowWindowFor(HitResult.Ok));
        var third = new BmsNote { Beatmap = beatmap, Column = 2, StartTime = inputTime };
        var mine = new BmsLandmine { Beatmap = beatmap, Column = 1, StartTime = inputTime };
        var events = new List<string>();
        var left = new BmsRecordingJudgementOutput { AfterResultCheckpoint = (note, _) => events.Add(note == first ? "first" : note == second ? "second" : "mine") };
        var right = new BmsRecordingJudgementOutput { AfterResultCheckpoint = (_, _) => events.Add("input") };
        var engine = new BmsJudgementEngine(new([third], beatmap.LayoutVariant, 2, right),
            new([first, second, mine], beatmap.LayoutVariant, 1, left));
        engine.QueueInput(inputTime, 2, true, false);
        engine.QueueInput(inputTime, 1, true, false);
        engine.ProcessFrame(new BmsFrameContext(2000, 1), 2000);
        Assert.That(events, Is.EqualTo(new[] { "input", "first", "second", "mine" }));
    }

    [Test]
    public void PmsRepressAtReleaseDeadlineCancelsThePendingRelease(
        [Values(BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)] BmsLongNoteMode mode)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Pms9K, Rank = 2, LockedLongNoteMode = mode };
        var hold = new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 2000 };
        var output = new BmsRecordingJudgementOutput();
        var engine = new BmsJudgementEngine([hold], beatmap.LayoutVariant, 1, output);
        engine.QueueInput(1000, 1, true, false);
        engine.QueueInput(1500, 1, false, false);
        engine.QueueInput(1700, 1, true, false);
        engine.ProcessFrame(new BmsFrameContext(1700, 1), 1700);
        Assert.That(engine.GetColumn(1).GetLongNoteState(hold).TailJudged, Is.False);
        Assert.That(engine.GetColumn(1).GetLongNoteState(hold).PendingReleaseTime, Is.Null);
        engine.QueueInput(3000, 1, false, false);
        engine.ProcessFrame(new BmsFrameContext(5000, 1), 3300);
        Assert.That(engine.GetColumn(1).GetLongNoteState(hold).TailResult, Is.EqualTo(HitResult.Perfect));
    }

    [Test]
    public void LateDeliveryUsesInputTimeForLongNoteHead(
        [Values(BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)] BmsLongNoteMode mode)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2, LockedLongNoteMode = mode };
        var hold = new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 1000 };
        var engine = new BmsJudgementEngine([hold], beatmap.LayoutVariant, 1, new BmsRecordingJudgementOutput());
        engine.QueueInput(1005, 1, true, false);
        engine.ProcessFrame(new BmsFrameContext(1500, 1), 1500);
        var state = engine.GetColumn(1).GetLongNoteState(hold);
        Assert.That(state.HeadTime, Is.EqualTo(1005));
        Assert.That(state.HeadResult, Is.EqualTo(HitResult.Perfect));
    }

    [Test]
    public void DeadlineHandlesCancellationAtZero()
    {
        var time = BmsJudgementWindowTable.TimeAfter(-280, 280);
        Assert.That(time + 280, Is.GreaterThan(280));
        Assert.That(Math.BitDecrement(time) + 280, Is.EqualTo(280));
    }

    [Test]
    public void TimingAndWindowChangesReplaceThePendingHeadDeadline(
        [Values(null, BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)] BmsLongNoteMode? mode)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, LockedLongNoteMode = mode ?? BmsLongNoteMode.LongNote };
        BmsHitObject note = mode == null ? new BmsNote() : new BmsLongNote { Duration = 1000 };
        note.Beatmap = beatmap;
        note.Column = 1;
        note.StartTime = 1000;
        var output = new BmsRecordingJudgementOutput();
        var engine = new BmsJudgementEngine([note], beatmap.LayoutVariant, 1, output);
        var oldDeadline = engine.NextEventTime;
        note.StartTime = 2000;
        engine.GetColumn(1).RefreshNoteTiming(note);
        engine.ProcessFrame(new BmsFrameContext(oldDeadline, 1), oldDeadline);
        Assert.That(output.GetMainResult(note).HasResult, Is.False);
        if (note is BmsLongNote hold) Assert.That(engine.GetColumn(1).GetLongNoteState(hold).HeadJudged, Is.False);

        note.JudgementRate = .25;
        engine.GetColumn(1).RefreshNoteConfiguration(note);
        var expected = BmsJudgementWindowTable.TimeAfter(2000,
            BmsJudgementProfileProvider.GetTable(beatmap.LayoutVariant, 1, .25, false).SlowWindowFor(HitResult.Ok));
        Assert.That(engine.NextEventTime, Is.EqualTo(expected));
        engine.ProcessFrame(new BmsFrameContext(expected, 1), expected - oldDeadline);
        Assert.That(note is BmsLongNote longNote ? engine.GetColumn(1).GetLongNoteState(longNote).HeadJudged : output.GetMainResult(note).HasResult, Is.True);
    }

    [Test]
    public void TimingChangesReplaceThePendingMineDeadline()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K };
        var mine = new BmsLandmine { Beatmap = beatmap, Column = 1, StartTime = 1000 };
        var engine = new BmsJudgementEngine([mine], beatmap.LayoutVariant, 1, new BmsRecordingJudgementOutput());
        mine.StartTime = 2000;
        engine.GetColumn(1).RefreshNoteTiming(mine);
        Assert.That(engine.NextEventTime, Is.EqualTo(2000));
        engine.ProcessFrame(new BmsFrameContext(1000, 1), 1000);
        Assert.That(engine.GetColumn(1).GetLandmineState(mine).HandledTime, Is.Null);
        engine.ProcessFrame(new BmsFrameContext(2000, 1), 1000);
        Assert.That(engine.GetColumn(1).GetLandmineState(mine).HandledTime, Is.EqualTo(2000));
    }

    private static (List<Event> Events, BmsGaugeStateSnapshot[] Gauges, bool Failed) run(BmsLongNoteMode mode, BmsLayoutVariant layout, double interval)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = layout, Rank = 2, LockedLongNoteMode = mode };
        BmsHitObject[] objects =
        [
            new BmsLongNote { StartTime = 1000, Duration = 1200, Column = 1 },
            new BmsNote { StartTime = 1000, Column = 2 }, new BmsNote { StartTime = 1100, Column = 2 },
            new BmsLandmine { StartTime = 1200, Column = 3 }, new BmsLandmine { StartTime = 1300, Column = 3 },
            new BmsLongNote { StartTime = 1300, Duration = 500, Column = 4 },
            new BmsNote { StartTime = 1800, Column = 1 }, new BmsNote { StartTime = 2200, Column = 2 },
        ];
        foreach (var note in objects) note.Beatmap = beatmap;
        var events = new List<Event>();
        var health = new BmsHealthProcessor();
        health.SetGaugeTypes([BmsGaugeType.Hard, BmsGaugeType.Normal], initialStates:
        [
            new(BmsGaugeType.Hard, .08, false), new(BmsGaugeType.Normal, .99, false),
        ]);
        var definitions = Enumerable.Range(1, 4).Select(column =>
        {
            var output = new BmsRecordingJudgementOutput();
            output.AfterResultCheckpoint = (note, frame) =>
            {
                var result = output.GetMainResult(note);
                events.Add(new("main", frame.SimulationTime, column, result.Type, result.TimeOffset));
                health.RegisterNonConsumingJudgement(result.Type, frame.SimulationTime);
            };
            output.SyntheticEndpoint = (endpoint, applied) =>
            {
                if (!applied) return;
                var time = endpoint.ApplicationTime ?? endpoint.EventTime;
                events.Add(new("endpoint", time, column, endpoint.Result, endpoint.TimeOffset));
                health.RegisterNonConsumingJudgement(endpoint.Result, time);
            };
            output.BodyTick = (_, held, scale, frame) =>
            {
                events.Add(new(held ? "recovery" : "damage", frame.SimulationTime, column, HitResult.None, 0));
                health.ApplyHellChargeTick(held, scale, frame.SimulationTime);
            };
            return new BmsJudgementColumnDefinition(objects, layout, column, output);
        }).ToArray();
        var engine = new BmsJudgementEngine(definitions);
        (double Time, int Column, bool Pressed)[] inputs =
        [
            (1000, 1, true), (1025, 2, true), (1026, 2, false), (1100, 3, true),
            (1200, 3, false), (1300, 3, true), (1350, 3, false), (1500, 1, false),
            (1800, 1, true), (1801, 1, false), (2200, 2, true), (2201, 2, false),
        ];
        var cursor = 0;
        var previous = 0d;
        for (var time = interval; previous < 5000; time += interval)
        {
            var end = Math.Min(time, 5000);
            while (cursor < inputs.Length && inputs[cursor].Time <= end)
            {
                var input = inputs[cursor++];
                engine.QueueInput(input.Time, input.Column, input.Pressed, false);
            }
            engine.ProcessFrame(new BmsFrameContext(end, 1), end - previous);
            previous = end;
        }
        Assert.That(engine.NextEventTime, Is.EqualTo(double.PositiveInfinity));
        return (events, health.CurrentGaugeStates.ToArray(), health.HasEverFailed);
    }

    private readonly record struct Event(string Kind, double Time, int Column, HitResult Result, double Offset);
}
