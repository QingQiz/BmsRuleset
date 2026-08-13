using System;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing.Pcm;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[TestFixture]
public class BmsPcmVoiceMixerTest
{
    [Test]
    public void SameDomainRetriggerFadesOldVoiceWithoutDelayingNewVoice()
    {
        var asset = createConstantAsset(500, 1);
        var mixer = createMixer();
        var domain = new BmsTerminationDomain(1);
        BmsVoicePlay[] plays =
        [
            new(asset, domain, 0, 0.2f),
            new(asset, domain, 100, 0.2f),
        ];

        mixer.SubmitPlayBatch(plays);
        var output = BmsPcmTestHelpers.RenderFrames(mixer, 200, 31);

        Assert.Multiple(() =>
        {
            Assert.That(output[99 * 2], Is.EqualTo(0.2f).Within(0.00001f));
            Assert.That(output[100 * 2], Is.GreaterThan(0.20f));
            Assert.That(output[150 * 2], Is.InRange(0.19f, 0.22f));
            Assert.That(output[190 * 2], Is.EqualTo(0.2f).Within(0.00001f));
        });
    }

    [Test]
    public void DifferentDomainsInChordStartOnSameFrame()
    {
        var asset = createConstantAsset(500, 1);
        var mixer = createMixer();
        BmsVoicePlay[] plays =
        [
            new(asset, new BmsTerminationDomain(1), 64, 0.2f),
            new(asset, new BmsTerminationDomain(2), 64, 0.3f),
        ];

        mixer.SubmitPlayBatch(plays);
        var output = BmsPcmTestHelpers.RenderFrames(mixer, 80);

        Assert.Multiple(() =>
        {
            Assert.That(output[63 * 2], Is.Zero);
            Assert.That(output[64 * 2], Is.GreaterThan(0));
            Assert.That(output[64 * 2 + 1], Is.EqualTo(output[64 * 2]).Within(0.00001f));
        });
    }

    [Test]
    public void ChordLargerThanInitialCommandSegmentIsNotDropped()
    {
        var asset = createConstantAsset(100, 0.1f);
        var mixer = createMixer();
        var plays = createPlays(asset, 4097);

        mixer.SubmitPlayBatch(plays);
        var output = BmsPcmTestHelpers.RenderFrames(mixer, 1);

        Assert.Multiple(() =>
        {
            Assert.That(mixer.ActiveVoiceCount, Is.EqualTo(plays.Length));
            Assert.That(output[0], Is.GreaterThan(0));
        });
    }

    [Test]
    public void OverlappingVoicesExpandBeyondInitialVoiceSegment()
    {
        var asset = createConstantAsset(100, 0.1f);
        var mixer = createMixer();
        var plays = createPlays(asset, 513);

        mixer.SubmitPlayBatch(plays);
        var output = BmsPcmTestHelpers.RenderFrames(mixer, 1);

        Assert.Multiple(() =>
        {
            Assert.That(mixer.ActiveVoiceCount, Is.EqualTo(plays.Length));
            Assert.That(output[0], Is.GreaterThan(0));
        });
    }

    [Test]
    public void RenderingExpandedVoiceSegmentsDoesNotAllocate()
    {
        var asset = createConstantAsset(1000, 0.1f);
        var mixer = createMixer();
        mixer.SubmitPlayBatch(createPlays(asset, 513));
        var output = new float[2];

        mixer.Render(output);
        var before = GC.GetAllocatedBytesForCurrentThread();
        mixer.Render(output);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(allocated, Is.Zero);
    }

    [Test]
    public void CompletedVoicesReuseExpandedSegments()
    {
        var asset = createConstantAsset(1, 0.1f);
        var mixer = createMixer();
        var plays = createPlays(asset, 513);

        mixer.SubmitPlayBatch(plays);
        var first = BmsPcmTestHelpers.RenderFrames(mixer, 1);
        mixer.SubmitPlayBatch(plays);
        var second = BmsPcmTestHelpers.RenderFrames(mixer, 1);

        Assert.Multiple(() =>
        {
            Assert.That(first[0], Is.GreaterThan(0));
            Assert.That(second, Is.EqualTo(first));
        });
    }

    [Test]
    public void CompletedExpandedVoicesLeaveNoHistoricalScanRange()
    {
        var asset = createConstantAsset(1, 0.1f);
        var mixer = createMixer();
        mixer.SubmitPlayBatch(createPlays(asset, 513));

        BmsPcmTestHelpers.RenderFrames(mixer, 1);

        Assert.Multiple(() =>
        {
            Assert.That(mixer.ActiveVoiceCount, Is.Zero);
            Assert.That(getOccupiedVoiceSlotCount(mixer), Is.Zero);
        });

        mixer.SubmitPlayBatch([new BmsVoicePlay(asset, new BmsTerminationDomain(1), mixer.RenderedFrames)]);
        var output = BmsPcmTestHelpers.RenderFrames(mixer, 1);

        Assert.Multiple(() =>
        {
            Assert.That(output[0], Is.GreaterThan(0));
            Assert.That(getOccupiedVoiceSlotCount(mixer), Is.Zero);
        });
    }

    [Test]
    public void RejectedPlayReleasesVoiceReservation()
    {
        var asset = createConstantAsset(100, 0.1f);
        var mixer = createMixer();
        mixer.SubmitControl(BmsVoiceCommandType.ReplaceEpoch, 0, 1);
        mixer.SubmitPlayBatch([new BmsVoicePlay(asset, new BmsTerminationDomain(1), 0, Epoch: 0)]);

        BmsPcmTestHelpers.RenderFrames(mixer, 1);
        mixer.SubmitPlayBatch([new BmsVoicePlay(asset, new BmsTerminationDomain(1), 0, Epoch: 1)]);
        var output = BmsPcmTestHelpers.RenderFrames(mixer, 1);

        Assert.Multiple(() =>
        {
            Assert.That(mixer.ActiveVoiceCount, Is.EqualTo(1));
            Assert.That(output[0], Is.GreaterThan(0));
        });
    }

