using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FFmpeg.AutoGen;

// ReSharper disable InconsistentNaming

namespace osu.Game.Rulesets.BmsRuleset.Media.FFmpeg;

internal sealed unsafe class BmsSupplementalFFmpegFuncs
{
    public delegate int AvStrErrorDelegate(int errnum, byte* buffer, ulong bufSize);

    public delegate void* AvMallocDelegate(ulong size);

    public delegate void AvFreepDelegate(void* ptr);

    public delegate AVPacket* AvPacketAllocDelegate();

    public delegate void AvPacketUnrefDelegate(AVPacket* pkt);

    public delegate void AvPacketFreeDelegate(AVPacket** pkt);

    public delegate AVFrame* AvFrameAllocDelegate();

    public delegate void AvFrameFreeDelegate(AVFrame** frame);

    public delegate void AvFrameUnrefDelegate(AVFrame* frame);

    public delegate int AvFrameGetBufferDelegate(AVFrame* frame, int align);

    public delegate int AvReadFrameDelegate(AVFormatContext* s, AVPacket* pkt);

    public delegate AVCodec* AvCodecIterateDelegate(void** opaque);

    public delegate int AvCodecIsDecoderDelegate(AVCodec* codec);

    public delegate AVCodecContext* AvcodecAllocContext3Delegate(AVCodec* codec);

    public delegate void AvcodecFreeContextDelegate(AVCodecContext** avctx);

    public delegate int AvcodecParametersToContextDelegate(AVCodecContext* codec, AVCodecParameters* par);

    public delegate int AvcodecOpen2Delegate(AVCodecContext* avctx, AVCodec* codec, AVDictionary** options);

    public delegate int AvcodecReceiveFrameDelegate(AVCodecContext* avctx, AVFrame* frame);

    public delegate int AvcodecSendPacketDelegate(AVCodecContext* avctx, AVPacket* avpkt);

    public delegate AVFormatContext* AvformatAllocContextDelegate();

    public delegate void AvformatCloseInputDelegate(AVFormatContext** s);

    public delegate void AvformatFreeContextDelegate(AVFormatContext* s);

    public delegate int AvformatFindStreamInfoDelegate(AVFormatContext* ic, AVDictionary** options);

    public delegate int AvformatOpenInputDelegate(AVFormatContext** ps, [MarshalAs(UnmanagedType.LPUTF8Str)] string url, AVInputFormat* fmt, AVDictionary** options);

    public delegate int AvFindBestStreamDelegate(AVFormatContext* ic, AVMediaType type, int wanted_stream_nb, int related_stream, AVCodec** decoder_ret, int flags);

    public delegate AVIOContext* AvioAllocContextDelegate(byte* buffer, int buffer_size, int write_flag, void* opaque, avio_alloc_context_read_packet_func read_packet, avio_alloc_context_write_packet_func write_packet, avio_alloc_context_seek_func seek);

    public delegate void AvioContextFreeDelegate(AVIOContext** s);

    public delegate SwsContext* SwsGetCachedContextDelegate(SwsContext* context, int srcW, int srcH, AVPixelFormat srcFormat, int dstW, int dstH, AVPixelFormat dstFormat, int flags, SwsFilter* srcFilter, SwsFilter* dstFilter, double* param);

    public delegate int SwsScaleDelegate(SwsContext* c, byte*[] srcSlice, int[] srcStride, int srcSliceY, int srcSliceH, byte*[] dst, int[] dstStride);

    public delegate void SwsFreeContextDelegate(SwsContext* swsContext);

    public const int AVSEEK_SIZE = 0x10000;
    public const int AVFMT_FLAG_GENPTS = 0x0001;
    public const int AV_TIME_BASE = 1000000;
    public const int AVERROR_EOF = -('E' + ('O' << 8) + ('F' << 16) + (' ' << 24));
    public const long AV_NOPTS_VALUE = unchecked((long)0x8000000000000000);
    public const int EAGAIN = 11;

    // The native backend is one shared object per OS/arch, each embedded as a manifest resource
    // (the LogicalName in the .csproj must match). The loader picks the matching pair at runtime
    // and declines — routing the file through the rest of the provider chain (framework, then
    // missing-video) — on platforms without an embedded artifact, so a build that omits a
    // platform simply doesn't support the supplemental backend there rather than crashing.
    private static readonly (string resource, string artifact) current_platform =
        OperatingSystem.IsWindows() ? ("bms-ffmpeg.win-x64.dll", "bms-ffmpeg.dll")
        : OperatingSystem.IsLinux() ? ("bms-ffmpeg.linux-x64.so", "bms-ffmpeg.so")
        : OperatingSystem.IsMacOS() ? ("bms-ffmpeg.osx.dylib", "bms-ffmpeg.dylib")
        : ("", "");

    // Resolved once for the process lifetime: the embedded bytes never change, so re-reading the
    // 4.6 MB resource + re-NativeLibrary.Load-ing on every decoder/probe init is pure waste and
    // bumps the loader refcount without ever freeing it. The first call extracts and loads the
    // backend; later callers get the same delegate bag. The delegates are immutable wrappers over
    // native function pointers, so sharing one instance across decoders is safe.
    private static readonly Lazy<(BmsSupplementalFFmpegFuncs? funcs, string? error)> resolved =
        new(loadOnce);

