using System;
using System.Collections.Generic;
using System.Threading;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

internal readonly record struct BmsPcmChunk(long StartFrame, int FrameCount, float[] Samples)
{
    internal long EndFrame => StartFrame + FrameCount;
}

internal enum BmsPcmAssetState
{
    Preparing,
    Ready,
    Complete,
    Failed,
    Disposed,
}

internal sealed class BmsPcmAsset
{
    private const int chunks_per_page = 64;

    private BmsPcmChunk[][] chunkPages = [];
    private int publishedChunkCount;
    private long publishedFrameCount;
    private long totalFrameCount = -1;
    private long residentBytes;
    private long originalDurationBits = BitConverter.DoubleToInt64Bits(double.NaN);
    private int state = (int)BmsPcmAssetState.Preparing;

    internal long TotalFrameCount => Interlocked.Read(ref totalFrameCount);

    internal long PublishedFrameCount => Interlocked.Read(ref publishedFrameCount);

    internal int SampleRate { get; }

    internal int Channels { get; }

    internal BmsPcmAssetState State => (BmsPcmAssetState)Volatile.Read(ref state);

    internal bool IsComplete => State == BmsPcmAssetState.Complete;

    internal IReadOnlyList<BmsPcmChunk> Chunks
    {
        get
        {
            var count = Volatile.Read(ref publishedChunkCount);
            var result = new BmsPcmChunk[count];

            for (var i = 0; i < count; i++)
                result[i] = getPublishedChunk(i);

            return result;
        }
    }

    internal long ResidentBytes => Interlocked.Read(ref residentBytes);

    internal double? OriginalDurationMilliseconds
    {
        get
        {
            var value = BitConverter.Int64BitsToDouble(Interlocked.Read(ref originalDurationBits));
            return double.IsFinite(value) && value >= 0 ? value : null;
        }
    }

    internal BmsPcmAsset(int sampleRate, int channels)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        if (channels <= 0)
            throw new ArgumentOutOfRangeException(nameof(channels));

        SampleRate = sampleRate;
        Channels = channels;
    }

    internal BmsPcmAsset(IEnumerable<BmsPcmChunk> chunks, long totalFrameCount, int sampleRate, int channels)
        : this(sampleRate, channels)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        foreach (var chunk in chunks)
            Publish(chunk);

        Complete(totalFrameCount);
    }

    internal void Publish(BmsPcmChunk chunk)
    {
        if (State is BmsPcmAssetState.Complete or BmsPcmAssetState.Failed or BmsPcmAssetState.Disposed)
            throw new InvalidOperationException("PCM chunks cannot be published after processing has ended.");

        var expectedStart = Interlocked.Read(ref publishedFrameCount);
        if (chunk.StartFrame != expectedStart || chunk.FrameCount <= 0 || chunk.Samples.Length != chunk.FrameCount * Channels)
            throw new ArgumentException("PCM chunks must be contiguous and contain complete interleaved frames.", nameof(chunk));

        var chunkIndex = Volatile.Read(ref publishedChunkCount);
        var pageIndex = chunkIndex / chunks_per_page;
        var pageOffset = chunkIndex % chunks_per_page;
        var pages = chunkPages;

        if (pageIndex >= pages.Length)
        {
            var expanded = new BmsPcmChunk[Math.Max(pageIndex + 1, Math.Max(1, pages.Length * 2))][];
            Array.Copy(pages, expanded, pages.Length);
            pages = expanded;
            Volatile.Write(ref chunkPages, pages);
        }

        pages[pageIndex] ??= new BmsPcmChunk[chunks_per_page];
        pages[pageIndex][pageOffset] = chunk;

        Interlocked.Add(ref residentBytes, (long)chunk.Samples.Length * sizeof(float));
        Interlocked.Exchange(ref publishedFrameCount, chunk.EndFrame);
        Volatile.Write(ref publishedChunkCount, chunkIndex + 1);
    }

    internal void MarkReady()
    {
        if (State == BmsPcmAssetState.Preparing && PublishedFrameCount > 0)
            Volatile.Write(ref state, (int)BmsPcmAssetState.Ready);
    }

    internal void Complete(long frameCount)
    {
        if (frameCount < 0 || frameCount != PublishedFrameCount)
            throw new ArgumentOutOfRangeException(nameof(frameCount), "The completed length must match all published PCM chunks.");

        Interlocked.Exchange(ref totalFrameCount, frameCount);
        Volatile.Write(ref state, (int)BmsPcmAssetState.Complete);
    }

    internal void SetOriginalDuration(double? durationMilliseconds)
    {
        if (durationMilliseconds is not { } duration || !double.IsFinite(duration) || duration < 0)
            return;

        Interlocked.Exchange(ref originalDurationBits, BitConverter.DoubleToInt64Bits(duration));
    }

    internal void Fail()
    {
        if (State != BmsPcmAssetState.Disposed)
            Volatile.Write(ref state, (int)BmsPcmAssetState.Failed);
    }

    internal void DisposePublishedChunks()
    {
        Volatile.Write(ref state, (int)BmsPcmAssetState.Disposed);
        Volatile.Write(ref publishedChunkCount, 0);
        Interlocked.Exchange(ref publishedFrameCount, 0);
        Interlocked.Exchange(ref residentBytes, 0);
        Volatile.Write(ref chunkPages, []);
    }

    internal bool TryReadStereoFrame(long frame, out float left, out float right)
    {
        if (frame < 0 || frame >= PublishedFrameCount)
        {
            left = right = 0;
            return false;
        }

        var low = 0;
        var high = Volatile.Read(ref publishedChunkCount) - 1;

        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var chunk = getPublishedChunk(middle);

            if (frame < chunk.StartFrame)
            {
                high = middle - 1;
                continue;
            }

            if (frame >= chunk.EndFrame)
            {
                low = middle + 1;
                continue;
            }

            var index = checked((int)((frame - chunk.StartFrame) * Channels));
            left = chunk.Samples[index];
            right = Channels == 1 ? left : chunk.Samples[index + 1];
            return true;
        }

        left = right = 0;
        return false;
    }

    private BmsPcmChunk getPublishedChunk(int index)
    {
        var pages = Volatile.Read(ref chunkPages);
        return pages[index / chunks_per_page][index % chunks_per_page];
    }
}
