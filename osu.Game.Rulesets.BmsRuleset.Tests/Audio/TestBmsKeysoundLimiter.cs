using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[TestFixture]
public class TestBmsKeysoundLimiter
{
    [Test]
    public void LimitsLinkedStereoPeak()
    {
        float[] samples = [2, -1, 0.5f, -0.25f];
        var gain = 1f;

        BmsKeysoundMixerPatcher.ProcessSamples(samples, ref gain);

        Assert.That(samples[0], Is.EqualTo(0.98f).Within(0.00001f));
        Assert.That(samples[1], Is.EqualTo(-0.49f).Within(0.00001f));
        Assert.That(samples, Has.All.InRange(-0.98f, 0.98f));
        Assert.That(gain, Is.LessThan(1));
    }

    [Test]
    public void GainRecoversWithoutOvershooting()
    {
        var samples = new float[4410 * 2];
        var gain = 0.25f;

        BmsKeysoundMixerPatcher.ProcessSamples(samples, ref gain);

        Assert.That(gain, Is.GreaterThan(0.25f));
        Assert.That(gain, Is.LessThanOrEqualTo(1));
    }

    [Test]
    public void ChordEventsUseSameSampleOffset()
    {
        var first = BmsKeysoundMixerPatcher.GetSampleOffsetBytes(1000, 4692.307709750567);
        var second = BmsKeysoundMixerPatcher.GetSampleOffsetBytes(1000, 4692.307709750567);

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void LiveBatchDoesNotScheduleIntoAlreadyGeneratedAudio()
    {
        Assert.That(BmsKeysoundMixerPatcher.SelectLiveBatchStart(1000, 900), Is.EqualTo(1000));
    }

    [Test]
    public void LiveBatchPreservesFutureSamplePosition()
    {
        Assert.That(BmsKeysoundMixerPatcher.SelectLiveBatchStart(1000, 1200), Is.EqualTo(1200));
    }

    [TestCase(162830.23, 162830)]
    [TestCase(162830.77, 162831)]
    public void FractionalSampleOffsetUsesNearestSample(double exactSampleOffset, long expectedSampleOffset)
    {
        var targetTime = exactSampleOffset / 44.1;

        Assert.That(BmsKeysoundMixerPatcher.GetSampleOffsetBytes(0, targetTime), Is.EqualTo(expectedSampleOffset * sizeof(float) * 2));
    }

    [Test]
    public void AbsoluteOffsetsPreserveAliceBassSliceLength()
    {
        const double origin_time = 1846.1538461538462;
        var first = BmsKeysoundMixerPatcher.GetSampleOffsetBytes(origin_time, 16615.384615384617);
        var second = BmsKeysoundMixerPatcher.GetSampleOffsetBytes(origin_time, 20307.69230769231);

        Assert.That((second - first) / (sizeof(float) * 2), Is.EqualTo(162831));
    }

    [Test]
    public void AbsoluteOffsetsUseMixerSampleRate()
    {
        var offset = BmsKeysoundMixerPatcher.GetSampleOffsetBytes(0, 1000, 48000);

        Assert.That(offset, Is.EqualTo(48000 * sizeof(float) * 2));
    }

    [Test]
    public void TailRampEndsAtZero()
    {
        var nodes = BmsKeysoundMixerPatcher.CreateTailRampNodes(1000, 100);

        Assert.That(nodes, Has.Length.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(nodes[0].Position, Is.Zero);
            Assert.That(nodes[0].Value, Is.EqualTo(1));
            Assert.That(nodes[1].Position, Is.EqualTo(900));
            Assert.That(nodes[1].Value, Is.EqualTo(1));
            Assert.That(nodes[2].Position, Is.EqualTo(1000));
            Assert.That(nodes[2].Value, Is.Zero);
        });
    }

    [Test]
    public void TailRampIsCappedForShortSamples()
    {
        var nodes = BmsKeysoundMixerPatcher.CreateTailRampNodes(100, 80);

        Assert.That(nodes[1].Position, Is.EqualTo(50));
    }

    [Test]
    public void StartAndTailRampAvoidsHardVoiceBoundaries()
    {
        var nodes = BmsKeysoundMixerPatcher.CreateStartAndTailRampNodes(1000, 100, 100);

        Assert.Multiple(() =>
        {
            Assert.That(nodes[0].Position, Is.Zero);
            Assert.That(nodes[0].Value, Is.Zero);
            Assert.That(nodes[1].Position, Is.EqualTo(100));
            Assert.That(nodes[1].Value, Is.EqualTo(1));
            Assert.That(nodes[^1].Position, Is.EqualTo(1000));
            Assert.That(nodes[^1].Value, Is.Zero);
        });
    }
}
