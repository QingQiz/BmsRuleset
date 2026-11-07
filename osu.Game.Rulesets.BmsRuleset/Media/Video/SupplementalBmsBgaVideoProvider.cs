using System;
using System.Collections.Generic;
using System.IO;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.Media.FFmpeg;
using osu.Game.Rulesets.BmsRuleset.Media.Video.Supplemental;

namespace osu.Game.Rulesets.BmsRuleset.Media.Video;

internal sealed class SupplementalBmsBgaVideoProvider : IBmsBgaVideoProvider
{
    // Pure MPEG program/elementary streams. The framework's bundled FFmpeg ships no mpegps/
    // mpegvideo demuxer, so these containers are owned exclusively by the supplemental path —
    // framework declines these extensions.
    private static readonly HashSet<string> legacy_mpeg_container_extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mpg",
        ".mpeg",
        ".m1v",
        ".m2v",
    };

    // Containers the supplemental build can demux (mpegps, mpegvideo, asf, avi). The ambiguous
    // .wmv/.asf/.avi are claimed by framework too; provider order (supplemental first) plus the
    // codec probe splits them — supplemental takes files whose codec it has a decoder for and
    // declines the rest so framework picks them back up.
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

    public static bool IsLegacyMpegContainerExtension(string extension) => legacy_mpeg_container_extensions.Contains(extension);

    public bool TryCreate(BmsBgaVideoRequest request, out Drawable? drawable)
    {
        drawable = null;
        if (!BmsSupplementalFFmpegFuncs.IsAvailable || !probeable_extensions.Contains(Path.GetExtension(request.Path)))
            return false;

        var stream = request.Stream;
        if (!stream.CanSeek)
            return false;

        var position = stream.Position;

        try
        {
            if (stream.Position != 0)
                stream.Position = 0;

            using var memory = new MemoryStream();
            stream.CopyTo(memory);

            if (!BmsSupplementalVideoDecoder.TryCreate(memory.ToArray(), out var decoder, out _))
                return false;

            // The codec check already opened the decoder; transfer it rather than opening the same bytes again.
            var source = new BmsSupplementalVideoFrameSource(decoder);
            drawable = new BmsSupplementalVideoDrawable(source, request.Clock, request.EventStartTime, stream);
            return true;
        }
        finally
        {
            stream.Position = position;
        }
    }
}
