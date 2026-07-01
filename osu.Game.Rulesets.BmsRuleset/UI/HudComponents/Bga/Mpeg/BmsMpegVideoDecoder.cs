using System;
using System.Buffers;
using System.Runtime.InteropServices;
using PLMpegSharp;
using PLMpegSharp.Container;
using SixLabors.ImageSharp.PixelFormats;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents.Bga.Mpeg;

internal sealed class BmsMpegVideoDecoder : IDisposable
{
    private readonly ArrayPool<Rgba32> framePool;
    private readonly Player player;

    private BmsMpegVideoDecoder(Player player, ArrayPool<Rgba32>? framePool = null)
    {
        this.player = player;
        this.framePool = framePool ?? ArrayPool<Rgba32>.Shared;
    }

    public static bool TryCreate(byte[] data, out BmsMpegVideoDecoder? decoder, out string? error)
    {
        decoder = null;
        error = null;

        try
        {
            var player = new Player(data);

            if (player.Width <= 0 || player.Height <= 0 || player.Framerate <= 0)
            {
                error = "MPEG-PS/MPEG-1 video headers were not found.";
                return false;
            }

            decoder = new BmsMpegVideoDecoder(player);
            return true;
        }
        catch (Exception ex)
        {
            error = $"MPEG decoder initialisation failed: {ex.Message}";
            return false;
        }
    }

    public bool TryDecodeNextFrame(out BmsMpegVideoFrame? frame, out string? error)
    {
        frame = null;
        error = null;

        try
        {
            Frame? decoded = player.DecodeVideo();
            if (decoded == null)
                return false;

            var pixels = framePool.Rent(decoded.Width * decoded.Height);
            var span = pixels.AsSpan(0, decoded.Width * decoded.Height);
            var rgbaBytes = ArrayPool<byte>.Shared.Rent(decoded.Width * decoded.Height * 4);
            var transferOwnership = false;

            try
            {
                var rgbaSpan = rgbaBytes.AsSpan(0, decoded.Width * decoded.Height * 4);
                for (var i = 3; i < rgbaSpan.Length; i += 4)
                    rgbaSpan[i] = 255;

                decoded.ToRGBA(rgbaBytes, decoded.Width * 4);
                MemoryMarshal.Cast<byte, Rgba32>(rgbaSpan).CopyTo(span);
                frame = new BmsMpegVideoFrame(framePool, pixels, decoded.Width, decoded.Height, decoded.Time);
                transferOwnership = true;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rgbaBytes);
                if (!transferOwnership)
                    framePool.Return(pixels);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"MPEG frame decode failed: {ex.Message}";
            return false;
        }
    }

    public void Dispose()
    {
    }
}

internal sealed class BmsMpegVideoFrame(ArrayPool<Rgba32> pool, Rgba32[] pixels, int width, int height, double time)
{
    private Rgba32[]? pixels = pixels;

    public double Time { get; } = time;

    public int Width { get; } = width;

    public int Height { get; } = height;

    public int PixelCount => Width * Height;

    public BmsPooledTextureUpload CreateUpload()
    {
        var taken = pixels ?? throw new ObjectDisposedException(nameof(BmsMpegVideoFrame));
        pixels = null;
        return new BmsPooledTextureUpload(pool, taken, Width, Height);
    }

    public void Dispose()
    {
        if (pixels == null)
            return;

        pool.Return(pixels);
        pixels = null;
    }
}
