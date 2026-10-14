using System;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;

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

        Assert.That(ticks, Is.EqualTo(499));
        Assert.That(allocated, Is.LessThan(100000));
        tracker.Rewind(99800.5);
        ticks = 0;
        tracker.Update(.5, true, tick, 99801);
        Assert.That(ticks, Is.Zero, "The tick already happened before the seek, even between observed frames.");
        tracker.Rewind(99800);
        tracker.Update(1, true, tick, 99801);
        Assert.That(ticks, Is.EqualTo(1));
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

        Assert.That(ticks, Is.EqualTo(500));
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(4096));
    }

    [Test]
    public void RewindRestoresTimeInsideHeldAndReleasedIntervals()
    {
        var tracker = new BmsHellChargeBodyTracker();
        var recovered = 0;
        var damaged = 0;
        void tick(bool held, double _) { if (held) recovered++; else damaged++; }
        tracker.Update(500, true, tick, 1500);
        tracker.Update(500, false, tick, 2000);
        Assert.That((recovered, damaged), Is.EqualTo((2, 1)));

        tracker.Rewind(1600);
        recovered = damaged = 0;
        tracker.Update(201, false, tick, 1801);
        Assert.That((recovered, damaged), Is.EqualTo((0, 1)));
        tracker.Rewind(1200);
        recovered = damaged = 0;
        tracker.Update(1, true, tick, 1201);
        Assert.That((recovered, damaged), Is.EqualTo((1, 0)));

        tracker.Reset();
        recovered = damaged = 0;
        tracker.Update(100, false, tick, 1100);
        tracker.Update(100, true, tick, 1200);
        Assert.That((recovered, damaged), Is.EqualTo((0, 0)));
    }
}
