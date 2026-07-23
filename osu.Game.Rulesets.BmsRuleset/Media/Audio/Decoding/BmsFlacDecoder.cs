using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using FFmpeg.AutoGen;
using osu.Framework.Allocation;
using osu.Framework.Platform;
using osu.Game.Rulesets.BmsRuleset.Media.FFmpeg;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Decoding;

internal sealed unsafe class BmsFlacDecoder : IDisposable
{
    private const int context_buffer_size = 4096;
    private const int initial_wave_allocation_limit = 256 * 1024 * 1024;
    private const int fallback_wave_capacity = 64 * 1024;

    private readonly BmsSupplementalFFmpegFuncs ffmpeg;
    private readonly byte[] inputData;
    private readonly MemoryStream dataStream;
    private ObjectHandle<BmsFlacDecoder> handle;
    private readonly avio_alloc_context_read_packet readPacketCallback;
    private readonly avio_alloc_context_seek seekCallback;

    private AVFormatContext* formatContext;
    private AVIOContext* ioContext;
    private AVPacket* packet;
    private AVStream* stream;
    private AVCodecContext* codecContext;
    private bool inputOpened;
    private bool disposed;

    private BmsFlacDecoder(BmsSupplementalFFmpegFuncs ffmpeg, byte[] data)
    {
        this.ffmpeg = ffmpeg;
        inputData = data;
        dataStream = new MemoryStream(data, writable: false);
        handle = new ObjectHandle<BmsFlacDecoder>(this, GCHandleType.Normal);
        readPacketCallback = readPacket;
        seekCallback = streamSeekCallback;
    }

    public static bool TryDecodeToWave(byte[] data, out byte[]? wave, out string? error)
    {
        wave = null;

        if (!BmsSupplementalFFmpegFuncs.TryCreate(out var ffmpeg, out error))
            return false;

        try
        {
            using var decoder = new BmsFlacDecoder(ffmpeg!, data);
            decoder.prepareDecoding();
            wave = decoder.decodeToWave();
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error = $"Supplemental FFmpeg FLAC decode failed: {exception.Message}";
            return false;
        }
    }

    private byte[] decodeToWave()
    {
        var decodedFrame = ffmpeg.av_frame_alloc();
        if (decodedFrame == null)
            throw new InvalidOperationException("Could not allocate an audio frame.");

        var output = new byte[getInitialWaveLength()];
        var outputPosition = 44;

        try
        {
            while (true)
            {
                var readResult = ffmpeg.av_read_frame(formatContext, packet);
                if (readResult == BmsSupplementalFFmpegFuncs.AVERROR_EOF)
                    break;

                if (readResult < 0)
                    throw new InvalidOperationException($"Audio frame read failed: {getErrorMessage(readResult)}");

                if (packet->stream_index != stream->index)
                {
                    ffmpeg.av_packet_unref(packet);
                    continue;
                }

                sendPacket(decodedFrame, ref output, ref outputPosition);
            }

            var drainResult = ffmpeg.avcodec_send_packet(codecContext, null);
            if (drainResult < 0 && drainResult != BmsSupplementalFFmpegFuncs.AVERROR_EOF)
                throw new InvalidOperationException($"Audio decoder drain failed: {getErrorMessage(drainResult)}");

            receiveFrames(decodedFrame, ref output, ref outputPosition);

            if (output.Length != outputPosition)
                Array.Resize(ref output, outputPosition);

            writeWaveHeader(output);
            return output;
        }
        finally
        {
            var framePtr = decodedFrame;
            ffmpeg.av_frame_free(&framePtr);
        }
    }

    private void sendPacket(AVFrame* decodedFrame, ref byte[] output, ref int outputPosition)
    {
        while (true)
        {
            var sendResult = ffmpeg.avcodec_send_packet(codecContext, packet);
            if (sendResult == -BmsSupplementalFFmpegFuncs.EAGAIN)
            {
                receiveFrames(decodedFrame, ref output, ref outputPosition);
                continue;
            }

            ffmpeg.av_packet_unref(packet);

            if (sendResult < 0)
                throw new InvalidOperationException($"Audio packet submission failed: {getErrorMessage(sendResult)}");

            receiveFrames(decodedFrame, ref output, ref outputPosition);
            return;
        }
    }

