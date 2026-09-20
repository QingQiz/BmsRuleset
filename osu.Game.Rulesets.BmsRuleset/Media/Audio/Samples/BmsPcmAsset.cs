using System;
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

    private BmsPcmChunk[]?[] chunkPages = [];
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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);

        SampleRate = sampleRate;
        Channels = channels;
    }

    internal void Publish(BmsPcmChunk chunk)
    {
        if (State is BmsPcmAssetState.Complete or BmsPcmAssetState.Failed or BmsPcmAssetState.Disposed)
            throw new InvalidOperationException("PCM chunks cannot be published after processing has ended.");

        var expectedStart = Interlocked.Read(ref publishedFrameCount);
        if (chunk.StartFrame != expectedStart || chunk.FrameCount <= 0 || chunk.Samples.Length != chunk.FrameCount * Channels)
            throw new ArgumentException(@"PCM chunks must be contiguous and contain complete interleaved frames.", nameof(chunk));

        var chunkIndex = Volatile.Read(ref publishedChunkCount);
        var pageIndex = chunkIndex / chunks_per_page;
        var pageOffset = chunkIndex % chunks_per_page;
        var pages = chunkPages;

        if (pageIndex >= pages.Length)
        {
            var expanded = new BmsPcmChunk[]?[Math.Max(pageIndex + 1, Math.Max(1, pages.Length * 2))];
            Array.Copy(pages, expanded, pages.Length);
            pages = expanded;
            Volatile.Write(ref chunkPages, pages);
        }

        var page = pages[pageIndex] ??= new BmsPcmChunk[chunks_per_page];
        page[pageOffset] = chunk;

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
            throw new ArgumentOutOfRangeException(nameof(frameCount), @"The completed length must match all published PCM chunks.");

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
        var cursor = new ReadCursor();
        return TryReadStereoFrame(frame, ref cursor, out left, out right);
    }

    internal struct ReadCursor
    {
        internal BmsPcmAsset? Owner;
        internal BmsPcmChunk Chunk;
    }

    internal bool TryReadStereoFrame(long frame, ref ReadCursor cursor, out float left, out float right)
    {
        if (State == BmsPcmAssetState.Disposed)
        {
            left = right = 0;
            return false;
        }

        // Voices advance sequentially but may share an asset at different offsets. Keep
        // the published chunk on each voice, avoiding a search for every sample frame.
        var chunk = cursor.Chunk;
        if (!ReferenceEquals(cursor.Owner, this) || frame < chunk.StartFrame || frame >= chunk.EndFrame)
        {
            if (!tryFindChunk(frame, out chunk))
            {
                left = right = 0;
                return false;
            }

            cursor.Owner = this;
            cursor.Chunk = chunk;
        }

        var index = checked((int)((frame - chunk.StartFrame) * Channels));
        left = chunk.Samples[index];
        right = Channels == 1 ? left : chunk.Samples[index + 1];
        return true;
    }

    private bool tryFindChunk(long frame, out BmsPcmChunk chunk)
    {
        chunk = default;
        if (frame < 0 || frame >= PublishedFrameCount)
            return false;

        var low = 0;
        var high = Volatile.Read(ref publishedChunkCount) - 1;
        var pages = Volatile.Read(ref chunkPages);

        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var pageIndex = middle / chunks_per_page;
            if (pageIndex >= pages.Length || pages[pageIndex] is not { } page)
                return false;

            chunk = page[middle % chunks_per_page];

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

            return true;
        }

        return false;
    }
}
