using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Timing;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;

// Legacy texture sequences only animate their bitmap. Keeping the existing outer
// sizing component while drawing on this leaf avoids two otherwise empty composites.
internal sealed partial class BmsResolvedTextureAnimation : Sprite, IFramedAnimation
{
    private readonly record struct Frame(Texture Texture, double Start, double End);
    private readonly List<Frame> frames = [];
    private double position;
    private double lastConsumedTime;
    private bool frameDirty = true;
    private bool applyingFrame;
    private bool hasCustomWidth;
    private bool hasCustomHeight;

    internal double DefaultFrameLength = 1000 / 60d;
    public int FrameCount => frames.Count;
    public int CurrentFrameIndex { get; private set; }
    internal Texture? CurrentFrame => frames.Count == 0 ? null : frames[CurrentFrameIndex].Texture;
    public double Duration { get; private set; }
    public bool IsPlaying { get; set; } = true;
    public bool Loop { get; set; } = true;

    public double PlaybackPosition
    {
        get => Math.Clamp(Loop ? position % Duration : position, 0, Duration);
        set
        {
            position = value;
            if (IsLoaded) consumeClockTime();
        }
    }

    public override IFrameBasedClock Clock
    {
        get => base.Clock;
        set
        {
            base.Clock = value;
            consumeClockTime();
        }
    }

    public override float Width
    {
        set
        {
            base.Width = value;
            if (!applyingFrame) hasCustomWidth = true;
        }
    }

    public override float Height
    {
        set
        {
            base.Height = value;
            if (!applyingFrame) hasCustomHeight = true;
        }
    }

    public override Vector2 Size
    {
        set
        {
            Width = value.X;
            Height = value.Y;
        }
    }

    internal BmsResolvedTextureAnimation(Texture[] textures)
    {
        foreach (var texture in textures) AddFrame(texture);
    }

    internal void AddFrame(Texture texture, double? displayDuration = null)
    {
        var start = Duration;
        Duration += displayDuration ?? DefaultFrameLength;
        frames.Add(new Frame(texture, start, Duration));
        if (frames.Count == 1) frameDirty = true;
    }

    internal void ClearFrames()
    {
        frames.Clear();
        Duration = 0;
        CurrentFrameIndex = 0;
        // Clearing the bitmap leaves the animation's last layout intact, as in
        // the framework sequence; newly added frames can establish it again.
        var aspect = FillAspectRatio;
        applyingFrame = true;
        Texture = null;
        applyingFrame = false;
        FillAspectRatio = aspect;
    }

    public void Seek(double time) => PlaybackPosition = time;

    public void GotoFrame(int frameIndex)
        => Seek(frames[Math.Clamp(frameIndex, 0, frames.Count)].Start);

    protected override void LoadComplete()
    {
        base.LoadComplete();
        consumeClockTime();
    }

    protected override void Update()
    {
        base.Update();
        var elapsed = consumeClockTime();
        if (IsPlaying) position += elapsed;
        if (frames.Count == 0) return;

        var playback = PlaybackPosition;
        var previous = CurrentFrameIndex;
        if (playback < frames[CurrentFrameIndex].Start)
        {
            while (CurrentFrameIndex > 0 && playback < frames[CurrentFrameIndex].Start)
                CurrentFrameIndex--;
        }
        else if (playback > frames[CurrentFrameIndex].Start)
        {
            while (CurrentFrameIndex < frames.Count - 1 && playback >= frames[CurrentFrameIndex].End)
                CurrentFrameIndex++;
        }
        if (!frameDirty && previous == CurrentFrameIndex) return;
        frameDirty = false;

        var frame = frames[CurrentFrameIndex].Texture;
        var size = Size;
        applyingFrame = true;
        Texture = frame;
        // The Sprite setter may initialise a zero size. Preserve explicitly set
        // axes and the framework animation's distinction between custom and intrinsic size.
        Size = size;
        if ((RelativeSizeAxes & Axes.X) == 0 && !hasCustomWidth) base.Width = frame.DisplayWidth;
        if ((RelativeSizeAxes & Axes.Y) == 0 && !hasCustomHeight) base.Height = frame.DisplayHeight;
        applyingFrame = false;
    }

    private double consumeClockTime()
    {
        var elapsed = Time.Current - lastConsumedTime;
        lastConsumedTime = Time.Current;
        return elapsed;
    }
}
