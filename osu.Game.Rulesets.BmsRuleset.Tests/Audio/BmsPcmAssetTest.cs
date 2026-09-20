using System;
using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[TestFixture]
public class BmsPcmAssetTest
{
    [Test]
    public void CursorReadsVariableChunksAcrossPagesAndSeeks()
    {
        var asset = new BmsPcmAsset(44100, 2);
        var expected = new List<float>();
        for (var i = 0; i < 130; i++)
        {
            var frames = i % 7 + 1;
            var samples = new float[frames * 2];
            for (var j = 0; j < samples.Length; j++)
                samples[j] = (expected.Count + j) / 2000f;
            asset.Publish(new BmsPcmChunk(expected.Count / 2, frames, samples));
            expected.AddRange(samples);
        }

        asset.Complete(expected.Count / 2);
        var cursor = new BmsPcmAsset.ReadCursor();
        for (var frame = 0; frame < expected.Count / 2; frame++)
            assertFrame(frame);
        for (var frame = expected.Count / 2 - 1; frame >= 0; frame--)
            assertFrame(frame);
        var random = new Random(20260920);
        for (var i = 0; i < 300; i++)
            assertFrame(random.Next(expected.Count / 2));
        Assert.That(asset.TryReadStereoFrame(-1, ref cursor, out _, out _), Is.False);
        Assert.That(asset.TryReadStereoFrame(expected.Count / 2, ref cursor, out _, out _), Is.False);

        void assertFrame(int frame)
        {
            Assert.That(asset.TryReadStereoFrame(frame, ref cursor, out var left, out var right), Is.True);
            Assert.That(left, Is.EqualTo(expected[frame * 2]));
            Assert.That(right, Is.EqualTo(expected[frame * 2 + 1]));
        }
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
}
