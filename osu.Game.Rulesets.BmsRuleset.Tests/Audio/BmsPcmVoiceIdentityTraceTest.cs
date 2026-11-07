using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing.Pcm;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[TestFixture]
public class BmsPcmVoiceIdentityTraceTest
{
    [TestCase(1)]
    [TestCase(31)]
    [TestCase(257)]
    public void RetriggersKeepIdentityAfterCompaction(int blockFrames)
    {
        var (mixer, output) = renderTrace(blockFrames);
        var (_, reference) = renderTrace(1);
        Assert.That(output, Is.EqualTo(reference));

        Assert.That(mixer.RenderedFrames, Is.EqualTo(1300));
        Assert.That(mixer.ActiveVoiceCount, Is.Zero);
        Assert.That(output.GetRange(1200 * 2, 100 * 2), Is.All.Zero);
        Assert.That(output.Exists(value => value != 0), Is.True);
        var bytes = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(output));
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        TestContext.Progress.WriteLine($"Voice identity trace {blockFrames}: {hash}");
        if (Environment.GetEnvironmentVariable("BMS_PCM_IDENTITY_TRACE_DIRECTORY") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, $"identity-block-{blockFrames}.f32"), bytes.ToArray());
        }
    }

    private static (BmsPcmVoiceMixer Mixer, List<float> Output) renderTrace(int blockFrames)
    {
        var mixer = new BmsPcmVoiceMixer();
        var output = new List<float>();

        // Mixed lengths retire non-final slots in both capacity segments. Retriggers must
        // still find samples that moved into those slots, including draining voices.
        mixer.SubmitPlayBatch(plays(0, 1056, 0, 0));
        output.AddRange(BmsPcmTestHelpers.RenderFrames(mixer, 173, blockFrames));
        mixer.SubmitPlayBatch(plays(1, 137, 173, 0));
        mixer.SubmitControl(BmsVoiceCommandType.Pause, 293, 0);
        mixer.SubmitControl(BmsVoiceCommandType.Resume, 337, 0);
        output.AddRange(BmsPcmTestHelpers.RenderFrames(mixer, 227, blockFrames));
        mixer.SubmitPlayBatch(plays(2, 81, 400, 0));
        mixer.SubmitControl(BmsVoiceCommandType.ReplaceEpoch, 450, 1);
        mixer.SubmitPlayBatch(plays(3, 49, 451, 1));
        output.AddRange(BmsPcmTestHelpers.RenderFrames(mixer, 900, blockFrames));

        return (mixer, output);

        static BmsVoicePlay[] plays(int group, int count, long targetFrame, int epoch)
        {
            var result = new BmsVoicePlay[count];
            for (var i = 0; i < count; i++)
            {
                var frames = 137 + i % 7 * 101;
                var samples = new float[frames * 2];
                for (var sample = 0; sample < samples.Length; sample++)
                    samples[sample] = ((sample * 17 + i * 31 + group * 11) % 37 - 18) / 512f;
                var asset = BmsPcmTestHelpers.CreateAsset([new(0, frames, samples)]);
                var sampleKey = (ushort)(i + 1);
                result[i] = new BmsVoicePlay(asset, sampleKey, targetFrame, (i % 11 + 1) / 127f, i % 13, epoch);
            }
            return result;
        }
    }
}
