using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Timing;

namespace osu.Game.Rulesets.BmsRuleset.Media.Video.Supplemental;

// Pre-warms BmsSupplementalVideoFrameSource instances during the loading screen so gameplay entry
// doesn't pay the FFmpeg init + first-frame latency on the game thread. The codec probe and the
// worker's avformat/avcodec open run on the loader thread here; createVideo() later just hands the
// already-decoding source to a drawable — no hitch at the BGA event's fire time.
internal sealed class BmsBgaVideoPreloader : IDisposable
{
    private readonly Dictionary<string, BmsSupplementalVideoFrameSource> warm = new(StringComparer.OrdinalIgnoreCase);
    private bool disposed;

    public void Preload(string path, byte[] bytes)
    {
        if (disposed)
            return;

        // Idempotent: a second call for the same path is a no-op. The loading screen may declare the
        // same BGA twice (base + poor layer share bitmap 0), and a rehosted display re-runs preload
        // against a shared preloader — neither should spawn a second decoder.
        if (warm.ContainsKey(path))
            return;

        // Mirror the supplemental provider's probe: only warm files the supplemental backend can
        // actually decode, so files routed to the framework/missing path are left for the factory.
        if (!BmsSupplementalVideoDecoder.TryCreate(bytes, out var probe, out _))
            return;

        probe?.Dispose();

        var source = new BmsSupplementalVideoFrameSource(bytes);
        source.Start();
        warm[path] = source;
    }

    // One-shot: ownership of the warm frame source transfers to the drawable. A later request for
    // the same path misses and falls back to a fresh factory create — acceptable mid-game (poor BGA,
    // layer swaps) and never at gameplay entry, which is the only place the hitch was visible.
    public bool TryCreateDrawable(string path, IClock clock, double eventStartTime, out Drawable? drawable)
    {
        drawable = null;

        if (disposed)
            return false;

        if (!warm.Remove(path, out var source))
            return false;

        drawable = new BmsSupplementalVideoDrawable(source, clock, eventStartTime)
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            FillMode = FillMode.Stretch,
        };
        return true;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        foreach (var source in warm.Values)
            source.Dispose();

        warm.Clear();
    }
}
