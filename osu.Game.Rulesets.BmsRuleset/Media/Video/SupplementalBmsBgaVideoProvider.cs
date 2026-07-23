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
    // framework declines them via FrameworkBmsBgaVideoProvider.CanCreate = !IsLegacyMpegContainer.
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

    public string Name => "supplemental-ffmpeg";

    public static bool IsLegacyMpegContainerExtension(string extension) => legacy_mpeg_container_extensions.Contains(extension);

    public bool CanCreate(BmsBgaVideoRequest request)
        => BmsSupplementalFFmpegFuncs.IsAvailable
           && probeable_extensions.Contains(Path.GetExtension(request.Path))
           && probeClaimsSupplementalCodec(request);

    public Drawable Create(BmsBgaVideoRequest request) => new BmsSupplementalVideoDrawable(request.Stream, request.Clock, request.EventStartTime)
    {
        Anchor = Anchor.Centre,
        Origin = Anchor.Centre,
        RelativeSizeAxes = Axes.Both,
        FillMode = FillMode.Stretch,
    };

    // Route by codec, not extension: attempt a full supplemental decoder init on the stream bytes
    // and claim the file only if it succeeds. Reusing the decoder keeps the supported-codec set
    // auto-synced with the native build (profile.txt) — no second list to drift out of sync. The
    // stream position is saved/restored so the next provider (or Create's decoder) still sees 0.
    private bool probeClaimsSupplementalCodec(BmsBgaVideoRequest request)
    {
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

            decoder!.Dispose();
            return true;
        }
        finally
        {
            stream.Position = position;
        }
    }
}
