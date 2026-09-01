using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Timing;

namespace osu.Game.Rulesets.BmsRuleset.Media.Video;

internal sealed partial class BmsDeferredVideoDrawable : CompositeDrawable
{
    private readonly Func<System.IO.Stream?> streamProvider;
    private readonly string path;
    private readonly IFrameBasedClock clock;
    private readonly double eventStartTime;

    internal BmsDeferredVideoDrawable(string path, Func<System.IO.Stream?> streamProvider, IFrameBasedClock clock, double eventStartTime)
    {
        this.path = path;
        this.streamProvider = streamProvider;
        this.clock = clock;
        this.eventStartTime = eventStartTime;
        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        var stream = streamProvider();
        if (stream == null)
            return;

        var streamTransferred = false;

        try
        {
            var video = BmsBgaVideoFactory.Create(path, stream, clock, eventStartTime);
            if (video != null)
            {
                LoadComponentAsync(video, drawable => InternalChild = drawable);
                streamTransferred = true;
            }
        }
        finally
        {
            // Provider-created drawables own their stream. A rejected request does not.
            if (!streamTransferred)
                stream.Dispose();
        }
    }
}
