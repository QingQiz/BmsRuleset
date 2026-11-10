using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

internal sealed class BmsBgaAnimationPlayback(BmsBgaEvent source)
{
    private readonly BmsBgaAnimation animation = source.Animation!;
    private int triggerCount;
    private int variant = -1;
    private double started = double.PositiveInfinity;
    private (int? Key, double StartTime)? displayedFrame;

    internal void Trigger(BmsBgaTrigger trigger, double time)
    {
        if (trigger != animation.Trigger || time < source.Time || time >= source.EndTime)
            return;
        if (triggerCount++ % animation.Interval != 0)
            return;

        variant = (variant + 1) % animation.Variants.Count;
        started = time;
        displayedFrame = null;
    }

    internal bool TryAdvance(double time, out (int? Key, double StartTime) frame)
    {
        frame = GetFrame(time);
        if (displayedFrame == frame)
            return false;

        displayedFrame = frame;
        return true;
    }

    internal double EndTime => variant < 0
        ? double.NegativeInfinity
        : started + System.Math.Max(500,
            animation.Variants[variant].Length == 0 ? 0 : animation.Variants[variant][^1].Offset);

    internal (int? Key, double StartTime) GetFrame(double time)
    {
        if (time < started || time >= source.EndTime || variant < 0)
            return (null, started);

        var frames = animation.Variants[variant];
        var low = 0;
        var high = frames.Length;
        var offset = time - started;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (frames[middle].Offset <= offset)
                low = middle + 1;
            else
                high = middle;
        }

        var frame = low == 0 ? (BmsBgaFrame?)null : frames[low - 1];
        return (frame?.DefinitionKey, started + (frame?.Offset ?? 0));
    }
}
