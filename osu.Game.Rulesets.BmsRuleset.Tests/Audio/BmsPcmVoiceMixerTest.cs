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
        var mixer = new BmsPcmVoiceMixer();
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
        var mixer = new BmsPcmVoiceMixer();
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
        var mixer = new BmsPcmVoiceMixer(voiceCapacity: 4, commandSegmentCapacity: 1);
        BmsVoicePlay[] plays =
        [
            new(asset, new BmsTerminationDomain(1), 0),
            new(asset, new BmsTerminationDomain(2), 0),
            new(asset, new BmsTerminationDomain(3), 0),
        ];

        mixer.SubmitPlayBatch(plays);
        BmsPcmTestHelpers.RenderFrames(mixer, 1);
        var diagnostics = mixer.GetDiagnostics();

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.StartedVoices, Is.EqualTo(3));
            Assert.That(diagnostics.ActiveVoices, Is.EqualTo(3));
            Assert.That(diagnostics.QueueExpansions, Is.EqualTo(1));
        });
    }

    [Test]
    public void OverlappingVoicesExpandBeyondInitialVoiceSegment()
    {
        var asset = createConstantAsset(100, 0.1f);
        var mixer = new BmsPcmVoiceMixer(voiceCapacity: 1);
        mixer.SubmitPlayBatch([
            new BmsVoicePlay(asset, new BmsTerminationDomain(1), 0),
            new BmsVoicePlay(asset, new BmsTerminationDomain(2), 0),
            new BmsVoicePlay(asset, new BmsTerminationDomain(3), 0),
        ]);

        BmsPcmTestHelpers.RenderFrames(mixer, 1);
        var diagnostics = mixer.GetDiagnostics();

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.StartedVoices, Is.EqualTo(3));
            Assert.That(diagnostics.ActiveVoices, Is.EqualTo(3));
            Assert.That(diagnostics.VoicePoolExpansions, Is.EqualTo(1));
            Assert.That(diagnostics.VoicePoolOverflows, Is.Zero);
        });
    }

    [Test]
    public void RenderingExpandedVoiceSegmentsDoesNotAllocate()
    {
        var asset = createConstantAsset(1000, 0.1f);
        var mixer = new BmsPcmVoiceMixer(voiceCapacity: 1);
        mixer.SubmitPlayBatch([
            new BmsVoicePlay(asset, new BmsTerminationDomain(1), 0),
            new BmsVoicePlay(asset, new BmsTerminationDomain(2), 0),
        ]);
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
        var mixer = new BmsPcmVoiceMixer(voiceCapacity: 1);
        BmsVoicePlay[] plays =
        [
            new(asset, new BmsTerminationDomain(1), 0),
            new(asset, new BmsTerminationDomain(2), 0),
        ];

        mixer.SubmitPlayBatch(plays);
        BmsPcmTestHelpers.RenderFrames(mixer, 1);
        mixer.SubmitPlayBatch(plays);
        BmsPcmTestHelpers.RenderFrames(mixer, 1);
        var diagnostics = mixer.GetDiagnostics();

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.StartedVoices, Is.EqualTo(4));
            Assert.That(diagnostics.VoicePoolExpansions, Is.EqualTo(1));
            Assert.That(diagnostics.VoicePoolOverflows, Is.Zero);
        });
    }

    [Test]
    public void RejectedPlayReleasesVoiceReservation()
    {
        var asset = createConstantAsset(100, 0.1f);
        var mixer = new BmsPcmVoiceMixer(voiceCapacity: 1);
        mixer.SubmitControl(BmsVoiceCommandType.ReplaceEpoch, 0, 1);
        mixer.SubmitPlayBatch([new BmsVoicePlay(asset, new BmsTerminationDomain(1), 0, Epoch: 0)]);

        BmsPcmTestHelpers.RenderFrames(mixer, 1);
        mixer.SubmitPlayBatch([new BmsVoicePlay(asset, new BmsTerminationDomain(1), 0, Epoch: 1)]);
        BmsPcmTestHelpers.RenderFrames(mixer, 1);
        var diagnostics = mixer.GetDiagnostics();

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.StartedVoices, Is.EqualTo(1));
            Assert.That(diagnostics.VoicePoolExpansions, Is.Zero);
            Assert.That(diagnostics.VoicePoolOverflows, Is.Zero);
        });
    }

    [Test]
    public void LastSameFrameDomainRequestWins()
    {
        var asset = createConstantAsset(500, 1);
        var mixer = new BmsPcmVoiceMixer();
        var domain = new BmsTerminationDomain(1);
        BmsVoicePlay[] plays =
        [
            new(asset, domain, 0, 0.1f),
            new(asset, domain, 0, 0.3f),
        ];

        mixer.SubmitPlayBatch(plays);
        var output = BmsPcmTestHelpers.RenderFrames(mixer, 88);
        var diagnostics = mixer.GetDiagnostics();

        Assert.That(output[^2], Is.EqualTo(0.3f).Within(0.00001f));
        Assert.That(diagnostics.SubmittedVoices, Is.EqualTo(2));
        Assert.That(diagnostics.FoldedVoices, Is.EqualTo(1));
    }

    [Test]
    public void RenderBlockSizeDoesNotChangeOutput()
    {
        var asset = createAlternatingAsset(2000, 0.7f);
        var oneFrameMixer = new BmsPcmVoiceMixer();
        var largeBlockMixer = new BmsPcmVoiceMixer();
        BmsVoicePlay[] plays =
        [
            new(asset, new BmsTerminationDomain(1), 0, 1),
            new(asset, new BmsTerminationDomain(2), 37, 1),
            new(asset, new BmsTerminationDomain(1), 181, 1),
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
        var mixer = new BmsPcmVoiceMixer();
        mixer.SubmitPlayBatch([new BmsVoicePlay(asset, new BmsTerminationDomain(1), 0)]);

        var output = BmsPcmTestHelpers.RenderFrames(mixer, 100);
        var diagnostics = mixer.GetDiagnostics();

        Assert.Multiple(() =>
        {
            Assert.That(output, Has.All.InRange(-0.98f, 0.98f));
            Assert.That(diagnostics.LimitedFrames, Is.GreaterThan(0));
            Assert.That(diagnostics.InputPeak, Is.EqualTo(2).Within(0.00001f));
        });
    }

    [Test]
    public void SteadyStateRenderDoesNotAllocate()
    {
        var asset = createConstantAsset(10000, 0.1f);
        var mixer = new BmsPcmVoiceMixer();
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
        var mixer = new BmsPcmVoiceMixer();
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
}