    [Test]
    public void LastSameFrameDomainRequestWins()
    {
        var asset = createConstantAsset(500, 1);
        var mixer = createMixer();
        var domain = new BmsTerminationDomain(1);
        BmsVoicePlay[] plays =
        [
            new(asset, domain, 0, 0.1f),
            new(asset, domain, 0, 0.3f),
        ];

        mixer.SubmitPlayBatch(plays);
        var output = BmsPcmTestHelpers.RenderFrames(mixer, 88);

        Assert.That(output[^2], Is.EqualTo(0.3f).Within(0.00001f));
    }

    [Test]
    public void RenderBlockSizeDoesNotChangeOutput()
    {
        var asset = createAlternatingAsset(2000, 0.7f);
        var oneFrameMixer = createMixer();
        var largeBlockMixer = createMixer();
        BmsVoicePlay[] plays =
        [
            new(asset, new BmsTerminationDomain(1), 0),
            new(asset, new BmsTerminationDomain(2), 37),
            new(asset, new BmsTerminationDomain(1), 181),
        ];

        oneFrameMixer.SubmitPlayBatch(plays);
        largeBlockMixer.SubmitPlayBatch(plays);

        var oneFrame = BmsPcmTestHelpers.RenderFrames(oneFrameMixer, 1000, 1);
        var largeBlock = BmsPcmTestHelpers.RenderFrames(largeBlockMixer, 1000, 257);

        Assert.That(largeBlock, Is.EqualTo(oneFrame));
    }

    [Test]
    public void LimiterKeepsLinkedStereoInsideCeiling()
    {
        var asset = createConstantAsset(500, 2);
        var mixer = createMixer();
        mixer.SubmitPlayBatch([new BmsVoicePlay(asset, new BmsTerminationDomain(1), 0)]);

        var output = BmsPcmTestHelpers.RenderFrames(mixer, 100);

        Assert.That(output, Has.All.InRange(-0.98f, 0.98f));
    }

    [Test]
    public void SteadyStateRenderDoesNotAllocate()
    {
        var asset = createConstantAsset(10000, 0.1f);
        var mixer = createMixer();
        mixer.SubmitPlayBatch([new BmsVoicePlay(asset, new BmsTerminationDomain(1), 0)]);
        var output = new float[256 * 2];

        mixer.Render(output);
        var before = GC.GetAllocatedBytesForCurrentThread();
        mixer.Render(output);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(allocated, Is.Zero);
    }

    [Test]
    public void SameDomainSineRetriggerHasNoDiscontinuityArtifact()
    {
        const int frames = 44100;
        var samples = new float[frames * 2];

        for (var frame = 0; frame < frames; frame++)
        {
            var value = (float)(Math.Sin(2 * Math.PI * 440 * frame / 44100) * 0.4);
            samples[frame * 2] = value;
            samples[frame * 2 + 1] = value;
        }

        var asset = BmsPcmTestHelpers.CreateAsset([new BmsPcmChunk(0, frames, samples)]);
        var mixer = createMixer();
        var domain = new BmsTerminationDomain(1);
        mixer.SubmitPlayBatch([
            new BmsVoicePlay(asset, domain, 0),
            new BmsVoicePlay(asset, domain, 1000),
            new BmsVoicePlay(asset, domain, 2000),
        ]);

        var output = BmsPcmTestHelpers.RenderFrames(mixer, 4000, 113);
        var report = BmsAudioArtifactAnalyzer.Analyze(output, 44100, 2);

        Assert.That(report.Artifacts, Has.None.Property(nameof(BmsAudioArtifact.Kind)).EqualTo("discontinuity"));
    }

    private static BmsPcmAsset createConstantAsset(int frames, float value)
    {
        var samples = new float[frames * 2];
        Array.Fill(samples, value);
        return BmsPcmTestHelpers.CreateAsset([new BmsPcmChunk(0, frames, samples)]);
    }

    private static BmsPcmAsset createAlternatingAsset(int frames, float value)
    {
        var samples = new float[frames * 2];
        for (var frame = 0; frame < frames; frame++)
        {
            samples[frame * 2] = frame % 2 == 0 ? value : -value;
            samples[frame * 2 + 1] = samples[frame * 2];
        }

        return BmsPcmTestHelpers.CreateAsset([new BmsPcmChunk(0, frames, samples)]);
    }

    private static BmsVoicePlay[] createPlays(BmsPcmAsset asset, int count)
    {
        var plays = new BmsVoicePlay[count];

        for (var i = 0; i < count; i++)
            plays[i] = new BmsVoicePlay(asset, new BmsTerminationDomain((ushort)(i + 1)), 0);

        return plays;
    }

    private static BmsPcmVoiceMixer createMixer() => new();

    private static int getOccupiedVoiceSlotCount(BmsPcmVoiceMixer mixer)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var segment = typeof(BmsPcmVoiceMixer).GetField("firstVoiceSegment", flags)!.GetValue(mixer);
        var count = 0;

        while (segment != null)
        {
            var type = segment.GetType();
            count += (int)type.GetField("ActiveCount", flags)!.GetValue(segment)!;
            segment = type.GetField("Next", flags)!.GetValue(segment);
        }

        return count;
    }
}
