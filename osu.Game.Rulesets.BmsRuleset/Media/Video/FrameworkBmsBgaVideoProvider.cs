using System;
using System.IO;
using osu.Framework.Graphics;
using FrameworkVideo = osu.Framework.Graphics.Video.Video;

namespace osu.Game.Rulesets.BmsRuleset.Media.Video;

internal sealed class FrameworkBmsBgaVideoProvider : IBmsBgaVideoProvider
{
    public bool TryCreate(BmsBgaVideoRequest request, out Drawable? drawable)
    {
        drawable = null;
        if (SupplementalBmsBgaVideoProvider.IsLegacyMpegContainerExtension(Path.GetExtension(request.Path)))
            return false;

        drawable = new FrameworkVideo(request.Stream, startAtCurrentTime: false)
        {
            Clock = request.Clock,
            PlaybackPosition = Math.Max(0, request.Clock.CurrentTime - request.EventStartTime),
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            FillMode = FillMode.Fit,
            Loop = true,
        };
        return true;
    }
}
