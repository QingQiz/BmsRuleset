using System;
using System.IO;
using osu.Framework.Graphics;
using FrameworkVideo = osu.Framework.Graphics.Video.Video;

namespace osu.Game.Rulesets.BmsRuleset.Media.Video;

internal sealed class FrameworkBmsBgaVideoProvider : IBmsBgaVideoProvider
{
    public string Name => "framework";

    public bool CanCreate(BmsBgaVideoRequest request) => !SupplementalBmsBgaVideoProvider.IsLegacyMpegContainerExtension(Path.GetExtension(request.Path));

    public Drawable Create(BmsBgaVideoRequest request) => new FrameworkVideo(request.Stream, startAtCurrentTime: false)
    {
        Clock = request.Clock,
        PlaybackPosition = Math.Max(0, request.Clock.CurrentTime - request.EventStartTime),
        Anchor = Anchor.Centre,
        Origin = Anchor.Centre,
        RelativeSizeAxes = Axes.Both,
        FillMode = FillMode.Fit,
        Loop = true,
    };
}
