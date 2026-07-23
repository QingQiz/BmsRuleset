using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using FFmpeg.AutoGen;
using osu.Framework.Allocation;
using osu.Framework.Platform;
using SixLabors.ImageSharp.PixelFormats;
using osu.Game.Rulesets.BmsRuleset.Media.FFmpeg;

namespace osu.Game.Rulesets.BmsRuleset.Media.Video.Supplemental;

internal sealed unsafe class BmsSupplementalVideoDecoder : IDisposable
{
    private readonly BmsSupplementalFFmpegFuncs ffmpeg;
    private readonly ArrayPool<Rgba32> framePool;
    private readonly MemoryStream dataStream;
    private readonly ObjectHandle<BmsSupplementalVideoDecoder> handle;

    private readonly avio_alloc_context_read_packet readPacketCallback;
    private readonly avio_alloc_context_seek seekCallback;

    private AVFormatContext* formatContext;
    private AVIOContext* ioContext;
    private AVPacket* packet;
    private AVStream* stream;
    private AVCodecContext* codecContext;
    private SwsContext* swsContext;
    private bool inputOpened;
    private bool disposed;
    private bool reachedInputEof;
    private bool sentDrainPacket;
    private bool decoderFullyDrained;
    private double timeBaseInSeconds;
    private long streamStartTime;

    private BmsSupplementalVideoDecoder(BmsSupplementalFFmpegFuncs ffmpeg, byte[] data, ArrayPool<Rgba32>? framePool = null)
    {
        this.ffmpeg = ffmpeg;
        this.framePool = framePool ?? ArrayPool<Rgba32>.Shared;
        dataStream = new MemoryStream(data, writable: false);
        handle = new ObjectHandle<BmsSupplementalVideoDecoder>(this, GCHandleType.Normal);
        readPacketCallback = readPacket;
        seekCallback = streamSeekCallbacks;
    }

    public static bool TryCreate(byte[] data, out BmsSupplementalVideoDecoder? decoder, out string? error)
    {
        decoder = null;
        error = null;

        if (!BmsSupplementalFFmpegFuncs.TryCreate(out var ffmpeg, out error))
            return false;

        try
        {
            decoder = new BmsSupplementalVideoDecoder(ffmpeg!, data);
            decoder.prepareDecoding();
            return true;
        }
        catch (Exception ex)
        {
            error = $"Supplemental FFmpeg decoder initialisation failed: {ex.Message}";
            decoder?.Dispose();
            decoder = null;
            return false;
        }
    }

    public bool TryDecodeNextFrame(out BmsSupplementalVideoFrame? frame, out string? error)
    {
        frame = null;
        error = null;

        ObjectDisposedException.ThrowIf(disposed, this);

        if (decoderFullyDrained)
            return false;

        var decodedFrame = ffmpeg.av_frame_alloc();
        var rgbaFrame = ffmpeg.av_frame_alloc();

        if (decodedFrame == null || rgbaFrame == null)
        {
            error = "Supplemental FFmpeg decoder could not allocate packet/frame buffers.";
            releaseFrames(decodedFrame, rgbaFrame);
            return false;
        }

        try
        {
            while (true)
            {
                if (packet->buf != null)
                {
                    if (trySendPendingPacket(decodedFrame, rgbaFrame, out frame, out error))
                        return true;

                    if (error != null)
                        return false;

                    if (packet->buf != null)
                        return false;

                    continue;
                }

                if (reachedInputEof)
                    return tryDrainDecoder(decodedFrame, rgbaFrame, out frame, out error);

                var readFrameResult = ffmpeg.av_read_frame(formatContext, packet);
                if (readFrameResult < 0)
                {
                    if (readFrameResult == BmsSupplementalFFmpegFuncs.AVERROR_EOF)
                    {
                        reachedInputEof = true;
                        continue;
                    }

                    error = $"Supplemental FFmpeg frame read failed: {getErrorMessage(readFrameResult)}";
                    return false;
                }

                try
                {
                    if (packet->stream_index != stream->index)
                    {
                        ffmpeg.av_packet_unref(packet);
                        continue;
                    }

                    var sendPacketResult = ffmpeg.avcodec_send_packet(codecContext, packet);
                    if (sendPacketResult == -BmsSupplementalFFmpegFuncs.EAGAIN)
                    {
                        // FFmpeg can ask us to back off before accepting the packet, so the packet must stay intact for a retry.
                        if (tryReceiveDecodedFrame(decodedFrame, rgbaFrame, out frame, out error))
                            return true;

                        if (error != null)
                        {
                        }

                        return false;
                    }

                    ffmpeg.av_packet_unref(packet);

                    if (sendPacketResult < 0)
                    {
                        error = $"Supplemental FFmpeg packet submission failed: {getErrorMessage(sendPacketResult)}";
                        return false;
                    }

                    if (tryReceiveDecodedFrame(decodedFrame, rgbaFrame, out frame, out error))
                        return true;

                    if (error != null)
                        return false;
                }
                finally
                {
                    ffmpeg.av_frame_unref(decodedFrame);
                }
            }
        }
        catch (Exception ex)
        {
            error = $"Supplemental FFmpeg frame decode failed: {ex.Message}";
            return false;
        }
        finally
        {
            releaseFrames(decodedFrame, rgbaFrame);
        }
    }

