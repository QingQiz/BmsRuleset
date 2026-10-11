using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
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
    public void RetriggerAndVoiceCommandsKeepIdentityAfterCompaction(int blockFrames)
    {
        var mixer = new BmsPcmVoiceMixer();
        var commands = (BmsVoiceCommandQueue)typeof(BmsPcmVoiceMixer).GetField("commands", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(mixer)!;
        var output = new List<float>();

        // Mixed lengths retire non-final slots in both capacity segments. Later commands
        // must still target the voice that moved into each slot, including draining voices.
        mixer.SubmitPlayBatch(plays(0, 1056, 0, 0));
        output.AddRange(BmsPcmTestHelpers.RenderFrames(mixer, 173, blockFrames));
        mixer.SubmitPlayBatch(plays(1, 137, 173, 0));
        commands.Enqueue(new BmsVoiceCommand(BmsVoiceCommandType.SetVoiceGain, 193, 0, Value: .17f, VoiceId: 999));
        commands.Enqueue(new BmsVoiceCommand(BmsVoiceCommandType.StopVoice, 227, 0, VoiceId: 1025));
        commands.Enqueue(new BmsVoiceCommand(BmsVoiceCommandType.SetVoiceGain, 263, 0, Value: .31f, VoiceId: 1081));
        commands.Enqueue(new BmsVoiceCommand(BmsVoiceCommandType.StopVoice, 281, 0, VoiceId: 1081));
        mixer.SubmitControl(BmsVoiceCommandType.Pause, 293, 0);
        mixer.SubmitControl(BmsVoiceCommandType.Resume, 337, 0);
        output.AddRange(BmsPcmTestHelpers.RenderFrames(mixer, 227, blockFrames));
        mixer.SubmitPlayBatch(plays(2, 81, 400, 0));
        mixer.SubmitControl(BmsVoiceCommandType.ReplaceEpoch, 450, 1);
        mixer.SubmitPlayBatch(plays(3, 49, 451, 1));
        commands.Enqueue(new BmsVoiceCommand(BmsVoiceCommandType.SetVoiceGain, 479, 1, Value: .41f, VoiceId: 3097));
        commands.Enqueue(new BmsVoiceCommand(BmsVoiceCommandType.StopVoice, 503, 1, VoiceId: 3073));
        output.AddRange(BmsPcmTestHelpers.RenderFrames(mixer, 900, blockFrames));

        Assert.That(mixer.RenderedFrames, Is.EqualTo(1300));
        Assert.That(mixer.ActiveVoiceCount, Is.Zero);
        Assert.That(output.GetRange(1200 * 2, 100 * 2), Is.All.Zero);
        Assert.That(output.Exists(value => value != 0), Is.True);
        var bytes = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(output));
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        // Captured with the frozen production DLL before changing voice storage.
        Assert.That(hash, Is.EqualTo("33DFAE4E3B8AF5A541E1410316A8661CCA3057E76EE998FBDB737D3B66756E02"));
        TestContext.Progress.WriteLine($"Voice identity trace {blockFrames}: {hash}");
        if (Environment.GetEnvironmentVariable("BMS_PCM_IDENTITY_TRACE_DIRECTORY") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, $"identity-block-{blockFrames}.f32"), bytes.ToArray());
        }

        // Record the actual runtime stride separately from PCM correctness; a smaller
        // array element is evidence of working-set reduction, not a CPU gain by itself.
        var voiceType = typeof(BmsPcmVoiceMixer).GetNestedType("BmsPcmVoice", BindingFlags.NonPublic)!;
        var stride = typeof(BmsPcmVoiceIdentityTraceTest).GetMethod(nameof(sizeOf), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(voiceType).Invoke(null, null);
        TestContext.Progress.WriteLine($"Rendering voice stride: {stride} bytes");

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
                var domain = new BmsTerminationDomain((ushort)(i % 353 + 1), i % 3, i % 5, 131 + i % 7 * 97);
                result[i] = new BmsVoicePlay(asset, domain, targetFrame, (i % 11 + 1) / 127f, i % 13, epoch, group * 1024 + i + 1);
            }
            return result;
        }
    }

    private static int sizeOf<T>() => Unsafe.SizeOf<T>();
}
