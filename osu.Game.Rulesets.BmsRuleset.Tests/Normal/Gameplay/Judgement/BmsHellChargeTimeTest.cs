using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsHellChargeTimeTest
{
    [Test]
    public void HoldingFiveHundredMillisecondsRecoversBeforeRelease()
    {
        var (engine, output) = create();
        engine.ProcessFrame(new BmsFrameContext(1200, 1), 200);
        Assert.That(output.BodyTicks, Is.Empty);
        engine.ProcessFrame(new BmsFrameContext(1201, 1), 1);
        Assert.That(output.BodyTicks, Has.Count.EqualTo(1));
        engine.ProcessFrame(new BmsFrameContext(1500, 1), 299);
        Assert.That(output.BodyTicks.Select(tick => tick.Time), Is.EqualTo(new[] { Math.BitIncrement(1200), Math.BitIncrement(1400) }));
        Assert.That(output.BodyTicks.All(tick => tick.Holding), Is.True);
    }

    [Test]
    public void AlternatingHundredMillisecondInputsCancelInsideOneUpdate()
    {
        var (engine, output) = create();
        engine.QueueInput(1100, 1, false, false);
        engine.QueueInput(1200, 1, true, false);
        engine.QueueInput(1300, 1, false, false);
        engine.QueueInput(1400, 1, true, false);
        engine.ProcessFrame(new BmsFrameContext(1400, 1), 400);
        Assert.That(output.BodyTicks, Is.Empty);
    }

    [Test]
    public void HeldTimeEndsAtReleaseAndReleasedTimeStartsThere()
    {
        var (engine, output) = create();
        engine.QueueInput(1500, 1, false, false);
        engine.ProcessFrame(new BmsFrameContext(2000, 1), 1000);
        Assert.That(output.BodyTicks.Select(tick => (tick.Holding, tick.Time)), Is.EqualTo(new[]
        {
            (true, Math.BitIncrement(1200)), (true, Math.BitIncrement(1400)), (false, Math.BitIncrement(1800)),
        }));
    }

    [TestCase(1)]
    [TestCase(1000.0 / 240)]
    [TestCase(500)]
    public void TickTimesAndDirectionsAreIndependentOfUpdatePartition(double step)
    {
        var expected = run(500);
        var actual = run(step);
        Assert.That(actual, Is.EqualTo(expected));

        static (bool, double)[] run(double interval)
        {
            var (engine, output) = create();
            var inputs = new[] { (1500d, false), (2000d, true), (2600d, false), (2950d, true) };
            var next = 0;
            var previous = 1000d;
            for (var time = 1000 + interval; previous < 3500; time += interval)
            {
                var end = Math.Min(3500, time);
                while (next < inputs.Length && inputs[next].Item1 <= end)
                {
                    engine.QueueInput(inputs[next].Item1, 1, inputs[next].Item2, false);
                    next++;
                }
                engine.ProcessFrame(new BmsFrameContext(end, 1), end - previous);
                previous = end;
            }
            return output.BodyTicks.Select(tick => (tick.Holding, tick.Time)).ToArray();
        }
    }

    [Test]
    public void SignedAccumulatorCancelsHundredMillisecondIntervals()
    {
        var tracker = new BmsHellChargeBodyTracker();
        var ticks = 0;
        for (var i = 0; i < 4; i++) tracker.Update(100, i % 2 == 0, (_, _) => ticks++);
        Assert.That(ticks, Is.Zero);
        tracker.Update(500, true, (_, _) => ticks++);
        Assert.That(ticks, Is.EqualTo(2));
    }

    [Test]
    public void DelayedUpdateInterleavesColumnsInGaugeOrder()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2, LockedLongNoteMode = BmsLongNoteMode.HellChargeNote };
        var released = new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 3000 };
        var held = new BmsLongNote { Beatmap = beatmap, Column = 2, StartTime = 1000, Duration = 3000 };
        var output = new BmsRecordingJudgementOutput();
        var health = createHealth();
        output.BodyTick = (_, holding, scale, frame) => health.ApplyHellChargeTick(holding, scale, frame.SimulationTime);
        var engine = new BmsJudgementEngine(
            new([released], beatmap.LayoutVariant, 1, output),
            new([held], beatmap.LayoutVariant, 2, output));


        engine.QueueInput(1000, 1, true, false);
        engine.QueueInput(1000, 2, true, false);
        engine.ProcessFrame(new BmsFrameContext(1000, 1), 0);
        engine.QueueInput(1100, 1, false, false);
        engine.ProcessFrame(new BmsFrameContext(1900, 1), 900);

        (int Column, bool Holding, double Time)[] expected =
        [
            (2, true, Math.BitIncrement(1200)),
            (1, false, Math.BitIncrement(1400)), (2, true, Math.BitIncrement(1400)),
            (1, false, Math.BitIncrement(1600)), (2, true, Math.BitIncrement(1600)),
            (1, false, Math.BitIncrement(1800)), (2, true, Math.BitIncrement(1800)),
        ];
        Assert.That(output.BodyTicks.Select(tick => (tick.Note.Column, tick.Holding, tick.Time)), Is.EqualTo(expected));
        var expectedHealth = createHealth();
        foreach (var tick in expected) expectedHealth.ApplyHellChargeTick(tick.Holding, eventTime: tick.Time);
        Assert.That(health.CurrentGaugeStates, Is.EqualTo(expectedHealth.CurrentGaugeStates));
        Assert.That(health.GaugeHistory.Select(entry => entry.Time), Is.Ordered);

        static BmsHealthProcessor createHealth()
        {
            var processor = new BmsHealthProcessor();
            processor.SetGaugeTypes([BmsGaugeType.Hard, BmsGaugeType.Normal], initialStates:
            [
                new(BmsGaugeType.Hard, 0.01, false), new(BmsGaugeType.Normal, 0.99, false),
            ]);
            return processor;
        }
    }

    private static (BmsJudgementEngine Engine, BmsRecordingJudgementOutput Output) create()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2, LockedLongNoteMode = BmsLongNoteMode.HellChargeNote };
        var note = new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 3000 };
        var output = new BmsRecordingJudgementOutput();
        var engine = new BmsJudgementEngine([note], beatmap.LayoutVariant, 1, output);

        engine.QueueInput(1000, 1, true, false);
        engine.ProcessFrame(new BmsFrameContext(1000, 1), 0);
        return (engine, output);
    }
}