    private void receiveFrames(AVFrame* decodedFrame, ref byte[] output, ref int outputPosition)
    {
        while (true)
        {
            var receiveResult = ffmpeg.avcodec_receive_frame(codecContext, decodedFrame);
            if (receiveResult == -BmsSupplementalFFmpegFuncs.EAGAIN || receiveResult == BmsSupplementalFFmpegFuncs.AVERROR_EOF)
                return;

            if (receiveResult < 0)
                throw new InvalidOperationException($"Audio frame receive failed: {getErrorMessage(receiveResult)}");

            var bytesPerSample = decodedFrame->format switch
            {
                (int)AVSampleFormat.AV_SAMPLE_FMT_S16 => 2,
                (int)AVSampleFormat.AV_SAMPLE_FMT_S32 => 4,
                _ => throw new NotSupportedException($"Unsupported decoded FLAC sample format {(AVSampleFormat)decodedFrame->format}."),
            };
            var byteCount = checked(decodedFrame->nb_samples * decodedFrame->channels * bytesPerSample);
            ensureOutputCapacity(ref output, outputPosition, byteCount);
            new ReadOnlySpan<byte>(decodedFrame->data[0], byteCount).CopyTo(output.AsSpan(outputPosition));
            outputPosition += byteCount;
            ffmpeg.av_frame_unref(decodedFrame);
        }
    }

    private int getInitialWaveLength()
    {
        var bytesPerSample = codecContext->sample_fmt switch
        {
            AVSampleFormat.AV_SAMPLE_FMT_S16 => 2,
            AVSampleFormat.AV_SAMPLE_FMT_S32 => 4,
            _ => throw new NotSupportedException($"Unsupported decoded FLAC sample format {codecContext->sample_fmt}."),
        };
        var totalSamples = getTotalSampleCount();

        if (totalSamples == 0)
            return 44;

        var decodedLength = checked(44 + totalSamples * (ulong)codecContext->channels * (ulong)bytesPerSample);

        return decodedLength <= initial_wave_allocation_limit
            ? (int)decodedLength
            : 44 + fallback_wave_capacity;
    }

    private ulong getTotalSampleCount()
    {
        var marker = "fLaC"u8;
        var markerOffset = inputData.AsSpan().IndexOf(marker);

        if (markerOffset < 0 || markerOffset + 42 > inputData.Length)
            return 0;

        var metadataHeaderOffset = markerOffset + marker.Length;
        var metadataType = inputData[metadataHeaderOffset] & 0x7f;
        var metadataLength = (inputData[metadataHeaderOffset + 1] << 16)
                             | (inputData[metadataHeaderOffset + 2] << 8)
                             | inputData[metadataHeaderOffset + 3];

        if (metadataType != 0 || metadataLength < 34)
            return 0;

        return BinaryPrimitives.ReadUInt64BigEndian(inputData.AsSpan(markerOffset + 18, 8)) & 0xfffffffffUL;
    }

    private static void ensureOutputCapacity(ref byte[] output, int outputPosition, int byteCount)
    {
        var requiredLength = checked(outputPosition + byteCount);
        if (requiredLength <= output.Length)
            return;

        var doubledLength = output.Length <= int.MaxValue / 2 ? output.Length * 2 : int.MaxValue;
        var expandedLength = Math.Max(requiredLength, doubledLength);
        Array.Resize(ref output, expandedLength);
    }

