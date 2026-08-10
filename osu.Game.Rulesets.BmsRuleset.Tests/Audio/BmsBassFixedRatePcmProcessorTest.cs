#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ManagedBass;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Processing;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[TestFixture]
[NonParallelizable]
public class BmsBassFixedRatePcmProcessorTest
{
    [OneTimeSetUp]
    public void InitialiseBass()
    {
        if (!Bass.Init(0, 44100, DeviceInitFlags.Default, IntPtr.Zero, IntPtr.Zero))
            Assert.Ignore($"The BASS no-sound device is unavailable: {Bass.LastError}.");
    }

    [OneTimeTearDown]
    public void FreeBass() => Bass.Free();

    [TestCase(0.5)]
    [TestCase(0.75)]
    [TestCase(1.0)]
    [TestCase(1.5)]
    [TestCase(2.0)]
    public void FixedTempoChangesDurationWithoutChangingPitch(double rate)
    {
        var wave = createWave(44100, 44100 * 2, 440);
        using var processor = BmsFixedRatePcmProcessor.CreateFromMemory(wave, rate, 1024);
        var asset = processor.Process();
        var samples = asset.Chunks.SelectMany(chunk => chunk.Samples).ToArray();
        var expectedFrames = 44100 * 2 / rate;

        Assert.Multiple(() =>
        {
            Assert.That(asset.TotalFrameCount, Is.EqualTo(expectedFrames).Within(2500));
            Assert.That(estimateFrequency(samples, 44100, 2, 4410), Is.EqualTo(440).Within(3));
        });
    }

    [Test]
    public async Task AssetCacheDeduplicatesAndPublishesStartupBuffer()
    {
        var wave = createWave(44100, 44100, 440);
        using var cache = new BmsPcmAssetCache((_, _) => Task.FromResult<byte[]?>(wave), 1, startupFrames: 512);
        using var first = cache.Acquire("same.wav");
        using var second = cache.Acquire("same.wav");

        await Task.WhenAll(first.Ready, second.Ready);

        Assert.Multiple(() =>
        {
            Assert.That(second.Asset, Is.SameAs(first.Asset));
            Assert.That(first.Asset.PublishedFrameCount, Is.GreaterThanOrEqualTo(512));
            Assert.That(first.Asset.State, Is.AnyOf(BmsPcmAssetState.Ready, BmsPcmAssetState.Complete));
        });

        await first.Completion;
        Assert.That(first.Asset.IsComplete, Is.True);
    }

    private static byte[] createWave(int sampleRate, int frames, double frequency)
    {
        const short channels = 1;
        const short bits_per_sample = 16;
        var dataBytes = frames * channels * bits_per_sample / 8;
        using var stream = new MemoryStream(44 + dataBytes);
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bits_per_sample / 8);
        writer.Write((short)(channels * bits_per_sample / 8));
        writer.Write(bits_per_sample);
        writer.Write("data"u8);
        writer.Write(dataBytes);

        for (var frame = 0; frame < frames; frame++)
            writer.Write((short)(Math.Sin(2 * Math.PI * frequency * frame / sampleRate) * short.MaxValue * 0.5));

        writer.Flush();
        return stream.ToArray();
    }

    private static double estimateFrequency(float[] samples, int sampleRate, int channels, int skipFrames)
    {
        var frames = samples.Length / channels;
        var crossings = 0;

        for (var frame = Math.Max(1, skipFrames + 1); frame < frames - skipFrames; frame++)
        {
            if (samples[(frame - 1) * channels] <= 0 && samples[frame * channels] > 0)
                crossings++;
        }

        return crossings * sampleRate / (double)Math.Max(1, frames - skipFrames * 2);
    }
}
