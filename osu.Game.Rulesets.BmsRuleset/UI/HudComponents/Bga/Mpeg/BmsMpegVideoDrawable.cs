using System;
using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Logging;
using osu.Framework.Timing;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents.Bga.Mpeg;

internal sealed partial class BmsMpegVideoDrawable : CompositeDrawable
{
    private readonly Stream stream;
    private readonly IClock playbackClock;
    private readonly double eventStartTime;
    private readonly Sprite sprite;
    private IRenderer? renderer;
    private Texture? texture;
    private BmsMpegVideoFrameSource? frameSource;

    public BmsMpegVideoDrawable(Stream stream, IClock playbackClock, double eventStartTime)
    {
        this.stream = stream;
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

    public int UploadedFrames { get; private set; }

    public BmsMpegVideoFrameSourceStats Stats => frameSource?.Stats ?? default;

    [BackgroundDependencyLoader]
    private void load(IRenderer renderer)
    {
        this.renderer = renderer;

        using var memory = new MemoryStream();
        if (stream.CanSeek)
            stream.Position = 0;

        stream.CopyTo(memory);
        frameSource = new BmsMpegVideoFrameSource(memory.ToArray());
        frameSource.Start();

        Logger.Log("[BGA] MPEG fallback selected", "bms-bga");
    }

    protected override void Update()
    {
        base.Update();

        if (frameSource == null || renderer == null)
            return;

        var playbackTime = Math.Max(0, (playbackClock.CurrentTime - eventStartTime) / 1000.0);
        frameSource.SetTargetTime(playbackTime);

        if (!frameSource.TryTakeLatestFrame(out var frame))
            return;

        using (var upload = frame!.CreateUpload())
        {
            if (texture == null || texture.Width != frame.Width || texture.Height != frame.Height)
            {
                texture = renderer.CreateTexture(frame.Width, frame.Height);
                sprite.Texture = texture;
            }

            texture.SetData(upload);
            UploadedFrames++;
        }
    }

    protected override void Dispose(bool isDisposing)
    {
        frameSource?.Dispose();
        stream.Dispose();
        base.Dispose(isDisposing);
    }
}