    public AvStrErrorDelegate av_strerror { get; }

    public AvMallocDelegate av_malloc { get; }

    public AvFreepDelegate av_freep { get; }

    public AvPacketAllocDelegate av_packet_alloc { get; }

    public AvPacketUnrefDelegate av_packet_unref { get; }

    public AvPacketFreeDelegate av_packet_free { get; }

    public AvFrameAllocDelegate av_frame_alloc { get; }

    public AvFrameFreeDelegate av_frame_free { get; }

    public AvFrameUnrefDelegate av_frame_unref { get; }

    public AvFrameGetBufferDelegate av_frame_get_buffer { get; }

    public AvReadFrameDelegate av_read_frame { get; }

    public AvCodecIterateDelegate av_codec_iterate { get; }

    public AvCodecIsDecoderDelegate av_codec_is_decoder { get; }

    public AvcodecAllocContext3Delegate avcodec_alloc_context3 { get; }

    public AvcodecFreeContextDelegate avcodec_free_context { get; }

    public AvcodecParametersToContextDelegate avcodec_parameters_to_context { get; }

    public AvcodecOpen2Delegate avcodec_open2 { get; }

    public AvcodecReceiveFrameDelegate avcodec_receive_frame { get; }

    public AvcodecSendPacketDelegate avcodec_send_packet { get; }

    public AvformatAllocContextDelegate avformat_alloc_context { get; }

    public AvformatCloseInputDelegate avformat_close_input { get; }

    public AvformatFreeContextDelegate avformat_free_context { get; }

    public AvformatFindStreamInfoDelegate avformat_find_stream_info { get; }

    public AvformatOpenInputDelegate avformat_open_input { get; }

    public AvFindBestStreamDelegate av_find_best_stream { get; }

    public AvioAllocContextDelegate avio_alloc_context { get; }

    public AvioContextFreeDelegate avio_context_free { get; }

    public SwsGetCachedContextDelegate sws_getCachedContext { get; }

    public SwsScaleDelegate sws_scale { get; }

    public SwsFreeContextDelegate sws_freeContext { get; }

    private BmsSupplementalFFmpegFuncs(IntPtr ffmpeg)
    {
        av_strerror = getExport<AvStrErrorDelegate>(ffmpeg, "av_strerror");
        av_malloc = getExport<AvMallocDelegate>(ffmpeg, "av_malloc");
        av_freep = getExport<AvFreepDelegate>(ffmpeg, "av_freep");
        av_packet_alloc = getExport<AvPacketAllocDelegate>(ffmpeg, "av_packet_alloc");
        av_packet_unref = getExport<AvPacketUnrefDelegate>(ffmpeg, "av_packet_unref");
        av_packet_free = getExport<AvPacketFreeDelegate>(ffmpeg, "av_packet_free");
        av_frame_alloc = getExport<AvFrameAllocDelegate>(ffmpeg, "av_frame_alloc");
        av_frame_free = getExport<AvFrameFreeDelegate>(ffmpeg, "av_frame_free");
        av_frame_unref = getExport<AvFrameUnrefDelegate>(ffmpeg, "av_frame_unref");
        av_frame_get_buffer = getExport<AvFrameGetBufferDelegate>(ffmpeg, "av_frame_get_buffer");
        av_read_frame = getExport<AvReadFrameDelegate>(ffmpeg, "av_read_frame");
        av_codec_iterate = getExport<AvCodecIterateDelegate>(ffmpeg, "av_codec_iterate");
        av_codec_is_decoder = getExport<AvCodecIsDecoderDelegate>(ffmpeg, "av_codec_is_decoder");
        avcodec_alloc_context3 = getExport<AvcodecAllocContext3Delegate>(ffmpeg, "avcodec_alloc_context3");
        avcodec_free_context = getExport<AvcodecFreeContextDelegate>(ffmpeg, "avcodec_free_context");
        avcodec_parameters_to_context = getExport<AvcodecParametersToContextDelegate>(ffmpeg, "avcodec_parameters_to_context");
        avcodec_open2 = getExport<AvcodecOpen2Delegate>(ffmpeg, "avcodec_open2");
        avcodec_receive_frame = getExport<AvcodecReceiveFrameDelegate>(ffmpeg, "avcodec_receive_frame");
        avcodec_send_packet = getExport<AvcodecSendPacketDelegate>(ffmpeg, "avcodec_send_packet");
        avformat_alloc_context = getExport<AvformatAllocContextDelegate>(ffmpeg, "avformat_alloc_context");
        avformat_close_input = getExport<AvformatCloseInputDelegate>(ffmpeg, "avformat_close_input");
        avformat_free_context = getExport<AvformatFreeContextDelegate>(ffmpeg, "avformat_free_context");
        avformat_find_stream_info = getExport<AvformatFindStreamInfoDelegate>(ffmpeg, "avformat_find_stream_info");
        avformat_open_input = getExport<AvformatOpenInputDelegate>(ffmpeg, "avformat_open_input");
        av_find_best_stream = getExport<AvFindBestStreamDelegate>(ffmpeg, "av_find_best_stream");
        avio_alloc_context = getExport<AvioAllocContextDelegate>(ffmpeg, "avio_alloc_context");
        avio_context_free = getExport<AvioContextFreeDelegate>(ffmpeg, "avio_context_free");
        sws_getCachedContext = getExport<SwsGetCachedContextDelegate>(ffmpeg, "sws_getCachedContext");
        sws_scale = getExport<SwsScaleDelegate>(ffmpeg, "sws_scale");
        sws_freeContext = getExport<SwsFreeContextDelegate>(ffmpeg, "sws_freeContext");
    }

