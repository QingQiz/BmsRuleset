using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Processing;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[TestFixture]
public class BmsFixedRatePcmProcessorTest
{
    [Test]
    public void Mono48KhzIsContinuouslyResampledToStereo44Khz()
    {
        var input = createSine(48000, 4800, 440, 1);
        using var processor = new BmsFixedRatePcmProcessor(new ArrayPcmSource(input, 48000, 1, 37), 1, 113);
        var asset = processor.Process();

        Assert.Multiple(() =>
        {
            Assert.That(asset.SampleRate, Is.EqualTo(44100));
            Assert.That(asset.Channels, Is.EqualTo(2));
            Assert.That(asset.TotalFrameCount, Is.EqualTo(4410));
            Assert.That(asset.Chunks, Has.Count.EqualTo(40));
        });

        var output = asset.Chunks.SelectMany(chunk => chunk.Samples).ToArray();
        for (var i = 0; i < output.Length; i += 2)
            Assert.That(output[i + 1], Is.EqualTo(output[i]).Within(0.000001f));

        Assert.That(estimateFrequency(output, 44100, 2), Is.EqualTo(440).Within(1));
    }

    [Test]
    public void SourceAndOutputChunkSizesDoNotChangePcm()
    {
        var input = createSine(32000, 3200, 523.25, 2);
        using var fineProcessor = new BmsFixedRatePcmProcessor(new ArrayPcmSource(input, 32000, 2, 2), 1, 17);
        using var coarseProcessor = new BmsFixedRatePcmProcessor(new ArrayPcmSource(input, 32000, 2, 317), 1, 509);

        var fine = fineProcessor.Process().Chunks.SelectMany(chunk => chunk.Samples).ToArray();
        var coarse = coarseProcessor.Process().Chunks.SelectMany(chunk => chunk.Samples).ToArray();

        Assert.That(coarse, Is.EqualTo(fine));
    }

    [Test]
    public void CancellationStopsContinuousProcessing()
    {
        var source = new ArrayPcmSource(new float[10000], 44100, 1, 64);
        using var processor = new BmsFixedRatePcmProcessor(source, 1);
        using var cancellation = new System.Threading.CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => processor.Process(cancellation.Token));
    }

    private static float[] createSine(int sampleRate, int frames, double frequency, int channels)
    {
        var samples = new float[frames * channels];

        for (var frame = 0; frame < frames; frame++)
        {
            var value = (float)Math.Sin(2 * Math.PI * frequency * frame / sampleRate);
            for (var channel = 0; channel < channels; channel++)
                samples[frame * channels + channel] = value;
        }

        return samples;
    }

    private static double estimateFrequency(float[] samples, int sampleRate, int channels)
    {
        var crossings = 0;
        for (var frame = 1; frame < samples.Length / channels; frame++)
        {
            if (samples[(frame - 1) * channels] <= 0 && samples[frame * channels] > 0)
                crossings++;
        }

        return crossings * sampleRate / (samples.Length / (double)channels);
    }

    private sealed class ArrayPcmSource(float[] samples, int sampleRate, int channels, int maximumReadSamples) : IBmsPcmSource
    {
        private int position;

        public int SampleRate { get; } = sampleRate;

        public int Channels { get; } = channels;

        public double? OriginalDurationMilliseconds => samples.Length / (double)(SampleRate * Channels) * 1000;

        public int Read(float[] buffer, int offset, int count)
        {
            var remaining = samples.Length - position;
            var read = Math.Min(Math.Min(count, maximumReadSamples), remaining);
            read -= read % Channels;

            Array.Copy(samples, position, buffer, offset, read);
            position += read;
            return read;
        }

        public void Dispose()
        {
        }
    }
}
