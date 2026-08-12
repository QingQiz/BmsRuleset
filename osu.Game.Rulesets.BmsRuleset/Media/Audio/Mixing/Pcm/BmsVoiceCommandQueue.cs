using System;
using System.Threading;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing.Pcm;

/// <summary>
///     A segmented single-producer, single-consumer queue whose consumer never allocates or blocks.
/// </summary>
internal sealed class BmsVoiceCommandQueue
{
    private readonly int segmentCapacity;
    private Segment readSegment;
    private Segment writeSegment;
    private int count;
    private long expansionCount;

    internal int Count => Volatile.Read(ref count);

    internal long ExpansionCount => Interlocked.Read(ref expansionCount);

    internal BmsVoiceCommandQueue(int segmentCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(segmentCapacity, 1);

        this.segmentCapacity = segmentCapacity;
        readSegment = writeSegment = new Segment(segmentCapacity);
    }

    internal void Enqueue(BmsVoiceCommand command)
    {
        var segment = writeSegment;

        if (tryEnqueue(segment, command))
            return;

        var next = new Segment(segmentCapacity);
        if (!tryEnqueue(next, command))
            throw new InvalidOperationException("A new command segment could not accept one command.");

        publishNextSegment(segment, next);
    }

    internal void Enqueue(ReadOnlySpan<BmsVoiceCommand> batch)
    {
        if (batch.IsEmpty)
            return;

        var segment = writeSegment;

        if (tryEnqueue(segment, batch))
            return;

        var next = new Segment(Math.Max(segmentCapacity, batch.Length));
        if (!tryEnqueue(next, batch))
            throw new InvalidOperationException("A new command segment could not accept its initial batch.");

        publishNextSegment(segment, next);
    }

    internal bool TryPeek(out BmsVoiceCommand command)
    {
        while (true)
        {
            var segment = readSegment;
            var read = segment.ReadIndex;

            if (read != Volatile.Read(ref segment.WriteIndex))
            {
                command = segment.Commands[read];
                return true;
            }

            var next = Volatile.Read(ref segment.Next);
            if (next == null)
            {
                command = default;
                return false;
            }

            readSegment = next;
        }
    }

    internal bool TryDequeue(out BmsVoiceCommand command)
    {
        while (true)
        {
            var segment = readSegment;
            var read = segment.ReadIndex;

            if (read != Volatile.Read(ref segment.WriteIndex))
            {
                command = segment.Commands[read];
                segment.Commands[read] = default;
                Volatile.Write(ref segment.ReadIndex, segment.Increment(read));
                Interlocked.Decrement(ref count);
                return true;
            }

            var next = Volatile.Read(ref segment.Next);
            if (next == null)
            {
                command = default;
                return false;
            }

            readSegment = next;
        }
    }

    private bool tryEnqueue(Segment segment, BmsVoiceCommand command)
    {
        var write = segment.WriteIndex;
        var nextWrite = segment.Increment(write);

        if (nextWrite == Volatile.Read(ref segment.ReadIndex))
            return false;

        segment.Commands[write] = command;
        Interlocked.Increment(ref count);
        Volatile.Write(ref segment.WriteIndex, nextWrite);
        return true;
    }

    private bool tryEnqueue(Segment segment, ReadOnlySpan<BmsVoiceCommand> batch)
    {
        if (batch.Length > segment.AvailableCapacity)
            return false;

        var write = segment.WriteIndex;

        foreach (var command in batch)
        {
            segment.Commands[write] = command;
            write = segment.Increment(write);
        }

        // Publishing once keeps a same-frame chord invisible until the complete batch is ready.
        Interlocked.Add(ref count, batch.Length);
        Volatile.Write(ref segment.WriteIndex, write);
        return true;
    }

    private void publishNextSegment(Segment previous, Segment next)
    {
        // The new segment is fully populated before the callback can observe the link.
        Volatile.Write(ref previous.Next, next);
        writeSegment = next;
        Interlocked.Increment(ref expansionCount);
    }

    private sealed class Segment
    {
        internal readonly BmsVoiceCommand[] Commands;
        internal int ReadIndex;
        internal int WriteIndex;
        internal Segment? Next;

        internal int Capacity => Commands.Length - 1;

        internal int Count
        {
            get
            {
                var read = Volatile.Read(ref ReadIndex);
                var write = WriteIndex;
                return write >= read ? write - read : Commands.Length - read + write;
            }
        }

        internal int AvailableCapacity => Capacity - Count;

        internal Segment(int capacity)
        {
            Commands = new BmsVoiceCommand[checked(capacity + 1)];
        }

        internal int Increment(int index) => ++index == Commands.Length ? 0 : index;
    }
}
