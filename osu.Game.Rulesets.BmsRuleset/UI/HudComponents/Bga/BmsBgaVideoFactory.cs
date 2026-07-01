using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Video;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents.Bga.Mpeg;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents.Bga;

internal static class BmsBgaVideoFactory
{
    private static readonly List<string> mpeg_ext = [".mpg", ".mpeg", ".m1v"];

    public static bool UsesMpegFallback(string path)
    {
        var extension = Path.GetExtension(path);
        return mpeg_ext.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    public static Drawable Create(string path, Stream stream, IFrameBasedClock clock, double eventStartTime)
    {
        if (UsesMpegFallback(path))
        {
            return new BmsMpegVideoDrawable(stream, clock, eventStartTime)
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                FillMode = FillMode.Stretch,
            };
        }

        return new Video(stream, startAtCurrentTime: false)
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
}
