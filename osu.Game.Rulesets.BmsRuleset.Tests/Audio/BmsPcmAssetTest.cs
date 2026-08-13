using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[TestFixture]
public class BmsPcmAssetTest
{
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
