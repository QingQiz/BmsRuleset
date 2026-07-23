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

    // Test-only: opens the file so the codec-probing provider can inspect real bytes. Production
    // routing goes through Create(), which receives the stream the caller already opened.
    public static string SelectProviderName(string path)
    {
        using var stream = File.OpenRead(path);
        var request = new BmsBgaVideoRequest(path, stream, new ManualFramedClock(), 0);
        return selectProvider(request).Name;
    }

    public static Drawable? Create(string path, Stream stream, IFrameBasedClock clock, double eventStartTime)
    {
        var request = new BmsBgaVideoRequest(path, stream, clock, eventStartTime);
        return selectProvider(request).Create(request);
    }

    private static IBmsBgaVideoProvider selectProvider(BmsBgaVideoRequest request)
    {
        foreach (var provider in providers)
        {
            if (provider.CanCreate(request))
                return provider;
        }

        throw new InvalidOperationException("The missing-video provider must accept every request.");
    }
}
