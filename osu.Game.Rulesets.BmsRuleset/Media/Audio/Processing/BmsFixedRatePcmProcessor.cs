using System;
using System.Collections.Generic;
using System.Threading;
using ManagedBass;
using ManagedBass.Fx;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Processing;

internal interface IBmsPcmSource : IDisposable
{
    int SampleRate { get; }

    int Channels { get; }

    double? OriginalDurationMilliseconds { get; }

    int Read(float[] buffer, int offset, int count);
}

internal sealed class BmsFixedRatePcmProcessor : IDisposable
{
    internal const int OUTPUT_SAMPLE_RATE = 44100;
    internal const int OUTPUT_CHANNELS = 2;
    internal const int DEFAULT_CHUNK_FRAMES = 4096;
    private const int source_buffer_frames = 4096;

    private readonly IBmsPcmSource source;
    private bool processingStarted;
    private bool disposed;

    internal double? OriginalDurationMilliseconds => source.OriginalDurationMilliseconds;

    internal BmsFixedRatePcmProcessor(IBmsPcmSource source, double rate)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!double.IsFinite(rate) || rate < 0.05 || rate > 2)
            throw new ArgumentOutOfRangeException(nameof(rate), @"BMS audio rate must be between 0.05 and 2.");

        if (source.SampleRate <= 0 || source.Channels <= 0)
            throw new ArgumentException(@"The PCM source must expose a valid sample format.", nameof(source));

        this.source = source;
    }

    internal static BmsFixedRatePcmProcessor CreateFromMemory(byte[] data, double rate) =>
        new(BmsBassPcmSource.FromMemory(data, rate), rate);

    internal IEnumerable<BmsPcmChunk> ProcessChunks(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (processingStarted)
            throw new InvalidOperationException("A PCM processor owns one continuous decode state and can only be consumed once.");

        processingStarted = true;

        var sourceChannels = source.Channels;
        var inputBuffer = new float[source_buffer_frames * sourceChannels];
        var pendingSamples = new List<float>(inputBuffer.Length * 2);
        var pendingStartFrame = 0L;
        var sourceFramesRead = 0L;
        var sourceEnded = false;
        var sourcePosition = 0d;
        var outputFrame = 0L;
        var sourceFramesPerOutputFrame = (double)source.SampleRate / OUTPUT_SAMPLE_RATE;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var samples = new float[DEFAULT_CHUNK_FRAMES * OUTPUT_CHANNELS];
            var producedFrames = 0;

            while (producedFrames < DEFAULT_CHUNK_FRAMES)
            {
                var firstFrame = (long)Math.Floor(sourcePosition);
                if (!ensureSourceFrame(firstFrame))
                    break;

                var hasSecondFrame = ensureSourceFrame(firstFrame + 1);
                var secondFrame = hasSecondFrame ? firstFrame + 1 : firstFrame;
                var fraction = (float)(sourcePosition - firstFrame);
                var outputIndex = producedFrames * OUTPUT_CHANNELS;

                for (var channel = 0; channel < OUTPUT_CHANNELS; channel++)
                {
                    var first = getSample(firstFrame, channel);
                    var second = getSample(secondFrame, channel);
                    samples[outputIndex + channel] = first + (second - first) * fraction;
                }

                producedFrames++;
                sourcePosition += sourceFramesPerOutputFrame;

                var retainFrom = Math.Max(pendingStartFrame, (long)Math.Floor(sourcePosition) - 1);
                var removableFrames = retainFrom - pendingStartFrame;

                if (removableFrames > 0)
                {
                    pendingSamples.RemoveRange(0, checked((int)(removableFrames * sourceChannels)));
                    pendingStartFrame = retainFrom;
                }
            }

            if (producedFrames == 0)
                yield break;

            if (producedFrames != DEFAULT_CHUNK_FRAMES)
                Array.Resize(ref samples, producedFrames * OUTPUT_CHANNELS);

            yield return new BmsPcmChunk(outputFrame, producedFrames, samples);

            outputFrame += producedFrames;
        }

        float getSample(long frame, int outputChannel)
        {
            var inputChannel = sourceChannels == 1 ? 0 : Math.Min(outputChannel, sourceChannels - 1);
            var index = checked((int)((frame - pendingStartFrame) * sourceChannels + inputChannel));
            return pendingSamples[index];
        }

        bool ensureSourceFrame(long requiredFrame)
        {
            while (sourceFramesRead <= requiredFrame && !sourceEnded)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var samplesRead = source.Read(inputBuffer, 0, inputBuffer.Length);

                if (samplesRead < 0 || samplesRead > inputBuffer.Length)
                    throw new InvalidOperationException("The PCM source returned an invalid sample count.");

                if (samplesRead == 0)
                {
                    sourceEnded = true;
                    break;
                }

                if (samplesRead % sourceChannels != 0)
                    throw new InvalidOperationException("The PCM source returned an incomplete sample frame.");

                for (var i = 0; i < samplesRead; i++)
                    pendingSamples.Add(inputBuffer[i]);

                sourceFramesRead += samplesRead / sourceChannels;
            }

            return requiredFrame < sourceFramesRead;
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        source.Dispose();
    }

    private sealed class BmsBassPcmSource : IBmsPcmSource
    {
        private readonly byte[]? retainedData;
        private int outputHandle;

        public int SampleRate { get; }

        public int Channels { get; }

        public double? OriginalDurationMilliseconds { get; }

        private BmsBassPcmSource(int sourceHandle, byte[]? retainedData, double rate)
        {
            if (sourceHandle == 0)
                throw new InvalidOperationException($"BASS failed to create a decode stream: {Bass.LastError}.");

            this.retainedData = retainedData;
            outputHandle = sourceHandle;

            var byteLength = Bass.ChannelGetLength(sourceHandle);
            var seconds = byteLength >= 0 ? Bass.ChannelBytes2Seconds(sourceHandle, byteLength) : -1;
            OriginalDurationMilliseconds = seconds >= 0 ? seconds * 1000 : null;

            try
            {
                if (Math.Abs(rate - 1) > 0.000001)
                {
                    var tempoHandle = BassFx.TempoCreate(sourceHandle, BassFlags.Decode | BassFlags.FxFreeSource);
                    if (tempoHandle == 0)
                        throw new InvalidOperationException($"BASS_FX failed to create a tempo stream: {Bass.LastError}.");

                    outputHandle = tempoHandle;

                    Bass.ChannelSetAttribute(outputHandle, ChannelAttribute.TempoUseQuickAlgorithm, 1);
                    Bass.ChannelSetAttribute(outputHandle, ChannelAttribute.TempoOverlapMilliseconds, 4);
                    Bass.ChannelSetAttribute(outputHandle, ChannelAttribute.TempoSequenceMilliseconds, 30);
                    Bass.ChannelSetAttribute(outputHandle, ChannelAttribute.Tempo, (float)((rate - 1) * 100));
                }

                if (!Bass.ChannelGetInfo(outputHandle, out var info))
                    throw new InvalidOperationException($"BASS failed to inspect a PCM stream: {Bass.LastError}.");

                SampleRate = info.Frequency;
                Channels = info.Channels;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal static BmsBassPcmSource FromMemory(byte[] data, double rate)
        {
            ArgumentNullException.ThrowIfNull(data);
            var handle = Bass.CreateStream(data, 0, data.LongLength, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
            return new BmsBassPcmSource(handle, data, rate);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            ObjectDisposedException.ThrowIf(outputHandle == 0, this);

            if (offset != 0)
                throw new ArgumentOutOfRangeException(nameof(offset), @"BASS PCM reads must start at the beginning of the supplied buffer.");

            if (count < 0 || count > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            var bytesRead = Bass.ChannelGetData(outputHandle, buffer, count * sizeof(float));
            if (bytesRead < 0)
            {
                if (Bass.ChannelIsActive(outputHandle) == PlaybackState.Stopped)
                    return 0;

                throw new InvalidOperationException($"BASS failed while decoding PCM data: {Bass.LastError}.");
            }

            return bytesRead / sizeof(float);
        }

        public void Dispose()
        {
            if (outputHandle == 0)
                return;

            Bass.StreamFree(outputHandle);
            outputHandle = 0;
            GC.KeepAlive(retainedData);
        }
    }
}