    private bool trySendPendingPacket(AVFrame* decodedFrame, AVFrame* rgbaFrame, out BmsSupplementalVideoFrame? frame, out string? error)
    {
        frame = null;
        error = null;

        var sendPacketResult = ffmpeg.avcodec_send_packet(codecContext, packet);
        if (sendPacketResult == -BmsSupplementalFFmpegFuncs.EAGAIN)
        {
            if (tryReceiveDecodedFrame(decodedFrame, rgbaFrame, out frame, out error))
                return true;

            return false;
        }

        ffmpeg.av_packet_unref(packet);

        if (sendPacketResult < 0)
        {
            error = $"Supplemental FFmpeg packet submission failed: {getErrorMessage(sendPacketResult)}";
            return false;
        }

        if (tryReceiveDecodedFrame(decodedFrame, rgbaFrame, out frame, out error))
            return true;

        return false;
    }

    private bool tryDrainDecoder(AVFrame* decodedFrame, AVFrame* rgbaFrame, out BmsSupplementalVideoFrame? frame, out string? error)
    {
        frame = null;
        error = null;

        if (!sentDrainPacket)
        {
            var sendPacketResult = ffmpeg.avcodec_send_packet(codecContext, null);
            if (sendPacketResult < 0
                && sendPacketResult != -BmsSupplementalFFmpegFuncs.EAGAIN
                && sendPacketResult != BmsSupplementalFFmpegFuncs.AVERROR_EOF)
            {
                error = $"Supplemental FFmpeg packet submission failed: {getErrorMessage(sendPacketResult)}";
                return false;
            }

            sentDrainPacket = true;
        }

        if (tryReceiveDecodedFrame(decodedFrame, rgbaFrame, out frame, out error))
            return true;

        if (error != null)
            return false;

        decoderFullyDrained = true;
        return false;
    }

    private bool tryReceiveDecodedFrame(AVFrame* decodedFrame, AVFrame* rgbaFrame, out BmsSupplementalVideoFrame? frame, out string? error)
    {
        frame = null;
        error = null;

        while (true)
        {
            var receiveFrameResult = ffmpeg.avcodec_receive_frame(codecContext, decodedFrame);
            if (receiveFrameResult == -BmsSupplementalFFmpegFuncs.EAGAIN || receiveFrameResult == BmsSupplementalFFmpegFuncs.AVERROR_EOF)
                return false;

            if (receiveFrameResult < 0)
            {
                error = $"Supplemental FFmpeg frame receive failed: {getErrorMessage(receiveFrameResult)}";
                return false;
            }

            frame = convertFrame(decodedFrame, rgbaFrame);
            ffmpeg.av_frame_unref(decodedFrame);
            return true;
        }
    }

