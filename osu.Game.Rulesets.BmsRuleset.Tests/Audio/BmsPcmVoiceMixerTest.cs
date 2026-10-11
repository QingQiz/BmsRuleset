using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing.Pcm;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[TestFixture]
public class BmsPcmVoiceMixerTest
{
    [Test]
    public void RenderingIndexDropsEmptyCapacityAndKeepsReuseOrder()
    {
        var mixer = createMixer();
        var asset = createConstantAsset(1, .01f);
        mixer.SubmitPlayBatch(createPlays(asset, 1536));
        mixer.Render(new float[2]);
        Assert.That(getOccupiedVoiceSlotCount(mixer), Is.Zero);
        assertRenderingIndex(0);

        mixer.SubmitPlayBatch([new BmsVoicePlay(createConstantAsset(1000, .01f), new BmsTerminationDomain(1), 1)]);
        var output = new float[2];
        mixer.Render(output);
        assertRenderingIndex(1);
        mixer.Render(output);
        var before = GC.GetAllocatedBytesForCurrentThread();
        mixer.Render(output);
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);

        void assertRenderingIndex(int expected)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var first = typeof(BmsPcmVoiceMixer).GetField("firstVoiceSegment", flags)!.GetValue(mixer)!;
            var type = first.GetType();
            var capacity = new List<object>();
            for (var segment = first; segment != null; segment = type.GetField("Next", flags)!.GetValue(segment))
                capacity.Add(segment);
            Assert.That(capacity.Count, Is.EqualTo(2), "Retired capacity remains available for later producer reservations.");
            var head = typeof(BmsPcmVoiceMixer).GetField("firstRenderingSegment", flags);
            Assert.That(head, Is.Not.Null, "Only the production rendering index should exclude retired capacity.");
            var occupied = new List<object>();
            var previous = default(object);
            for (var segment = head!.GetValue(mixer); segment != null; segment = type.GetField("NextRendering", flags)!.GetValue(segment))
            {
                Assert.That(occupied, Does.Not.Contain(segment), "Rendering links must not contain a cycle.");
                Assert.That(type.GetField("ActiveCount", flags)!.GetValue(segment), Is.GreaterThan(0));
                Assert.That(type.GetField("PreviousRendering", flags)!.GetValue(segment), Is.SameAs(previous));
                occupied.Add(segment);
                previous = segment;
            }
            Assert.That(occupied.Count, Is.EqualTo(expected));
            Assert.That(occupied, Is.EqualTo(capacity.FindAll(segment => (int)type.GetField("ActiveCount", flags)!.GetValue(segment)! > 0)));
        }
    }

    [TestCase(0, 1)]
    [TestCase(0, 31)]
    [TestCase(0, 257)]
    [TestCase(1, 1)]
    [TestCase(1, 31)]
    [TestCase(1, 257)]
    [TestCase(2, 1)]
    [TestCase(2, 31)]
    [TestCase(2, 257)]
    public void ExpandedSegmentRetirementPreservesPcm(int retiredGroup, int blockFrames)
    {
        // Distinct signed signals make accumulation-order changes observable. Retire
        // each capacity segment in turn, refill its slots, then replace the whole epoch.
        var mixer = createMixer();
        var output = new List<float>();
        for (var group = 0; group < 3; group++)
            mixer.SubmitPlayBatch(plays(group, group == retiredGroup ? 100 : 800, 0, 0));
        output.AddRange(BmsPcmTestHelpers.RenderFrames(mixer, 120, blockFrames));
        Assert.That(getOccupiedVoiceSlotCount(mixer), Is.EqualTo(1024));
        mixer.SubmitPlayBatch(plays(retiredGroup, 800, 120, 0));
        mixer.SubmitControl(BmsVoiceCommandType.SetMasterGain, 250, 0, .7f);
        mixer.SubmitControl(BmsVoiceCommandType.Pause, 293, 0);
        mixer.SubmitControl(BmsVoiceCommandType.Resume, 337, 0);
        mixer.SubmitControl(BmsVoiceCommandType.ReplaceEpoch, 450, 1);
        mixer.SubmitPlayBatch(plays(3, 100, 451, 1));
        output.AddRange(BmsPcmTestHelpers.RenderFrames(mixer, 780, blockFrames));
        Assert.That(mixer.RenderedFrames, Is.EqualTo(900));
        Assert.That(mixer.ActiveVoiceCount, Is.Zero);
        Assert.That(getOccupiedVoiceSlotCount(mixer), Is.Zero);
        Assert.That(output.GetRange(800 * 2, 100 * 2), Is.All.Zero);

        var bytes = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(output));
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        // These PCM hashes were captured from the pre-optimisation production DLL,
        // rather than a reference mixer that could share the candidate's ordering bug.
        string[] expectedHashes =
        [
            "7A0CB8F034413046BFCC07A90AED0E91F216392F7C89F47F0FA1A90BF48930A4",
            "9D683CA674819B4E2328A012D62158E7B2D570DA2B5DDDD224252735052093AB",
            "2917E471DCE3214F80CE9B165960B1B92A9988AD5441B21DF40D52550BFBF772",
        ];
        Assert.That(hash, Is.EqualTo(expectedHashes[retiredGroup]));
        TestContext.Progress.WriteLine($"PCM segment trace {retiredGroup}/{blockFrames}: {hash}");
        if (Environment.GetEnvironmentVariable("BMS_PCM_SEGMENT_TRACE_DIRECTORY") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, $"retire-{retiredGroup}-block-{blockFrames}.f32"), bytes.ToArray());
        }

        static BmsVoicePlay[] plays(int group, int frames, long targetFrame, int epoch)
        {
            var samples = new float[frames * 2];
            for (var i = 0; i < samples.Length; i++)
                samples[i] = ((i * 17 + group * 31) % 37 - 18) / 128f;
            var asset = BmsPcmTestHelpers.CreateAsset([new BmsPcmChunk(0, frames, samples)]);
            var result = new BmsVoicePlay[512];
            for (var i = 0; i < result.Length; i++)
                result[i] = new BmsVoicePlay(asset, new BmsTerminationDomain((ushort)(group * 512 + i + 1)),
                    targetFrame, (i % 11 + 1) / 997f, i % 7, epoch, group * 512 + i + 1);
            return result;
        }
    }

    [Test]
    public void FutureControlWaitsForExactFrameAcrossRenderBlocks()
    {
        var asset = createConstantAsset(1000, 0.1f);
        var mixer = createMixer();
        mixer.SubmitPlayBatch([new BmsVoicePlay(asset, new BmsTerminationDomain(1), 0)]);
        mixer.SubmitControl(BmsVoiceCommandType.SetMasterGain, 127, 0, 0.25f);
        mixer.SubmitControl(BmsVoiceCommandType.Pause, 191, 0);
        mixer.SubmitControl(BmsVoiceCommandType.Resume, 257, 0);
        var output = BmsPcmTestHelpers.RenderFrames(mixer, 400, 31);

        var reference = createMixer();
        reference.SubmitPlayBatch([new BmsVoicePlay(asset, new BmsTerminationDomain(1), 0)]);
        var expected = new List<float>();
        expected.AddRange(BmsPcmTestHelpers.RenderFrames(reference, 127));
        reference.SubmitControl(BmsVoiceCommandType.SetMasterGain, 127, 0, 0.25f);
        expected.AddRange(BmsPcmTestHelpers.RenderFrames(reference, 64));
        reference.SubmitControl(BmsVoiceCommandType.Pause, 191, 0);
        expected.AddRange(BmsPcmTestHelpers.RenderFrames(reference, 66));
        reference.SubmitControl(BmsVoiceCommandType.Resume, 257, 0);
        expected.AddRange(BmsPcmTestHelpers.RenderFrames(reference, 143));

        Assert.That(output, Is.EqualTo(expected));
        Assert.That(output[126 * 2], Is.EqualTo(0.1f));
        Assert.That(output[127 * 2], Is.EqualTo(0.025f));
        Assert.That(output[191 * 2], Is.Zero);
        Assert.That(output[256 * 2], Is.Zero);
        Assert.That(output[257 * 2], Is.EqualTo(0.025f));
    }

    [Test]
    public void IndependentVoiceCursorsPreserveExactOutputAcrossChunks()
    {
        var samples = new float[2000];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (float)Math.Sin(i * 0.1) * 0.01f;
        var whole = BmsPcmTestHelpers.CreateAsset([new BmsPcmChunk(0, samples.Length / 2, samples)]);
        var split = new BmsPcmAsset(44100, 2);
        for (var frame = 0; frame < 1000;)
        {
            var count = Math.Min(13 + frame % 11, 1000 - frame);
            split.Publish(new BmsPcmChunk(frame, count, samples.AsSpan(frame * 2, count * 2).ToArray()));
            frame += count;
        }

        split.Complete(1000);
        Assert.That(render(split, 31), Is.EqualTo(render(whole, 127)));

        static float[] render(BmsPcmAsset asset, int blockSize)
        {
            var mixer = new BmsPcmVoiceMixer();
            mixer.SubmitPlayBatch([
                new BmsVoicePlay(asset, new BmsTerminationDomain(1), 0),
                new BmsVoicePlay(asset, new BmsTerminationDomain(2), 0, SourceOffsetFrame: 71),
                new BmsVoicePlay(asset, new BmsTerminationDomain(1), 100, SourceOffsetFrame: 217),
            ]);
            return BmsPcmTestHelpers.RenderFrames(mixer, 1100, blockSize);
        }
    }

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
    public void BatchCoalescingPreservesDomainsFramesAndLastRequest([Values(16, 63, 64, 65, 256)] int count)
    {
        var asset = createAlternatingAsset(500, 0.1f);
        var mixer = createMixer();
        var reference = createMixer();
        var plays = new List<BmsVoicePlay>();
        var expected = new List<BmsVoicePlay>();
        for (var i = 0; i < count - 6; i++)
        {
            var play = new BmsVoicePlay(asset, new BmsTerminationDomain((ushort)(10 + i)), 0, 0.01f);
            plays.Add(play);
            expected.Add(play);
        }

        var domain = new BmsTerminationDomain(1);
        var last = new BmsVoicePlay(asset, domain, 0, 0.3f, SourceOffsetFrame: 7);
        var pitched = new BmsVoicePlay(asset, domain with { Pitch = 1 }, 0, 0.2f);
        var sliced = new BmsVoicePlay(asset, domain with { SliceStartFrame = 1, SliceFrameCount = 200 }, 0, 0.1f);
        var later = new BmsVoicePlay(asset, domain, 20, 0.4f);
        plays.AddRange([
            new BmsVoicePlay(asset, domain, -1, 0.9f),
            new BmsVoicePlay(asset, domain, 0, 0.8f),
            last, pitched, sliced, later,
        ]);
        expected.AddRange([last, pitched, sliced, later]);

        mixer.SubmitPlayBatch(plays.ToArray());
        foreach (var play in expected)
            reference.SubmitPlayBatch([play]);

        Assert.That(BmsPcmTestHelpers.RenderFrames(mixer, 600, 17),
            Is.EqualTo(BmsPcmTestHelpers.RenderFrames(reference, 600, 31)));
        Assert.That(mixer.ActiveVoiceCount, Is.Zero);
    }

    [Test]
    public void LargeBatchFailurePublishesNothingAndNextBatchRemainsIndependent([Values(0, 63)] int invalidIndex)
    {
        var asset = createConstantAsset(500, 0.1f);
        var mixer = createMixer();
        var invalid = createPlays(asset, 64);
        invalid[invalidIndex] = invalid[invalidIndex] with { Asset = null! };
        Assert.Throws<ArgumentNullException>(() => mixer.SubmitPlayBatch(invalid));
        Assert.That(BmsPcmTestHelpers.RenderFrames(mixer, 1), Has.All.Zero);

        var valid = createPlays(asset, 65);
        mixer.SubmitPlayBatch(valid);
        var reference = createMixer();
        BmsPcmTestHelpers.RenderFrames(reference, 1);
        reference.SubmitPlayBatch(valid);
        Assert.That(BmsPcmTestHelpers.RenderFrames(mixer, 500), Is.EqualTo(BmsPcmTestHelpers.RenderFrames(reference, 500)));
    }

    [Test]
    public void ClampedNegativeTargetsPreserveExistingRetriggerOrder()
    {
        var asset = createConstantAsset(500, 0.1f);
        var plays = createPlays(asset, 64);
        plays[^2] = new BmsVoicePlay(asset, new BmsTerminationDomain(1), -1, 0.2f);
        plays[^1] = new BmsVoicePlay(asset, new BmsTerminationDomain(1), -2, 0.3f);
        var mixer = createMixer();
        var reference = createMixer();
        mixer.SubmitPlayBatch(plays);
        foreach (var play in plays)
            reference.SubmitPlayBatch([play]);
        Assert.That(BmsPcmTestHelpers.RenderFrames(mixer, 500), Is.EqualTo(BmsPcmTestHelpers.RenderFrames(reference, 500)));
    }

    [Test]
    public void SinglePlaySubmissionDoesNotAllocateWhenTheConsumerKeepsUp()
    {
        var asset = createConstantAsset(1, 0.1f);
        var mixer = createMixer();
        BmsVoicePlay[] plays = [new(asset, new BmsTerminationDomain(1), 0)];
        var output = new float[2];
        for (var i = 0; i < 100; i++)
        {
            mixer.SubmitPlayBatch(plays);
            mixer.Render(output);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            mixer.SubmitPlayBatch(plays);
            mixer.Render(output);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero);
        Assert.That(output[0], Is.GreaterThan(0));
        Assert.That(mixer.ActiveVoiceCount, Is.Zero);
    }

    [Test]
    public void InvalidSinglePlayPublishesNothingBeforeTheNextValidPlay()
    {
        var asset = createConstantAsset(500, 0.1f);
        var mixer = createMixer();
        Assert.Throws<ArgumentNullException>(() => mixer.SubmitPlayBatch([new BmsVoicePlay(null!, new BmsTerminationDomain(1), 0)]));
        Assert.That(BmsPcmTestHelpers.RenderFrames(mixer, 1), Has.All.Zero);
        mixer.SubmitPlayBatch([new BmsVoicePlay(asset, new BmsTerminationDomain(1), 1)]);

        var reference = createMixer();
        BmsPcmTestHelpers.RenderFrames(reference, 1);
        reference.SubmitPlayBatch([new BmsVoicePlay(asset, new BmsTerminationDomain(1), 1)]);
        Assert.That(BmsPcmTestHelpers.RenderFrames(mixer, 500), Is.EqualTo(BmsPcmTestHelpers.RenderFrames(reference, 500)));
    }

    [Test]
    public void AttackAndTailEnvelopePreserveEverySample([Values(1, 50, 88, 89, 500)] int frames)
    {
        var asset = createConstantAsset(frames, 0.1f);
        var mixer = createMixer();
        mixer.SubmitPlayBatch([new BmsVoicePlay(asset, new BmsTerminationDomain(1), 13)]);
        var output = BmsPcmTestHelpers.RenderFrames(mixer, frames + 20, 17);
        for (var i = 0; i < frames + 20; i++)
        {
            var elapsed = i - 13;
            var gain = elapsed < 0 || elapsed >= frames
                ? 0
                : Math.Min(Math.Clamp((elapsed + 1) / 88f, 0, 1), Math.Clamp((frames - elapsed) / 88f, 0, 1));
            Assert.That(output[i * 2], Is.EqualTo(0.1f * gain), $"Left frame {i}");
            Assert.That(output[i * 2 + 1], Is.EqualTo(0.1f * gain), $"Right frame {i}");
        }
        Assert.That(mixer.ActiveVoiceCount, Is.Zero);
    }

    [Test]
    public void RetriggerEnvelopePreservesSamplesAcrossAttackBoundary([Values(1, 43, 87, 88, 89, 300)] int retriggerFrame)
    {
        const int asset_frames = 500;
        var asset = createConstantAsset(asset_frames, 0.1f);
        var mixer = createMixer();
        var domain = new BmsTerminationDomain(1);
        mixer.SubmitPlayBatch([new BmsVoicePlay(asset, domain, 0), new BmsVoicePlay(asset, domain, retriggerFrame)]);
        var output = BmsPcmTestHelpers.RenderFrames(mixer, asset_frames + retriggerFrame + 10, 31);
        var gainAtRetrigger = envelope(retriggerFrame);
        for (var frame = 0; frame < output.Length / 2; frame++)
        {
            var oldGain = envelope(frame);
            if (frame >= retriggerFrame)
                oldGain = Math.Min(oldGain, gainAtRetrigger * (1 - Math.Clamp((frame - retriggerFrame) / 88f, 0, 1)));
            var newGain = envelope(frame - retriggerFrame);
            var expected = 0.1f * oldGain + 0.1f * newGain;
            Assert.That(output[frame * 2], Is.EqualTo(expected), $"Left frame {frame}");
            Assert.That(output[frame * 2 + 1], Is.EqualTo(expected), $"Right frame {frame}");
        }
        Assert.That(mixer.ActiveVoiceCount, Is.Zero);

        static float envelope(int sourceFrame) => sourceFrame < 0 || sourceFrame >= asset_frames
            ? 0
            : Math.Min(Math.Clamp((sourceFrame + 1) / 88f, 0, 1), Math.Clamp((asset_frames - sourceFrame) / 88f, 0, 1));
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

    [TestCase(1f)]
    [TestCase(0.5f)]
    [TestCase(0.9999989f)]
    [TestCase(0.999999f)]
    [TestCase(0.99999994f)]
    public void LimiterPreservesExactOutputThroughRecovery(float initialGain)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var mixer = createMixer();
        typeof(BmsPcmVoiceMixer).GetField("limiterGain", flags)!.SetValue(mixer, initialGain);
        var limit = typeof(BmsPcmVoiceMixer).GetMethod("applyLimiter", flags)!.CreateDelegate<Limiter>(mixer);
        var referenceGain = initialGain;
        var random = new Random(5317);
        float[] boundaryPeaks = [0, -0f, float.BitDecrement(0.98f), 0.98f, float.BitIncrement(0.98f), 1, 4];

        // Alternate overloads with enough quiet frames to exercise both recovery
        // and the fully recovered path, including peaks adjacent to the ceiling.
        for (var frame = 0; frame < 10000; frame++)
        {
            var peak = frame < boundaryPeaks.Length ? boundaryPeaks[frame]
                : frame % 3000 == 0 ? 4
                : frame % 3000 < 2500 ? (float)random.NextDouble() * 0.5f
                : boundaryPeaks[frame % boundaryPeaks.Length];
            var left = frame % 2 == 0 ? peak : -peak * 0.25f;
            var right = frame % 2 == 0 ? -peak * 0.25f : peak;
            var expectedLeft = left;
            var expectedRight = right;
            applyReferenceLimiter(ref expectedLeft, ref expectedRight, ref referenceGain);
            limit(ref left, ref right);

            Assert.That(BitConverter.SingleToInt32Bits(left), Is.EqualTo(BitConverter.SingleToInt32Bits(expectedLeft)), $"Left frame {frame}");
            Assert.That(BitConverter.SingleToInt32Bits(right), Is.EqualTo(BitConverter.SingleToInt32Bits(expectedRight)), $"Right frame {frame}");
        }

        Assert.That((float)typeof(BmsPcmVoiceMixer).GetField("limiterGain", flags)!.GetValue(mixer)!, Is.EqualTo(referenceGain));
    }

    private delegate void Limiter(ref float left, ref float right);

    [TestCase(1)]
    [TestCase(31)]
    [TestCase(512)]
    public void RenderOverwritesDirtyOutputAcrossPlaybackChanges(int blockFrames)
    {
        const int frames = 2000;
        var asset = createConstantAsset(500, 0.1f);
        var mixer = prepareMixer();
        var reference = prepareMixer();
        var expected = BmsPcmTestHelpers.RenderFrames(reference, frames, blockFrames);
        var output = new float[frames * 2 + 2];
        Array.Fill(output, float.NaN);
        output[0] = 17;
        output[^1] = 19;
        for (var frame = 0; frame < frames; frame += blockFrames)
            mixer.Render(output.AsSpan(1 + frame * 2, Math.Min(blockFrames, frames - frame) * 2));

        Assert.That(output.AsSpan(1, frames * 2).ToArray(), Is.EqualTo(expected));
        Assert.That(output[0], Is.EqualTo(17));
        Assert.That(output[^1], Is.EqualTo(19));
        Assert.That(mixer.RenderedFrames, Is.EqualTo(frames));
        Assert.That(mixer.ActiveVoiceCount, Is.Zero);

        BmsPcmVoiceMixer prepareMixer()
        {
            var prepared = createMixer();
            prepared.SubmitPlayBatch([new BmsVoicePlay(asset, new BmsTerminationDomain(1), 7)]);
            prepared.SubmitControl(BmsVoiceCommandType.Pause, 127, 0);
            prepared.SubmitControl(BmsVoiceCommandType.Resume, 191, 0);
            prepared.SubmitControl(BmsVoiceCommandType.SetMasterGain, 299, 0, 0);
            prepared.SubmitControl(BmsVoiceCommandType.ReplaceEpoch, 400, 1);
            prepared.SubmitControl(BmsVoiceCommandType.SetMasterGain, 401, 1, 1);
            prepared.SubmitPlayBatch([new BmsVoicePlay(asset, new BmsTerminationDomain(1), 401, Epoch: 1)]);
            return prepared;
        }
    }

    [Test]
    public void EmptyAndIncompleteOutputLeaveStateAndBufferUntouched()
    {
        var mixer = createMixer();
        mixer.SubmitPlayBatch([new BmsVoicePlay(createConstantAsset(500, 0.1f), new BmsTerminationDomain(1), 0)]);
        mixer.Render([]);
        float[] incomplete = [17, 19, 23];
        Assert.Throws<ArgumentException>(() => mixer.Render(incomplete));
        Assert.That(incomplete, Is.EqualTo(new float[] { 17, 19, 23 }));
        Assert.That(mixer.RenderedFrames, Is.Zero);
        Assert.That(mixer.ActiveVoiceCount, Is.Zero);
        var output = BmsPcmTestHelpers.RenderFrames(mixer, 1);
        Assert.That(output[0], Is.GreaterThan(0));
        Assert.That(mixer.ActiveVoiceCount, Is.EqualTo(1));
    }

    [Test]
    public void LimiterPreservesNonFiniteAndSignedZeroInput()
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var mixer = createMixer();
        var limit = typeof(BmsPcmVoiceMixer).GetMethod("applyLimiter", flags)!.CreateDelegate<Limiter>(mixer);
        var referenceGain = 1f;
        float[] values = [0, -0f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue, -float.MaxValue, 0.25f];
        foreach (var value in values)
        {
            var left = value;
            var right = -value;
            var expectedLeft = left;
            var expectedRight = right;
            applyReferenceLimiter(ref expectedLeft, ref expectedRight, ref referenceGain);
            limit(ref left, ref right);
            Assert.That(BitConverter.SingleToInt32Bits(left), Is.EqualTo(BitConverter.SingleToInt32Bits(expectedLeft)));
            Assert.That(BitConverter.SingleToInt32Bits(right), Is.EqualTo(BitConverter.SingleToInt32Bits(expectedRight)));
        }
    }

    [TestCase(1)]
    [TestCase(31)]
    [TestCase(512)]
    public void RenderedLimiterOutputMatchesScalarReference(int blockFrames)
    {
        const int frames = 10000;
        var samples = new float[frames * 2];
        for (var frame = 0; frame < frames; frame++)
        {
            var amplitude = frame % 3000 < 200 ? 4 : 0.05f;
            samples[frame * 2] = amplitude * (float)Math.Sin(frame * 0.07);
            samples[frame * 2 + 1] = amplitude * (float)Math.Cos(frame * 0.11);
        }
        var asset = BmsPcmTestHelpers.CreateAsset([new BmsPcmChunk(0, frames, samples)]);
        var mixer = createMixer();
        mixer.SubmitPlayBatch([new BmsVoicePlay(asset, new BmsTerminationDomain(1), 0)]);
        var output = BmsPcmTestHelpers.RenderFrames(mixer, frames, blockFrames);
        var referenceGain = 1f;
        for (var frame = 0; frame < frames; frame++)
        {
            var envelope = Math.Min(Math.Clamp((frame + 1) / 88f, 0, 1), Math.Clamp((frames - frame) / 88f, 0, 1));
            var left = samples[frame * 2] * envelope;
            var right = samples[frame * 2 + 1] * envelope;
            applyReferenceLimiter(ref left, ref right, ref referenceGain);
            Assert.That(BitConverter.SingleToInt32Bits(output[frame * 2]), Is.EqualTo(BitConverter.SingleToInt32Bits(left)), $"Left frame {frame}");
            Assert.That(BitConverter.SingleToInt32Bits(output[frame * 2 + 1]), Is.EqualTo(BitConverter.SingleToInt32Bits(right)), $"Right frame {frame}");
        }
        Assert.That(mixer.ActiveVoiceCount, Is.Zero);
    }

    private static void applyReferenceLimiter(ref float left, ref float right, ref float gain)
    {
        // Preserve the pre-optimisation scalar formula as an independent curve reference.
        const float ceiling = 0.98f;
        var peak = Math.Max(Math.Abs(left), Math.Abs(right));
        var requiredGain = peak > ceiling ? ceiling / peak : 1;
        gain = requiredGain < 1 ? Math.Min(gain, requiredGain) : Math.Min(1, gain + 1 / (44100 * 50f / 1000));
        if (gain < 0.999999f)
        {
            left *= gain;
            right *= gain;
        }

        var outputPeak = Math.Max(Math.Abs(left), Math.Abs(right));
        if (outputPeak > ceiling)
        {
            var correction = ceiling / outputPeak;
            left = Math.Clamp(left * correction, -ceiling, ceiling);
            right = Math.Clamp(right * correction, -ceiling, ceiling);
        }
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
