using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing.Pcm;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[TestFixture]
public class BmsVoiceCommandQueueTest
{
    [Test]
    public void DueCheckPreservesFutureHeadAndPayload()
    {
        var queue = new BmsVoiceCommandQueue();
        Assert.That(queue.HasDueCommand(long.MaxValue), Is.False);
        var future = new BmsVoiceCommand(BmsVoiceCommandType.SetVoiceGain, 100, 7, Value: 0.25f, VoiceId: 123);
        queue.Enqueue(future);
        queue.Enqueue(command(0));

        for (var frame = 0; frame < 100; frame++)
            Assert.That(queue.HasDueCommand(frame), Is.False);

        Assert.That(queue.HasDueCommand(100), Is.True);
        Assert.That(queue.HasDueCommand(99), Is.False);
        Assert.That(queue.TryDequeue(out var first), Is.True);
        Assert.That(first, Is.EqualTo(future));
        Assert.That(queue.HasDueCommand(0), Is.True);
        Assert.That(queue.TryDequeue(out var second), Is.True);
        Assert.That(second, Is.EqualTo(command(0)));
        Assert.That(queue.HasDueCommand(long.MaxValue), Is.False);
    }

    [Test]
    public void DueCheckCrossesSegmentsWithoutConsumingFutureCommand()
    {
        var queue = new BmsVoiceCommandQueue();
        for (var i = 0; i < 4097; i++)
            queue.Enqueue(command(i));
        for (var i = 0; i < 4096; i++)
            Assert.That(queue.TryDequeue(out _), Is.True);

        Assert.That(queue.HasDueCommand(4095), Is.False);
        Assert.That(queue.HasDueCommand(4096), Is.True);
        Assert.That(queue.TryPeek(out var remaining), Is.True);
        Assert.That(remaining, Is.EqualTo(command(4096)));
        Assert.That(queue.TryDequeue(out remaining), Is.True);
        Assert.That(remaining, Is.EqualTo(command(4096)));
        Assert.That(queue.HasDueCommand(long.MaxValue), Is.False);
    }

    [Test]
    public async Task DuePollingObservesConcurrentBatchesAcrossRingAndSegmentBoundaries()
    {
        const int command_count = 16388;
        var queue = new BmsVoiceCommandQueue();
        var batch = Enumerable.Range(0, 4097).Select(i => command(i)).ToArray();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var start = new ManualResetEventSlim();
        var producer = Task.Run(() =>
        {
            start.Wait(timeout.Token);
            for (var first = 0; first < command_count; first += batch.Length)
            {
                for (var i = 0; i < batch.Length; i++)
                    batch[i] = command(first + i);
                queue.Enqueue(batch);
            }
        });

        var expected = 0;
        start.Set();
        while (expected < command_count)
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (!queue.HasDueCommand(expected))
            {
                Thread.SpinWait(1);
                continue;
            }

            Assert.That(queue.HasDueCommand(expected - 1), Is.False);
            Assert.That(queue.TryDequeue(out var dequeued), Is.True);
            Assert.That(dequeued, Is.EqualTo(command(expected)));
            expected++;
        }

        await producer;
        Assert.That(queue.HasDueCommand(long.MaxValue), Is.False);
    }

    [Test]
    public void RingCapacityIsReusedWhenConsumerKeepsUp()
    {
        var queue = new BmsVoiceCommandQueue();

        for (var i = 0; i < 10000; i++)
        {
            queue.Enqueue(command(i));
            Assert.That(queue.HasDueCommand(i - 1), Is.False);
            Assert.That(queue.HasDueCommand(i), Is.True);
            Assert.That(queue.TryDequeue(out var dequeued), Is.True);
            Assert.That(dequeued.TargetFrame, Is.EqualTo(i));
            Assert.That(queue.HasDueCommand(long.MaxValue), Is.False);
        }

        Assert.That(queue.TryDequeue(out _), Is.False);
    }

    [Test]
    public void BatchLargerThanCurrentSegmentExpandsWithoutLosingOrder()
    {
        var queue = new BmsVoiceCommandQueue();
        var batch = new BmsVoiceCommand[4097];

        for (var i = 0; i < batch.Length; i++)
            batch[i] = command(i + 1);

        queue.Enqueue(command(0));
        queue.Enqueue(batch);

        for (var expected = 0; expected <= batch.Length; expected++)
        {
            Assert.That(queue.TryDequeue(out var dequeued), Is.True);
            Assert.That(dequeued.TargetFrame, Is.EqualTo(expected));
        }

        Assert.That(queue.TryDequeue(out _), Is.False);
    }

    [Test]
    public void ConsumerDoesNotAllocateWhenCrossingSegments()
    {
        var queue = new BmsVoiceCommandQueue();

        for (var i = 0; i < 4097; i++)
            queue.Enqueue(command(i));

        var frames = new long[4097];
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < frames.Length; i++)
        {
            queue.TryDequeue(out var dequeued);
            frames[i] = dequeued.TargetFrame;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Multiple(() =>
        {
            Assert.That(frames, Is.EqualTo(Enumerable.Range(0, frames.Length).Select(value => (long)value)));
            Assert.That(allocated, Is.Zero);
            Assert.That(queue.TryDequeue(out _), Is.False);
        });
    }

    [Test]
    public async Task ConcurrentProducerAndConsumerPreserveOrder()
    {
        const int command_count = 10_000;
        var queue = new BmsVoiceCommandQueue();
        var producer = Task.Run(() =>
        {
            for (var i = 0; i < command_count; i++)
                queue.Enqueue(command(i));
        });

        var expected = 0;
        var mismatch = -1L;

        while (expected < command_count)
        {
            if (!queue.TryDequeue(out var dequeued))
            {
                Thread.SpinWait(1);
                continue;
            }

            if (dequeued.TargetFrame != expected && mismatch < 0)
                mismatch = dequeued.TargetFrame;

            expected++;
        }

        await producer;

        Assert.That(mismatch, Is.EqualTo(-1));
        Assert.That(queue.TryDequeue(out _), Is.False);
    }

    private static BmsVoiceCommand command(long targetFrame) =>
        new(BmsVoiceCommandType.SetMasterGain, targetFrame, 0, Value: targetFrame);

}
