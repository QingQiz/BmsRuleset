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
    public void RingCapacityIsReusedWhenConsumerKeepsUp()
    {
        var queue = new BmsVoiceCommandQueue();

        for (var i = 0; i < 1000; i++)
        {
            queue.Enqueue(command(i));
            Assert.That(queue.TryDequeue(out var dequeued), Is.True);
            Assert.That(dequeued.TargetFrame, Is.EqualTo(i));
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