    private void writeWaveHeader(byte[] output)
    {
        var sampleFormat = codecContext->sample_fmt;
        var bitsPerSample = sampleFormat switch
        {
            AVSampleFormat.AV_SAMPLE_FMT_S16 => 16,
            AVSampleFormat.AV_SAMPLE_FMT_S32 => 32,
            _ => throw new NotSupportedException($"Unsupported decoded FLAC sample format {sampleFormat}."),
        };
        var channels = codecContext->channels;
        var blockAlign = checked(channels * bitsPerSample / 8);
        var dataLength = checked(output.Length - 44);

        "RIFF"u8.CopyTo(output);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4), checked(dataLength + 36));
        "WAVE"u8.CopyTo(output.AsSpan(8));
        "fmt "u8.CopyTo(output.AsSpan(12));
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(22), checked((short)channels));
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(24), codecContext->sample_rate);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(28), checked(codecContext->sample_rate * blockAlign));
        BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(32), checked((short)blockAlign));
        BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(34), checked((short)bitsPerSample));
        "data"u8.CopyTo(output.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(40), dataLength);
    }

    private void prepareDecoding()
    {
        packet = ffmpeg.av_packet_alloc();
        if (packet == null)
            throw new InvalidOperationException("Could not allocate an audio packet.");

        var contextBuffer = (byte*)ffmpeg.av_malloc(context_buffer_size);
        if (contextBuffer == null)
            throw new InvalidOperationException("Could not allocate an audio IO buffer.");

        ioContext = ffmpeg.avio_alloc_context(contextBuffer, context_buffer_size, 0, (void*)handle.Handle, readPacketCallback, null, seekCallback);
        if (ioContext == null)
        {
            ffmpeg.av_freep(&contextBuffer);
            throw new InvalidOperationException("Could not allocate an audio IO context.");
        }

        formatContext = ffmpeg.avformat_alloc_context();
        if (formatContext == null)
            throw new InvalidOperationException("Could not allocate an audio format context.");

        formatContext->pb = ioContext;
        var formatContextPtr = formatContext;
        var openResult = ffmpeg.avformat_open_input(&formatContextPtr, "pipe:", null, null);
        formatContext = formatContextPtr;
        inputOpened = openResult >= 0;
        if (!inputOpened)
            throw new InvalidOperationException($"Error opening FLAC stream: {getErrorMessage(openResult)}");

        var streamInfoResult = ffmpeg.avformat_find_stream_info(formatContext, null);
        if (streamInfoResult < 0)
            throw new InvalidOperationException($"Error finding FLAC stream info: {getErrorMessage(streamInfoResult)}");

        var streamIndex = ffmpeg.av_find_best_stream(formatContext, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, null, 0);
        if (streamIndex < 0)
            throw new InvalidOperationException($"Could not find a FLAC audio stream: {getErrorMessage(streamIndex)}");

        stream = formatContext->streams[streamIndex];
        var decoder = findDecoder(stream->codecpar->codec_id);
        if (decoder == null)
            throw new InvalidOperationException($"No usable decoder found for codec ID {stream->codecpar->codec_id}.");

        codecContext = ffmpeg.avcodec_alloc_context3(decoder);
        if (codecContext == null)
            throw new InvalidOperationException("Could not allocate a FLAC codec context.");

        var parametersResult = ffmpeg.avcodec_parameters_to_context(codecContext, stream->codecpar);
        if (parametersResult < 0)
            throw new InvalidOperationException($"Could not copy FLAC codec parameters: {getErrorMessage(parametersResult)}");

        var openCodecResult = ffmpeg.avcodec_open2(codecContext, decoder, null);
        if (openCodecResult < 0)
            throw new InvalidOperationException($"Error opening FLAC codec: {getErrorMessage(openCodecResult)}");
    }

    private AVCodec* findDecoder(AVCodecID codecId)
    {
        void* iterator = null;

        while (true)
        {
            var codec = ffmpeg.av_codec_iterate(&iterator);
            if (codec == null)
                return null;
            if (codec->id == codecId && ffmpeg.av_codec_is_decoder(codec) != 0)
                return codec;
        }
    }

    private string getErrorMessage(int errorCode)
    {
        const ulong buffer_size = 256;
        var buffer = new byte[buffer_size];

        fixed (byte* bufferPtr = buffer)
        {
            var result = ffmpeg.av_strerror(errorCode, bufferPtr, buffer_size);
            if (result < 0)
                return $"{errorCode} (av_strerror failed with code {result})";
        }

        var messageLength = Array.IndexOf(buffer, (byte)0);
        if (messageLength < 0)
            messageLength = buffer.Length;
        return $"{Encoding.ASCII.GetString(buffer, 0, messageLength)} ({errorCode})";
    }

    [MonoPInvokeCallback(typeof(avio_alloc_context_read_packet))]
    private static int readPacket(void* opaque, byte* bufferPtr, int bufferSize)
    {
        var objectHandle = new ObjectHandle<BmsFlacDecoder>((IntPtr)opaque);
        if (!objectHandle.GetTarget(out var decoder))
            return 0;

        var bytesRead = decoder.dataStream.Read(new Span<byte>(bufferPtr, bufferSize));
        return bytesRead != 0 ? bytesRead : BmsSupplementalFFmpegFuncs.AVERROR_EOF;
    }

    [MonoPInvokeCallback(typeof(avio_alloc_context_seek))]
    private static long streamSeekCallback(void* opaque, long offset, int whence)
    {
        var objectHandle = new ObjectHandle<BmsFlacDecoder>((IntPtr)opaque);
        if (!objectHandle.GetTarget(out var decoder))
            return -1;

        return whence switch
        {
            0 => decoder.dataStream.Seek(offset, SeekOrigin.Begin),
            1 => decoder.dataStream.Seek(offset, SeekOrigin.Current),
            2 => decoder.dataStream.Seek(offset, SeekOrigin.End),
            BmsSupplementalFFmpegFuncs.AVSEEK_SIZE => decoder.dataStream.Length,
            _ => -1,
        };
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        if (codecContext != null)
        {
            var codecContextPtr = codecContext;
            ffmpeg.avcodec_free_context(&codecContextPtr);
            codecContext = null;
        }

        if (packet != null)
        {
            var packetPtr = packet;
            ffmpeg.av_packet_free(&packetPtr);
            packet = null;
        }

        if (formatContext != null && inputOpened)
        {
            var formatContextPtr = formatContext;
            ffmpeg.avformat_close_input(&formatContextPtr);
            formatContext = null;
        }
        else if (formatContext != null)
        {
            ffmpeg.avformat_free_context(formatContext);
            formatContext = null;
        }

        if (ioContext != null)
        {
            ffmpeg.av_freep(&ioContext->buffer);
            var ioContextPtr = ioContext;
            ffmpeg.avio_context_free(&ioContextPtr);
            ioContext = null;
        }

        handle.Dispose();
        dataStream.Dispose();
    }
}
