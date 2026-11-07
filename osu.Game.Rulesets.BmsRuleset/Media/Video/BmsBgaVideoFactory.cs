using System;
using System.Collections.Generic;
using System.IO;
using osu.Framework.Graphics;
using osu.Framework.Timing;

namespace osu.Game.Rulesets.BmsRuleset.Media.Video;

internal static class BmsBgaVideoFactory
{
    private static readonly IReadOnlyList<IBmsBgaVideoProvider> providers =
    [
        new SupplementalBmsBgaVideoProvider(),
        new FrameworkBmsBgaVideoProvider(),
        new MissingBmsBgaVideoProvider(),
    ];

    public static Drawable? Create(string path, Stream stream, IFrameBasedClock clock, double eventStartTime)
    {
        var request = new BmsBgaVideoRequest(path, stream, clock, eventStartTime);
        foreach (var provider in providers)
        {
            if (provider.TryCreate(request, out var drawable))
                return drawable;
        }

        throw new InvalidOperationException("The missing-video provider must accept every request.");
    }
}
