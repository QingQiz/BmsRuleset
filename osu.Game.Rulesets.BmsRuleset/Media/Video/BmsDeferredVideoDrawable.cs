using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Timing;

namespace osu.Game.Rulesets.BmsRuleset.Media.Video;

internal sealed partial class BmsDeferredVideoDrawable : CompositeDrawable
{
    private readonly Func<System.IO.Stream?> streamProvider;
    private readonly IFrameBasedClock clock;
    private readonly double eventStartTime;

    internal BmsDeferredVideoDrawable(Func<System.IO.Stream?> streamProvider, IFrameBasedClock clock, double eventStartTime)
    {
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
            var video = BmsBgaVideoFactory.Create(stream, clock, eventStartTime);
            LoadComponentAsync(video, drawable => InternalChild = drawable);
            streamTransferred = true;
        }
        finally
        {
            // The created drawable owns its stream; otherwise this loader must release it.
            if (!streamTransferred)
                stream.Dispose();
        }
    }
}
