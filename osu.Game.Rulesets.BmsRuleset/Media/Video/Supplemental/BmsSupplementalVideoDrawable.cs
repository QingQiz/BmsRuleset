using System;
using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Timing;

namespace osu.Game.Rulesets.BmsRuleset.Media.Video.Supplemental;

internal sealed partial class BmsSupplementalVideoDrawable : CompositeDrawable
{
    private readonly Stream? stream;
    private readonly IClock playbackClock;
    private readonly double eventStartTime;
    private readonly Sprite sprite;
    private IRenderer? renderer;
    private Texture? texture;
    private readonly BmsSupplementalVideoFrameSource frameSource;

    public BmsSupplementalVideoDrawable(BmsSupplementalVideoFrameSource frameSource, IClock playbackClock, double eventStartTime, Stream? stream = null)
    {
        this.stream = stream;
        this.frameSource = frameSource;
        this.playbackClock = playbackClock;
        this.eventStartTime = eventStartTime;

        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        RelativeSizeAxes = Axes.Both;
        FillMode = FillMode.Stretch;

        InternalChild = sprite = new Sprite
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            FillMode = FillMode.Fit,
        };
    }

    [BackgroundDependencyLoader]
    private void load(IRenderer renderer)
    {
        this.renderer = renderer;

        // Preloading may have started the handed-off decoder already.
        frameSource.Start();
        BmsLogger.Log("[BGA] Supplemental FFmpeg video path selected");
    }

    protected override void Update()
    {
        base.Update();

        if (renderer == null)
            return;

        var playbackTime = Math.Max(0, (playbackClock.CurrentTime - eventStartTime) / 1000.0);
        frameSource.SetTargetTime(playbackTime);

        if (!frameSource.TryTakeLatestFrame(out var frame))
            return;

        var currentFrame = frame;

        using (currentFrame)
        using (var upload = currentFrame.CreateUpload())
        {
            if (texture == null || texture.Width != currentFrame.Width || texture.Height != currentFrame.Height)
            {
                texture = renderer.CreateTexture(currentFrame.Width, currentFrame.Height);
                sprite.Texture = texture;
            }

            texture.SetData(upload);
        }
    }

    protected override void Dispose(bool isDisposing)
    {
        frameSource.Dispose();
        stream?.Dispose();
        base.Dispose(isDisposing);
    }
}
