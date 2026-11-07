using System;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Timing;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;

// The outer note piece owns sizing and transforms; this leaf only advances its bitmap.
internal sealed partial class BmsResolvedTextureAnimation(Texture[] textures) : Sprite
{
    private const double frame_length = 1000 / 60d;
    private readonly double duration = textures.Length * frame_length;
    private double position;
    private double lastConsumedTime;
    private int currentFrameIndex = -1;

    public override IFrameBasedClock Clock
    {
        get => base.Clock;
        set
        {
            base.Clock = value;
            consumeClockTime();
        }
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        consumeClockTime();
    }

    protected override void Update()
    {
        base.Update();
        position += consumeClockTime();
        var playback = Math.Max(0, position % duration);
        var index = Math.Min((int)(playback / frame_length), textures.Length - 1);
        if (index == currentFrameIndex)
            return;

        currentFrameIndex = index;
        Texture = textures[index];
        Size = Texture.DisplaySize;
    }

    private double consumeClockTime()
    {
        var elapsed = Time.Current - lastConsumedTime;
        lastConsumedTime = Time.Current;
        return elapsed;
    }
}
