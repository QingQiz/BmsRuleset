#nullable enable

using System.Threading;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

[TestFixture, Platform("Win")]
public class BmsThreadCpuCounterTest
{
    [Test]
    public void CurrentThreadCpuTimeIsMonotonic()
    {
        var first = BmsThreadCpuCounter.Read();
        var second = BmsThreadCpuCounter.Read();
        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.Not.Null);
        Assert.That(BmsThreadCpuCounter.ElapsedMilliseconds(first, second), Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void SamplesFromDifferentThreadsCannotFormAnInterval()
    {
        var first = BmsThreadCpuCounter.Read();
        BmsThreadCpuCounter.Snapshot? other = null;
        var worker = new Thread(() => other = BmsThreadCpuCounter.Read());
        worker.Start();
        worker.Join();
        Assert.That(first, Is.Not.Null);
        Assert.That(other, Is.Not.Null);
        Assert.That(BmsThreadCpuCounter.ElapsedMilliseconds(first, other), Is.Null);
    }

    [Test]
    public void CycleTimeMeasuresExecutionOnTheCallingThread()
    {
        var first = BmsThreadCpuCounter.ReadCycles();
        var total = 0L;
        for (var i = 0; i < 100_000; i++)
            total += i;
        var second = BmsThreadCpuCounter.ReadCycles();
        Assert.That(total, Is.EqualTo(4_999_950_000L));
        Assert.That(BmsThreadCpuCounter.ElapsedCycles(first, second), Is.GreaterThan(0));

        BmsThreadCpuCounter.CycleSnapshot? other = null;
        var worker = new Thread(() => other = BmsThreadCpuCounter.ReadCycles());
        worker.Start();
        worker.Join();
        Assert.That(other, Is.Not.Null);
        Assert.That(BmsThreadCpuCounter.ElapsedCycles(first, other), Is.Null);
        Assert.That(BmsThreadCpuCounter.ElapsedCycles(second, first), Is.Null);
    }

    [Test]
    public void CapturedThreadCanBeMeasuredAfterExitFromAnotherThread()
    {
        BmsThreadCpuCounter.CapturedThread? captured = null;
        BmsThreadCpuCounter.Snapshot? initialCpu = null;
        BmsThreadCpuCounter.CycleSnapshot? initialCycles = null;
        var total = 0L;
        var worker = new Thread(() =>
        {
            captured = BmsThreadCpuCounter.CaptureCurrentThread();
            initialCpu = captured?.Read();
            initialCycles = captured?.ReadCycles();
            for (var i = 0; i < 1_000_000; i++)
                total += i;
        });
        worker.Start();
        worker.Join();
        using (captured)
        {
            Assert.That(total, Is.EqualTo(499_999_500_000L));
            Assert.That(captured, Is.Not.Null);
            Assert.That(initialCpu, Is.Not.Null);
            Assert.That(initialCycles, Is.Not.Null);
            Assert.That(BmsThreadCpuCounter.ElapsedMilliseconds(initialCpu, captured!.Read()), Is.GreaterThanOrEqualTo(0));
            Assert.That(BmsThreadCpuCounter.ElapsedCycles(initialCycles, captured.ReadCycles()), Is.GreaterThan(0));
            Assert.That(BmsThreadCpuCounter.ElapsedCycles(BmsThreadCpuCounter.ReadCycles(), captured.ReadCycles()), Is.Null);
        }
    }

    [Test]
    public void DisposedCapturedThreadHasNoSamples()
    {
        using var captured = BmsThreadCpuCounter.CaptureCurrentThread();
        Assert.That(captured, Is.Not.Null);
        captured!.Dispose();
        Assert.That(captured.Read(), Is.Null);
        Assert.That(captured.ReadCycles(), Is.Null);
    }
}