    private BmsSupplementalVideoFrame convertFrame(AVFrame* decodedFrame, AVFrame* rgbaFrame)
    {
        var width = decodedFrame->width;
        var height = decodedFrame->height;

        swsContext = ffmpeg.sws_getCachedContext(
            swsContext,
            width, height, (AVPixelFormat)decodedFrame->format,
            width, height, AVPixelFormat.AV_PIX_FMT_RGBA,
            1, null, null, null);

        if (swsContext == null)
            throw new InvalidOperationException("sws_getCachedContext returned null.");

        ffmpeg.av_frame_unref(rgbaFrame);
        rgbaFrame->format = (int)AVPixelFormat.AV_PIX_FMT_RGBA;
        rgbaFrame->width = width;
        rgbaFrame->height = height;

        var getBufferResult = ffmpeg.av_frame_get_buffer(rgbaFrame, 0);
        if (getBufferResult < 0)
            throw new InvalidOperationException($"Failed to allocate RGBA frame buffer: {getErrorMessage(getBufferResult)}");

        var scaleResult = ffmpeg.sws_scale(
            swsContext,
            decodedFrame->data, decodedFrame->linesize, 0, height,
            rgbaFrame->data, rgbaFrame->linesize);

        if (scaleResult < 0)
            throw new InvalidOperationException($"Failed to scale frame to RGBA: {getErrorMessage(scaleResult)}");

        var pixels = framePool.Rent(width * height);
        var pixelSpan = pixels.AsSpan(0, width * height);
        var pixelBytes = MemoryMarshal.AsBytes(pixelSpan);
        var sourceBase = rgbaFrame->data[0];
        var sourceStride = rgbaFrame->linesize[0];

        for (var row = 0; row < height; row++)
        {
            var sourceRow = new ReadOnlySpan<byte>(sourceBase + row * sourceStride, width * 4);
            sourceRow.CopyTo(pixelBytes.Slice(row * width * 4, width * 4));
        }

        var frameTimestamp = decodedFrame->best_effort_timestamp != BmsSupplementalFFmpegFuncs.AV_NOPTS_VALUE
            ? decodedFrame->best_effort_timestamp
            : decodedFrame->pts;

        if (frameTimestamp == BmsSupplementalFFmpegFuncs.AV_NOPTS_VALUE)
            frameTimestamp = streamStartTime;

        var frameTime = (frameTimestamp - streamStartTime) * timeBaseInSeconds;
        return new BmsSupplementalVideoFrame(framePool, pixels, width, height, frameTime);
    }

    [MonoPInvokeCallback(typeof(avio_alloc_context_read_packet))]
    private static int readPacket(void* opaque, byte* bufferPtr, int bufferSize)
    {
        var handle = new ObjectHandle<BmsSupplementalVideoDecoder>((IntPtr)opaque);
        if (!handle.GetTarget(out var decoder))
            return 0;

        var span = new Span<byte>(bufferPtr, bufferSize);
        var bytesRead = decoder.dataStream.Read(span);
        return bytesRead != 0 ? bytesRead : BmsSupplementalFFmpegFuncs.AVERROR_EOF;
    }

    [MonoPInvokeCallback(typeof(avio_alloc_context_seek))]
    private static long streamSeekCallbacks(void* opaque, long offset, int whence)
    {
        var handle = new ObjectHandle<BmsSupplementalVideoDecoder>((IntPtr)opaque);
        if (!handle.GetTarget(out var decoder))
            return -1;

        switch (whence)
        {
            case 0:
                decoder.dataStream.Seek(offset, SeekOrigin.Begin);
                break;

            case 1:
                decoder.dataStream.Seek(offset, SeekOrigin.Current);
                break;

            case 2:
                decoder.dataStream.Seek(offset, SeekOrigin.End);
                break;

            case BmsSupplementalFFmpegFuncs.AVSEEK_SIZE:
                return decoder.dataStream.Length;

            default:
                return -1;
        }

        return decoder.dataStream.Position;
    }

