using System;
using System.Collections.Generic;
using System.IO;
using osu.Framework.Graphics;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.Media.FFmpeg;
using osu.Game.Rulesets.BmsRuleset.Media.Video.Supplemental;
using FrameworkVideo = osu.Framework.Graphics.Video.Video;

namespace osu.Game.Rulesets.BmsRuleset.Media.Video;

internal static class BmsBgaVideoFactory
{
    // The framework lacks the demuxers for pure MPEG program/elementary streams.
    private static readonly HashSet<string> legacy_mpeg_container_extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mpg",
        ".mpeg",
        ".m1v",
        ".m2v",
    };

    // WMV/ASF/AVI overlap with the framework; probing the codec gives the supplemental
    // backend only the files it can decode, leaving the rest for the framework.
    private static readonly HashSet<string> probeable_extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mpg",
        ".mpeg",
        ".m1v",
        ".m2v",
        ".wmv",
        ".asf",
        ".avi",
    };

    public static Drawable? Create(string path, Stream stream, IFrameBasedClock clock, double eventStartTime)
    {
        var extension = Path.GetExtension(path);
        if (tryCreateSupplemental(extension, stream, clock, eventStartTime) is { } supplemental)
            return supplemental;

        if (legacy_mpeg_container_extensions.Contains(extension))
        {
            BmsLogger.Log($"[BGA] No video backend accepted '{path}'.");
            return null;
        }

        return new FrameworkVideo(stream, startAtCurrentTime: false)
        {
            Clock = clock,
            PlaybackPosition = Math.Max(0, clock.CurrentTime - eventStartTime),
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            FillMode = FillMode.Fit,
            Loop = true,
        };
    }

    private static BmsSupplementalVideoDrawable? tryCreateSupplemental(string extension, Stream stream, IFrameBasedClock clock, double eventStartTime)
    {
        if (!BmsSupplementalFFmpegFuncs.IsAvailable || !probeable_extensions.Contains(extension) || !stream.CanSeek)
            return null;

        var position = stream.Position;

        try
        {
            if (stream.Position != 0)
                stream.Position = 0;

            using var memory = new MemoryStream();
            stream.CopyTo(memory);

            if (!BmsSupplementalVideoDecoder.TryCreate(memory.ToArray(), out var decoder, out _))
                return null;

            // The codec check already opened the decoder; transfer it rather than opening the same bytes again.
            var source = new BmsSupplementalVideoFrameSource(decoder);
            return new BmsSupplementalVideoDrawable(source, clock, eventStartTime, stream);
        }
        finally
        {
            stream.Position = position;
        }
    }
}
