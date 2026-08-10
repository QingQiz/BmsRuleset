using System;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Processing;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing;

internal sealed class BmsPlaybackClockMapper
{
    private readonly double rate;
    private readonly int sampleRate;
    private double originChartTime;
    private long originOutputFrame;

    internal BmsPlaybackClockMapper(double rate, int sampleRate = BmsFixedRatePcmProcessor.OUTPUT_SAMPLE_RATE)
    {
        if (!double.IsFinite(rate) || rate < 0.05 || rate > 2)
            throw new ArgumentOutOfRangeException(nameof(rate));

        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        this.rate = rate;
        this.sampleRate = sampleRate;
    }

    internal void Rebase(double chartTime, long outputFrame)
    {
        if (!double.IsFinite(chartTime))
            throw new ArgumentOutOfRangeException(nameof(chartTime));

        originChartTime = chartTime;
        originOutputFrame = Math.Max(0, outputFrame);
    }

    internal long Map(double chartTime, long renderedFrame)
    {
        if (!double.IsFinite(chartTime))
            return Math.Max(0, renderedFrame);

        var exactFrame = originOutputFrame + (chartTime - originChartTime) * sampleRate / (1000 * rate);
        var mappedFrame = (long)Math.Round(exactFrame, MidpointRounding.AwayFromZero);
        return Math.Max(Math.Max(0, mappedFrame), renderedFrame);
    }

    internal long MapSourceOffset(double chartOffsetMilliseconds)
    {
        if (!double.IsFinite(chartOffsetMilliseconds) || chartOffsetMilliseconds <= 0)
            return 0;

        return Math.Max(0, (long)Math.Round(chartOffsetMilliseconds * sampleRate / (1000 * rate), MidpointRounding.AwayFromZero));
    }
}