    private void prepareDecoding()
    {
        const int context_buffer_size = 4096;

        packet = ffmpeg.av_packet_alloc();
        if (packet == null)
            throw new InvalidOperationException("Could not allocate FFmpeg packet.");

        var contextBuffer = (byte*)ffmpeg.av_malloc(context_buffer_size);
        if (contextBuffer == null)
            throw new InvalidOperationException("Could not allocate FFmpeg IO buffer.");

        ioContext = ffmpeg.avio_alloc_context(contextBuffer, context_buffer_size, 0, (void*)handle.Handle, readPacketCallback, null, seekCallback);
        if (ioContext == null)
        {
            ffmpeg.av_freep(&contextBuffer);
            throw new InvalidOperationException("Could not allocate FFmpeg AVIO context.");
        }

        formatContext = ffmpeg.avformat_alloc_context();
        if (formatContext == null)
            throw new InvalidOperationException("Could not allocate FFmpeg format context.");

        formatContext->pb = ioContext;
        formatContext->flags |= BmsSupplementalFFmpegFuncs.AVFMT_FLAG_GENPTS;

        var formatContextPtr = formatContext;
        var openInputResult = ffmpeg.avformat_open_input(&formatContextPtr, "pipe:", null, null);
        formatContext = formatContextPtr;
        inputOpened = openInputResult >= 0;

        if (!inputOpened)
            throw new InvalidOperationException($"Error opening file or stream: {getErrorMessage(openInputResult)}");

        var findStreamInfoResult = ffmpeg.avformat_find_stream_info(formatContext, null);
        if (findStreamInfoResult < 0)
            throw new InvalidOperationException($"Error finding stream info: {getErrorMessage(findStreamInfoResult)}");

        var streamIndex = ffmpeg.av_find_best_stream(formatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
        if (streamIndex < 0)
            throw new InvalidOperationException($"Couldn't find video stream: {getErrorMessage(streamIndex)}");

        stream = formatContext->streams[streamIndex];
        timeBaseInSeconds = stream->time_base.den == 0 ? 0 : stream->time_base.num / (double)stream->time_base.den;
        streamStartTime = stream->start_time == BmsSupplementalFFmpegFuncs.AV_NOPTS_VALUE ? 0 : stream->start_time;

        var decoder = findDecoder(stream->codecpar->codec_id);
        if (decoder == null)
            throw new InvalidOperationException($"No usable decoder found for codec ID {stream->codecpar->codec_id}.");

        codecContext = ffmpeg.avcodec_alloc_context3(decoder);
        if (codecContext == null)
            throw new InvalidOperationException("Could not allocate codec context.");

        var paramCopyResult = ffmpeg.avcodec_parameters_to_context(codecContext, stream->codecpar);
        if (paramCopyResult < 0)
            throw new InvalidOperationException($"Couldn't copy codec parameters: {getErrorMessage(paramCopyResult)}");

        var openCodecResult = ffmpeg.avcodec_open2(codecContext, decoder, null);
        if (openCodecResult < 0)
            throw new InvalidOperationException($"Error opening codec: {getErrorMessage(openCodecResult)}");
    }

    private AVCodec* findDecoder(AVCodecID codecId)
    {
        void* iterator = null;

        while (true)
        {
            var codec = ffmpeg.av_codec_iterate(&iterator);
            if (codec == null)
                return null;

            if (codec->id != codecId)
                continue;

            if (ffmpeg.av_codec_is_decoder(codec) == 0)
                continue;

            return codec;
        }
    }

    private string getErrorMessage(int errorCode)
    {
        const ulong buffer_size = 256;
        var buffer = new byte[buffer_size];

        fixed (byte* bufferPtr = buffer)
        {
            var strErrorCode = ffmpeg.av_strerror(errorCode, bufferPtr, buffer_size);
            if (strErrorCode < 0)
                return $"{errorCode} (av_strerror failed with code {strErrorCode})";
        }

        var messageLength = Array.IndexOf(buffer, (byte)0);
        if (messageLength < 0)
            messageLength = buffer.Length;

        return $"{Encoding.ASCII.GetString(buffer, 0, messageLength)} ({errorCode})";
    }

    private void releaseFrames(AVFrame* decodedFrame, AVFrame* rgbaFrame)
    {
        if (decodedFrame != null)
        {
            var framePtr = decodedFrame;
            ffmpeg.av_frame_free(&framePtr);
        }

        if (rgbaFrame != null)
        {
            var framePtr = rgbaFrame;
            ffmpeg.av_frame_free(&framePtr);
        }
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

        if (swsContext != null)
        {
            ffmpeg.sws_freeContext(swsContext);
            swsContext = null;
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
