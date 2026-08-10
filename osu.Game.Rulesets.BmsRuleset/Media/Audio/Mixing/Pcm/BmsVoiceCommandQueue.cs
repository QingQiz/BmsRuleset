using System;
using System.Threading;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing;

internal sealed class BmsVoiceCommandQueue
{
    private readonly BmsVoiceCommand[] commands;
    private int readIndex;
    private int writeIndex;

    internal int Capacity => commands.Length - 1;

    internal int Count
    {
        get
        {
            var read = Volatile.Read(ref readIndex);
            var write = Volatile.Read(ref writeIndex);
            return write >= read ? write - read : commands.Length - read + write;
        }
    }

    internal int AvailableCapacity => Capacity - Count;

    internal BmsVoiceCommandQueue(int capacity)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        commands = new BmsVoiceCommand[checked(capacity + 1)];
    }

    internal bool TryEnqueue(BmsVoiceCommand command)
    {
        var write = writeIndex;
        var nextWrite = increment(write);

        if (nextWrite == Volatile.Read(ref readIndex))
            return false;

        commands[write] = command;
        Volatile.Write(ref writeIndex, nextWrite);
        return true;
    }

    internal bool TryEnqueue(ReadOnlySpan<BmsVoiceCommand> batch)
    {
        if (batch.Length > AvailableCapacity)
            return false;

        foreach (var command in batch)
        {
            if (!TryEnqueue(command))
                throw new InvalidOperationException("The SPSC command queue capacity changed during a producer-only batch.");
        }

        return true;
    }

    internal bool TryPeek(out BmsVoiceCommand command)
    {
        var read = readIndex;

        if (read == Volatile.Read(ref writeIndex))
        {
            command = default;
            return false;
        }

        command = commands[read];
        return true;
    }

    internal bool TryDequeue(out BmsVoiceCommand command)
    {
        var read = readIndex;

        if (read == Volatile.Read(ref writeIndex))
        {
            command = default;
            return false;
        }

        command = commands[read];
        Volatile.Write(ref readIndex, increment(read));
        return true;
    }

    private int increment(int index) => ++index == commands.Length ? 0 : index;
}
