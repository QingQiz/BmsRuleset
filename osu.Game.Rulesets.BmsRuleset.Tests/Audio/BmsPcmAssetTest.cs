using System;
using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[TestFixture]
public class BmsPcmAssetTest
{
    [Test]
    public void CursorReadsVariableChunksAcrossPagesAndSeeks([Values(1, 2, 4)] int channels)
    {
        var asset = new BmsPcmAsset(44100, channels);
        var expected = new List<float>();
        for (var i = 0; i < 130; i++)
        {
            var frames = i % 7 + 1;
            var samples = new float[frames * channels];
            for (var j = 0; j < samples.Length; j++)
                samples[j] = (expected.Count + j) / 2000f;
            asset.Publish(new BmsPcmChunk(expected.Count / channels, frames, samples));
            expected.AddRange(samples);
        }

        asset.Complete(expected.Count / channels);
        var cursor = new BmsPcmAsset.ReadCursor();
        for (var frame = 0; frame < expected.Count / channels; frame++)
            assertFrame(frame);
        for (var frame = expected.Count / channels - 1; frame >= 0; frame--)
            assertFrame(frame);
        var random = new Random(20260920);
        for (var i = 0; i < 300; i++)
            assertFrame(random.Next(expected.Count / channels));
        Assert.That(asset.TryReadStereoFrame(-1, ref cursor, out _, out _), Is.False);
        Assert.That(asset.TryReadStereoFrame(expected.Count / channels, ref cursor, out _, out _), Is.False);

        void assertFrame(int frame)
        {
            Assert.That(asset.TryReadStereoFrame(frame, ref cursor, out var left, out var right), Is.True);
            Assert.That(left, Is.EqualTo(expected[frame * channels]));
            Assert.That(right, Is.EqualTo(expected[frame * channels + (channels == 1 ? 0 : 1)]));
        }
    }

    [Test]
    public void FailedCursorReadsClearOutputAndPreserveSubsequentReads()
    {
        var stereo = new BmsPcmAsset(44100, 2);
        stereo.Publish(new BmsPcmChunk(0, 2, [0.1f, 0.2f, 0.3f, 0.4f]));
        var mono = new BmsPcmAsset(44100, 1);
        mono.Publish(new BmsPcmChunk(0, 1, [0.5f]));
        var cursor = new BmsPcmAsset.ReadCursor();
        Assert.That(stereo.TryReadStereoFrame(1, ref cursor, out _, out _), Is.True);

        long[] invalidFrames = [long.MinValue, -1, 1, long.MaxValue];
        foreach (var frame in invalidFrames)
        {
            Assert.That(mono.TryReadStereoFrame(frame, ref cursor, out var left, out var right), Is.False);
            Assert.That(left, Is.Zero);
            Assert.That(right, Is.Zero);
            Assert.That(stereo.TryReadStereoFrame(1, ref cursor, out left, out right), Is.True);
            Assert.That(left, Is.EqualTo(0.3f));
            Assert.That(right, Is.EqualTo(0.4f));
        }

        Assert.That(mono.TryReadStereoFrame(0, ref cursor, out var monoLeft, out var monoRight), Is.True);
        Assert.That(monoLeft, Is.EqualTo(0.5f));
        Assert.That(monoRight, Is.EqualTo(0.5f));
        mono.DisposePublishedChunks();
        Assert.That(mono.TryReadStereoFrame(0, ref cursor, out monoLeft, out monoRight), Is.False);
        Assert.That(monoLeft, Is.Zero);
        Assert.That(monoRight, Is.Zero);
        Assert.That(stereo.TryReadStereoFrame(0, ref cursor, out var stereoLeft, out var stereoRight), Is.True);
        Assert.That(stereoLeft, Is.EqualTo(0.1f));
        Assert.That(stereoRight, Is.EqualTo(0.2f));
    }

    [Test]
    public void CursorHandlesPublicationDisposalAndAnotherAsset()
    {
        var asset = new BmsPcmAsset(44100, 1);
        var cursor = new BmsPcmAsset.ReadCursor();
        asset.Publish(new BmsPcmChunk(0, 1, [0.1f]));
        Assert.That(asset.TryReadStereoFrame(0, ref cursor, out _, out var right), Is.True);
        Assert.That(right, Is.EqualTo(0.1f));
        Assert.That(asset.TryReadStereoFrame(1, ref cursor, out _, out _), Is.False);
        asset.Publish(new BmsPcmChunk(1, 1, [0.2f]));
        Assert.That(asset.TryReadStereoFrame(1, ref cursor, out var left, out _), Is.True);
        Assert.That(left, Is.EqualTo(0.2f));
        asset.DisposePublishedChunks();
        Assert.That(asset.TryReadStereoFrame(1, ref cursor, out _, out _), Is.False);
        var another = new BmsPcmAsset(44100, 1);
        another.Publish(new BmsPcmChunk(0, 2, [0.3f, 0.4f]));
        Assert.That(another.TryReadStereoFrame(1, ref cursor, out left, out _), Is.True);
        Assert.That(left, Is.EqualTo(0.4f));
    }

    [Test]
    public void PublishedChunksRemainPreparingUntilStartupWatermark()
    {
        var asset = new BmsPcmAsset(44100, 2);
        asset.Publish(new BmsPcmChunk(0, 2, [0.1f, 0.2f, 0.3f, 0.4f]));

        Assert.Multiple(() =>
        {
            Assert.That(asset.State, Is.EqualTo(BmsPcmAssetState.Preparing));
            Assert.That(asset.PublishedFrameCount, Is.EqualTo(2));
            Assert.That(asset.TotalFrameCount, Is.EqualTo(-1));
            Assert.That(asset.TryReadStereoFrame(1, out var left, out var right), Is.True);
            Assert.That(left, Is.EqualTo(0.3f));
            Assert.That(right, Is.EqualTo(0.4f));
        });

        asset.MarkReady();
        Assert.That(asset.State, Is.EqualTo(BmsPcmAssetState.Ready));
    }

    [Test]
    public void CompletionPublishesFinalLength()
    {
        var asset = new BmsPcmAsset(44100, 2);
        asset.Publish(new BmsPcmChunk(0, 1, [0, 0]));
        asset.Complete(1);

        Assert.Multiple(() =>
        {
            Assert.That(asset.IsComplete, Is.True);
            Assert.That(asset.TotalFrameCount, Is.EqualTo(1));
            Assert.That(asset.TryReadStereoFrame(1, out _, out _), Is.False);
        });
    }

    [Test]
    public void RejectsNonContiguousChunk()
    {
        var asset = new BmsPcmAsset(44100, 2);

        Assert.Throws<System.ArgumentException>(() =>
            asset.Publish(new BmsPcmChunk(1, 1, [0, 0])));
    }

    [TestCase(1073741824, 4, 0)]
    [TestCase(536870912, 8, 0)]
    [TestCase(int.MaxValue, int.MaxValue, 1)]
    public void RejectsInterleavedLengthThatOverflowsInt(int frames, int channels, int sampleCount)
    {
        var asset = new BmsPcmAsset(44100, channels);

        Assert.Throws<ArgumentException>(() => asset.Publish(new BmsPcmChunk(0, frames, new float[sampleCount])));
        Assert.That(asset.PublishedFrameCount, Is.Zero);
        Assert.That(asset.ResidentBytes, Is.Zero);
    }
}
