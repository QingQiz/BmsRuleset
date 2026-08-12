using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing.Pcm;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[TestFixture]
public class BmsVoiceCommandQueueTest
{
    [Test]
    public void RingCapacityIsReusedWhenConsumerKeepsUp()
    {
        var queue = new BmsVoiceCommandQueue(2);

        for (var i = 0; i < 1000; i++)
        {
            queue.Enqueue(command(i));
            Assert.That(queue.TryDequeue(out var dequeued), Is.True);
            Assert.That(dequeued.TargetFrame, Is.EqualTo(i));
        }

        Assert.Multiple(() =>
        {
            Assert.That(queue.Count, Is.Zero);
            Assert.That(queue.ExpansionCount, Is.Zero);
        });
    }

    [Test]
    public void BatchLargerThanCurrentSegmentExpandsWithoutLosingOrder()
    {
        var queue = new BmsVoiceCommandQueue(2);
        var batch = new BmsVoiceCommand[7];

        for (var i = 0; i < batch.Length; i++)
            batch[i] = command(i + 1);

        queue.Enqueue(command(0));
        queue.Enqueue(batch);

        Assert.Multiple(() =>
        {
            Assert.That(queue.Count, Is.EqualTo(8));
            Assert.That(queue.ExpansionCount, Is.EqualTo(1));
        });

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
        var queue = new BmsVoiceCommandQueue(2);

        for (var i = 0; i < 6; i++)
            queue.Enqueue(command(i));

        var frames = new long[6];
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < frames.Length; i++)
        {
            queue.TryDequeue(out var dequeued);
            frames[i] = dequeued.TargetFrame;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Multiple(() =>
        {
            Assert.That(frames, Is.EqualTo(new long[] { 0, 1, 2, 3, 4, 5 }));
            Assert.That(allocated, Is.Zero);
            Assert.That(queue.Count, Is.Zero);
            Assert.That(queue.ExpansionCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task ConcurrentProducerAndConsumerPreserveOrder()
    {
        const int command_count = 10_000;
        var queue = new BmsVoiceCommandQueue(4);
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

        Assert.Multiple(() =>
        {
            Assert.That(mismatch, Is.EqualTo(-1));
            Assert.That(queue.Count, Is.Zero);
        });
    }

    private static BmsVoiceCommand command(long targetFrame) =>
        new(BmsVoiceCommandType.SetMasterGain, targetFrame, 0, Value: targetFrame);
}
