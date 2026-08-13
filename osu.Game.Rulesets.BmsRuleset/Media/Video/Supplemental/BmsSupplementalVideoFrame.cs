using System;
using System.Buffers;
using SixLabors.ImageSharp.PixelFormats;

namespace osu.Game.Rulesets.BmsRuleset.Media.Video.Supplemental;

internal sealed class BmsSupplementalVideoFrame(ArrayPool<Rgba32> pool, Rgba32[] pixels, int width, int height, double time) : IDisposable
{
    private Rgba32[]? pixels = pixels;

    public double Time { get; } = time;

    public int Width { get; } = width;

    public int Height { get; } = height;

    public BmsPooledTextureUpload CreateUpload()
    {
        var taken = pixels ?? throw new ObjectDisposedException(nameof(BmsSupplementalVideoFrame));
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
