using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Processing;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

public partial class BmsFixedRatePcmProcessorTest
{
    // Frozen before changing refill storage, so these detect PCM changes independently
    // of whether two read sizes happen to share the same new implementation mistake.
    [TestCase(8000, 1, 10003, 55142, "46A33DE29D79AAB00E3A317FD382DF038D3FFF97CEBA8369C9BC0CFAB30066A0")]
    [TestCase(32000, 2, 10003, 13786, "16331D3F7002DA578FEB6ED4EC8C830058E4BA2A1D7459254FAC7AC9CA1911B4")]
    [TestCase(44100, 2, 10003, 10003, "78601FF9E71DC2C6678138113B9CFFCDF7746211D88AE1F1D9903C92D7179F33")]
    [TestCase(48000, 1, 10003, 9191, "BF61C3263AAEBBECD8ECB8C3A409A0729AB489550AEC99139201A2A5B38E1C02")]
    [TestCase(192000, 2, 10003, 2298, "B9C7F6D2340A35D395CEB4C6BCEC5B0C4C3FAC8EC4564CEA357D75405A8C2D3E")]
    [TestCase(96000, 4, 10003, 4596, "A7C0E87AFFDD289300F53E0BB8013464BE4F3A49218E65F310D0BDA5FC1E85E9")]
    [TestCase(22050, 3, 10003, 20006, "B84BEF5FF78A07EE8E265BE44525C9AA03DCB965DF2CB6E0867961BA7D7ABF91")]
    [TestCase(44100, 2, 0, 0, "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855")]
    [TestCase(44100, 2, 1, 1, "001F456125D8FB19574F73D18B03241D67544B1D7B7E51DCB53B271CDF967C03")]
    [TestCase(44100, 2, 4095, 4095, "6F688FE9E6CC0A56DAB9EBEF380E53DFAAB54BAF1B3520CA69978DC7327369BD")]
    [TestCase(44100, 2, 4096, 4096, "78496B5BB6BE2660E53ABD1C11FEFA136759C71230FF99DD3F6EAD37C9937D71")]
    [TestCase(44100, 2, 4097, 4097, "A03A6E0C86B56B0A944DDEA2194EC1F32ABB83E2B69F5E2229EC732F4153D85F")]
    public void PcmMatchesFrozenReferenceAcrossReadAndOutputBoundaries(int sampleRate, int channels, int frames, int outputFrames, string expectedHash)
    {
        var input = createReferenceSamples(frames, channels);
        foreach (var readFrames in new[] { 1, 37, 4096 })
        {
            using var processor = new BmsFixedRatePcmProcessor(new ArrayPcmSource(input, sampleRate, channels, readFrames * channels), 1);
            var chunks = BmsPcmTestHelpers.ProcessChunks(processor);
            var output = chunks.SelectMany(chunk => chunk.Samples).ToArray();
            var nextFrame = 0L;
            foreach (var chunk in chunks)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(chunk.StartFrame, Is.EqualTo(nextFrame));
                    Assert.That(chunk.Samples.Length, Is.EqualTo(chunk.FrameCount * 2));
                });
                nextFrame += chunk.FrameCount;
            }

            Assert.Multiple(() =>
            {
                Assert.That(nextFrame, Is.EqualTo(outputFrames), $"Read size: {readFrames} frames");
                Assert.That(pcmHash(output), Is.EqualTo(expectedHash), $"Read size: {readFrames} frames");
            });
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PublishedChunksRemainIndependentAfterEarlyTermination(bool cancel)
    {
        var input = createReferenceSamples(20003, 2);
        using var processor = new BmsFixedRatePcmProcessor(new ArrayPcmSource(input, 48000, 2, 74), 1);
        using var cancellation = new CancellationTokenSource();
        using var chunks = processor.ProcessChunks(cancellation.Token).GetEnumerator();
        Assert.That(chunks.MoveNext(), Is.True);
        var published = chunks.Current;
        var before = pcmHash(published.Samples);
        if (cancel)
        {
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => chunks.MoveNext());
        }
        chunks.Dispose();

        using var replacement = new BmsFixedRatePcmProcessor(new ArrayPcmSource(new float[input.Length], 48000, 2, 8192), 1);
        var replacementChunks = BmsPcmTestHelpers.ProcessChunks(replacement);
        Assert.Multiple(() =>
        {
            Assert.That(replacementChunks.SelectMany(chunk => chunk.Samples), Is.All.Zero);
            Assert.That(pcmHash(published.Samples), Is.EqualTo(before));
        });
    }

    [TestCase(-1, "The PCM source returned an invalid sample count.")]
    [TestCase(8193, "The PCM source returned an invalid sample count.")]
    [TestCase(1, "The PCM source returned an incomplete sample frame.")]
    public void InvalidSourceReadsAreRejected(int returnedSamples, string message)
    {
        using var processor = new BmsFixedRatePcmProcessor(new InvalidPcmSource(returnedSamples), 1);
        var exception = Assert.Throws<InvalidOperationException>(() => BmsPcmTestHelpers.ProcessChunks(processor));
        Assert.That(exception!.Message, Is.EqualTo(message));
    }

    [Test]
    public void SourceFailureIsPropagated()
    {
        using var processor = new BmsFixedRatePcmProcessor(new InvalidPcmSource(null), 1);
        var exception = Assert.Throws<InvalidOperationException>(() => BmsPcmTestHelpers.ProcessChunks(processor));
        Assert.That(exception!.Message, Is.EqualTo("Source failure"));
    }

    private static float[] createReferenceSamples(int frames, int channels)
    {
        var samples = new float[frames * channels];
        for (var sample = 0; sample < samples.Length; sample++)
            samples[sample] = (sample * 73 % 65521 - 32760) / 32768f;
        return samples;
    }

    private static string pcmHash(float[] samples) => Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(samples.AsSpan())));

    private sealed class InvalidPcmSource(int? returnedSamples) : IBmsPcmSource
    {
        public int SampleRate => 44100;
        public int Channels => 2;
        public double? OriginalDurationMilliseconds => null;
        public int Read(float[] buffer, int offset, int count) => returnedSamples ?? throw new InvalidOperationException("Source failure");
        public void Dispose()
        {
        }
    }
}
