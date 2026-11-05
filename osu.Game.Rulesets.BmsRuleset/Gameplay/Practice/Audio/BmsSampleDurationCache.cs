using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ManagedBass;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Practice.Audio;

internal enum BmsSampleDurationState
{
    Unknown,
    Known,
    Failed,
}

internal readonly record struct BmsSampleDuration(BmsSampleDurationState State, double Milliseconds = 0);

internal sealed class BmsSampleDurationCache(Func<string, BmsSampleDuration>? probe = null)
{
    private readonly ConcurrentDictionary<string, Lazy<Task<BmsSampleDuration>>> durations = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim probingSlots = new(2, 2);

    internal async Task<IReadOnlyDictionary<string, BmsSampleDuration>> GetAsync(IEnumerable<string> resources, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requests = resources.Distinct(StringComparer.Ordinal).ToDictionary(path => path,
            path => durations.GetOrAdd(path, identity => new Lazy<Task<BmsSampleDuration>>(
                () => Task.Run(() => probeAsync(identity)), LazyThreadSafetyMode.ExecutionAndPublication)).Value);
        // A cancelled attempt must not cancel a query shared with the next attempt.
        await Task.WhenAll(requests.Values).WaitAsync(cancellationToken).ConfigureAwait(false);
        return requests.ToDictionary(pair => pair.Key, pair => pair.Value.GetAwaiter().GetResult());
    }

    private async Task<BmsSampleDuration> probeAsync(string path)
    {
        await probingSlots.WaitAsync().ConfigureAwait(false);
        try
        {
            return (probe ?? Probe)(path);
        }
        catch (Exception exception)
        {
            BmsLogger.LogAudioFailure($"Failed to inspect BMS sample duration '{path}'.", exception);
            return new BmsSampleDuration(BmsSampleDurationState.Failed);
        }
        finally
        {
            probingSlots.Release();
        }
    }

    internal static BmsSampleDuration Probe(string path)
    {
        if (Path.GetExtension(path).Equals(".flac", StringComparison.OrdinalIgnoreCase))
        {
            // The audio resource store expands FLAC to WAV. STREAMINFO gives the duration
            // without decoding or allocating that waveform just to reject an expired sample.
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[42];
            stream.ReadExactly(header);
            if (!header[..4].SequenceEqual("fLaC"u8) || (header[4] & 0x7f) != 0
                                                    || header[5] != 0 || header[6] != 0 || header[7] != 34)
                return new BmsSampleDuration(BmsSampleDurationState.Unknown);

            var info = BinaryPrimitives.ReadUInt64BigEndian(header[18..26]);
            var sampleRate = info >> 44;
            var samples = info & 0xfffffffff;
            return sampleRate == 0 || samples == 0
                ? new BmsSampleDuration(BmsSampleDurationState.Unknown)
                : new BmsSampleDuration(BmsSampleDurationState.Known, samples * 1000d / sampleRate);
        }

        var handle = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
        if (handle == 0)
            return new BmsSampleDuration(BmsSampleDurationState.Failed);

        try
        {
            // Practice retries can replace the output device while a metadata query is running.
            if (Bass.ChannelGetDevice(handle) != 0x20000 && !Bass.ChannelSetDevice(handle, 0x20000))
                return new BmsSampleDuration(BmsSampleDurationState.Failed);

            var length = Bass.ChannelGetLength(handle);
            var seconds = length >= 0 ? Bass.ChannelBytes2Seconds(handle, length) : -1;
            return double.IsFinite(seconds) && seconds >= 0
                ? new BmsSampleDuration(BmsSampleDurationState.Known, seconds * 1000)
                : new BmsSampleDuration(BmsSampleDurationState.Unknown);
        }
        finally
        {
            Bass.StreamFree(handle);
        }
    }
}
