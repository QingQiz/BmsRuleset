using System;
using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.LnHelper;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsHellChargeHistoryTest
{
    [Test]
    public void SteadyBodyHistoryDoesNotAllocateOneSnapshotPerFrame()
    {
        var tracker = new BmsHellChargeBodyTracker();
        var ticks = 0;
        Action<bool, double> tick = (_, _) => ticks++;
        tracker.Update(1, true, tick, 1);
        tracker.Reset();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var time = 1; time <= 100000; time++)
            tracker.Update(1, true, tick, time);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.WriteLine($"100,000 steady body updates allocated {allocated} bytes of history storage.");

        Assert.That(ticks, Is.EqualTo(499), "The exact 200ms boundary must not tick.");
        Assert.That(allocated, Is.LessThan(100000), "A steady 100,000-frame hold must not retain megabytes of frame snapshots.");
        tracker.Rewind(99800.5);
        ticks = 0;
        tracker.Update(1, true, tick, 99801);
        Assert.That(ticks, Is.EqualTo(1), "Rewind must restore frame 99800's pending 200ms, without interpolation.");
    }

    [Test]
    public void DisabledHistoryDoesNotAllocateFrameSnapshots()
    {
        var tracker = new BmsHellChargeBodyTracker { HistoryEnabled = false };
        var ticks = 0;
        Action<bool, double> tick = (_, _) => ticks++;
        tracker.Update(1, true, static (_, _) => { }, 1);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var time = 2; time <= 100001; time++)
            tracker.Update(1, true, tick, time);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(ticks, Is.EqualTo(500));
        Assert.That(allocated, Is.LessThan(4096), "Live HCN updates must not retain frame-history storage.");
    }

    [TestCase(1)]
    [TestCase(1000.0 / 240)]
    [TestCase(.001)]
    public void RewindMatchesUncompressedSnapshotsAcrossRunsAndIrregularFrames(double step)
    {
        var history = new BmsHellChargeHistory();
        var snapshots = new List<(double Time, double Accumulator)>();
        var random = new Random(4139);
        for (var i = 1; i <= 1600; i++)
        {
            var time = 1000 + i * step;
            // Alternating linear runs, signed tick discontinuities, and arbitrary values
            // exercise compression without deriving the oracle from the history implementation.
            var value = i % 400 < 300 ? (i % 200 - 100) * step : random.NextDouble() * 1000 - 500;
            snapshots.Add((time, value));
            history.Add(time, value);
        }

        for (var i = snapshots.Count - 1; i >= 0; i--)
        {
            var frame = snapshots[i];
            Assert.That(history.Rewind(Math.BitIncrement(frame.Time)), Is.EqualTo(frame.Accumulator), "after frame " + i);
            Assert.That(history.Rewind(frame.Time), Is.EqualTo(frame.Accumulator), "on frame " + i);
            Assert.That(history.Rewind(Math.BitDecrement(frame.Time)), Is.EqualTo(i > 0 ? snapshots[i - 1].Accumulator : 0), "before frame " + i);
        }
    }

    [Test]
    public void RewindThenAppendDiscardsFutureRunsAndRawFrames()
    {
        var history = new BmsHellChargeHistory();
        for (var i = 1; i <= 600; i++)
            history.Add(i, i % 200);
        history.Add(601.25, -723.125);

        Assert.That(history.Rewind(305.5), Is.EqualTo(105));
        for (var i = 306; i <= 550; i++)
            history.Add(i, 410 - i);
        history.Add(551.25, 0);
        Assert.That(history.Rewind(551.25), Is.Zero);
        Assert.That(history.Rewind(551), Is.EqualTo(-140));
        Assert.That(history.Rewind(309.5), Is.EqualTo(101));
        Assert.That(history.Rewind(305), Is.EqualTo(105));

        history.Clear();
        Assert.That(history.Rewind(600), Is.Zero);
        history.Add(20, 17);
        history.Add(20, 0);
        Assert.That(history.Rewind(20), Is.Zero, "The last snapshot wins at a shared timestamp.");
        Assert.That(history.Rewind(19), Is.Zero);
    }
}
