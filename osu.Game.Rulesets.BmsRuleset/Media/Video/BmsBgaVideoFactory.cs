using System;
using System.IO;
using osu.Framework.Graphics;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.Media.FFmpeg;
using osu.Game.Rulesets.BmsRuleset.Media.Video.Supplemental;
using FrameworkVideo = osu.Framework.Graphics.Video.Video;

namespace osu.Game.Rulesets.BmsRuleset.Media.Video;

internal static class BmsBgaVideoFactory
{
    public static Drawable Create(Stream stream, IFrameBasedClock clock, double eventStartTime)
    {
        // Packs often use a video suffix that does not match the container. Match the
        // preloader's content probe so replaying a consumed warm source uses the same decoder.
        if (tryCreateSupplemental(stream, clock, eventStartTime) is { } supplemental)
            return supplemental;

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

    private static BmsSupplementalVideoDrawable? tryCreateSupplemental(Stream stream, IFrameBasedClock clock, double eventStartTime)
    {
        if (!BmsSupplementalFFmpegFuncs.IsAvailable || !stream.CanSeek)
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