    // Cheap probe used by the provider to route videos without doing a native load on every lookup.
    public static bool IsAvailable
    {
        get
        {
            if (current_platform.artifact.Length == 0)
                return false;

            var assembly = Assembly.GetExecutingAssembly();
            return assembly.GetManifestResourceInfo(current_platform.resource) != null
                   || assembly.GetManifestResourceInfo($"{current_platform.resource}.br") != null;
        }
    }

    public static bool TryCreate(out BmsSupplementalFFmpegFuncs? funcs, out string? error)
    {
        (funcs, error) = resolved.Value;
        return funcs != null;
    }

    private static (BmsSupplementalFFmpegFuncs? funcs, string? error) loadOnce()
    {
        if (current_platform.artifact.Length == 0)
            return (null, "Supplemental FFmpeg native backend is not packaged for this OS/arch.");

        var nativeBytes = readEmbeddedBackend(current_platform.resource, out var readError);
        if (nativeBytes == null)
            return (null, readError);

        try
        {
            var tempPath = extractToTemp(nativeBytes, current_platform.artifact);
            var ffmpeg = NativeLibrary.Load(tempPath);
            return (new BmsSupplementalFFmpegFuncs(ffmpeg), null);
        }
        catch (Exception ex)
        {
            return (null, $"Failed to load supplemental FFmpeg native backend '{current_platform.artifact}': {ex.Message}");
        }
    }

    private static byte[]? readEmbeddedBackend(string name, out string? error)
    {
        error = null;

        var assembly = Assembly.GetExecutingAssembly();
        using var rawStream = assembly.GetManifestResourceStream(name);

        if (rawStream != null)
            return readFully(rawStream, current_platform.artifact, out error);

        using var compressedStream = assembly.GetManifestResourceStream($"{name}.br");

        if (compressedStream == null)
        {
            error = $"Supplemental FFmpeg native backend '{current_platform.artifact}' is not embedded in this ruleset build.";
            return null;
        }

        try
        {
            using var brotli = new BrotliStream(compressedStream, CompressionMode.Decompress);
            using var decompressed = new MemoryStream();
            brotli.CopyTo(decompressed);

            return decompressed.ToArray();
        }
        catch (InvalidDataException ex)
        {
            error = $"Supplemental FFmpeg native backend '{current_platform.artifact}' compressed resource could not be decompressed: {ex.Message}";
            return null;
        }
    }

    private static byte[]? readFully(Stream stream, string artifactName, out string? error)
    {
        error = null;
        var bytes = new byte[stream.Length];
        var totalRead = 0;

        while (totalRead < bytes.Length)
        {
            var read = stream.Read(bytes, totalRead, bytes.Length - totalRead);
            if (read <= 0)
                break;

            totalRead += read;
        }

        if (totalRead != bytes.Length)
        {
            error = $"Supplemental FFmpeg native backend '{artifactName}' resource was truncated.";
            return null;
        }

        return bytes;
    }

    // Drop the embedded DLL into a per-content-hash temp path so repeated runs reuse it without
    // re-extracting, and a new ruleset build (different bytes) gets its own file. The atomic Move
    // keeps concurrent processes from observing a half-written file.
    private static string extractToTemp(byte[] nativeBytes, string artifactName)
    {
        var hash = Convert.ToHexString(SHA256.HashData(nativeBytes)).ToLowerInvariant()[..16];
        var dir = Path.Combine(Path.GetTempPath(), "bms-ffmpeg");
        // Keep the platform's native extension so the loader and OS recognise the file
        // (dlopen is liberal, but Windows LoadLibrary expects .dll).
        var final = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(artifactName)}.{hash}{Path.GetExtension(artifactName)}");

        if (File.Exists(final))
            return final;

        Directory.CreateDirectory(dir);

        var staging = $"{final}.{Environment.ProcessId}.tmp";
        File.WriteAllBytes(staging, nativeBytes);

        try
        {
            File.Move(staging, final, overwrite: true);
        }
        catch (IOException)
        {
            // Another process won the race with identical bytes; reuse its file.
            File.Delete(staging);
        }

        return final;
    }

    private static T getExport<T>(IntPtr handle, string symbol)
        where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(handle, symbol));
}
