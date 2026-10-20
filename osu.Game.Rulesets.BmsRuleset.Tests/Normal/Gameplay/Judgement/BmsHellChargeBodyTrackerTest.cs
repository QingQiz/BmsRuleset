using System;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsHellChargeBodyTrackerTest
{
    [Test]
    public void BodyUpdatesDoNotAllocateSnapshots([Values] bool alternate)
    {
        var tracker = new BmsHellChargeBodyTracker();
        var ticks = 0;
        Action<bool, double> tick = (_, _) => ticks++;
        tracker.Update(1, true, static (_, _) => { }, 1);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var time = 2; time <= 100001; time++)
            tracker.Update(1, !alternate || time % 2 == 0, tick, time);

        Assert.That(ticks, Is.EqualTo(alternate ? 0 : 500));
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(4096));
    }

    [TestCase(1200, 1, 0)]
    [TestCase(1600, 0, 1)]
    public void RebuiltPartialIntervalsKeepTheNextTick(double target, int expectedRecovery, int expectedDamage)
    {
        var tracker = new BmsHellChargeBodyTracker();
        tracker.Update(500, true, static (_, _) => { }, 1500);
        tracker.Update(500, false, static (_, _) => { }, 2000);

        tracker.Reset(1000);
        var heldEnd = Math.Min(target, 1500);
        tracker.Update(heldEnd - 1000, true, static (_, _) => { }, heldEnd);
        if (target > heldEnd)
            tracker.Update(target - heldEnd, false, static (_, _) => { }, target);

        var recovered = 0;
        var damaged = 0;
        tracker.Update(target < 1500 ? 1 : 201, target < 1500, (held, _) =>
        {
            if (held) recovered++;
            else damaged++;
        });
        Assert.That((recovered, damaged), Is.EqualTo((expectedRecovery, expectedDamage)));
    }
}
