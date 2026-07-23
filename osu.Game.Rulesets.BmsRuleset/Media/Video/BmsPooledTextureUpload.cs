using System;
using System.Buffers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Textures;
using osuTK.Graphics.ES30;
using SixLabors.ImageSharp.PixelFormats;

namespace osu.Game.Rulesets.BmsRuleset.Media.Video;

internal sealed class BmsPooledTextureUpload(ArrayPool<Rgba32> pool, Rgba32[] pixels, int width, int height, int level = 0)
    : ITextureUpload
{
    private readonly int pixelCount = width * height;
    private bool disposed;

    public ReadOnlySpan<Rgba32> Data => pixels.AsSpan(0, pixelCount);

    public PixelFormat Format => PixelFormat.Rgba;

    public RectangleI Bounds { get; set; } = new(0, 0, width, height);

    public int Level { get; } = level;

    public void Dispose()
    {
        if (disposed)
            return;

        pool.Return(pixels);
        disposed = true;
    }
}
