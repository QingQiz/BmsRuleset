using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[TestFixture]
public class BmsPcmExpandedCapacityTest
{
    [TestCase(512, 0)]
    [TestCase(512, 1)]
    [TestCase(512, 8)]
    [TestCase(2048, 0)]
    [TestCase(2048, 1)]
    [TestCase(2048, 8)]
    [TestCase(8192, 0)]
    [TestCase(8192, 1)]
    [TestCase(8192, 8)]
    public void SparsePlaybackMatchesUnexpandedCapacity(int peakVoices, int voices)
    {
        var samples = new float[6000 * 2];
        for (var frame = 0; frame < 6000; frame++)
        {
            samples[frame * 2] = (float)Math.Sin(frame * .071) * .003f;
            samples[frame * 2 + 1] = (float)Math.Cos(frame * .053) * -.002f;
        }

        var asset = BmsPcmTestHelpers.CreateAsset([new BmsPcmChunk(0, 6000, samples)]);
        var expanded = BmsPcmExpandedCapacityWorkload.Prepare(peakVoices);
        var reference = BmsPcmExpandedCapacityWorkload.Prepare(512);
        Assert.That(expanded.Peak.OccupiedSegments, Is.EqualTo(peakVoices / 512));
        Assert.That(expanded.Retired.OccupiedSegments, Is.Zero);
        Assert.That(expanded.Mixer.RenderedFrames, Is.EqualTo(512));
        BmsPcmExpandedCapacityWorkload.SubmitSparseVoices(expanded.Mixer, asset, voices);
        BmsPcmExpandedCapacityWorkload.SubmitSparseVoices(reference.Mixer, asset, voices);

        // Different callback boundaries check that capacity history does not change
        // the attack, cursor progression, mixing order, or subsequent retirement.
        var actual = BmsPcmTestHelpers.RenderFrames(expanded.Mixer, 6500, 257);
        var expected = BmsPcmTestHelpers.RenderFrames(reference.Mixer, 6500, 113);
        var bytes = MemoryMarshal.AsBytes(actual.AsSpan());
        Assert.That(bytes.SequenceEqual(MemoryMarshal.AsBytes(expected.AsSpan())), Is.True);
        Assert.That(expanded.Mixer.ActiveVoiceCount, Is.Zero);
        Assert.That(BmsPcmExpandedCapacityWorkload.ReadCapacity(expanded.Mixer).OccupiedVoices, Is.Zero);
        Assert.That(BmsPcmExpandedCapacityWorkload.ReadCapacity(expanded.Mixer).Segments, Is.EqualTo(peakVoices / 512));
        Assert.That(Array.Exists(actual, value => value != 0), Is.EqualTo(voices > 0));
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        // Captured from the pre-index production DLL and compared byte-for-byte
        // with the candidate, so a shared regression cannot redefine the reference.
        var expectedHash = voices switch
        {
            0 => "ABE30C240726FA05E7B546969B8A5C47B7AFBB02F946B730790C92B16A456E8A",
            1 => "7BC6E6D2176AEB5A9227D53926DDD05C023F473B23FDAA0FEC6673EB7AA1F62E",
            8 => "4C5B2A78465D37EC28A452D20BB6A38F24E366D56F4840DF59C62F876FFCC897",
            _ => throw new ArgumentOutOfRangeException(nameof(voices)),
        };
        Assert.That(hash, Is.EqualTo(expectedHash));
        TestContext.Progress.WriteLine($"Expanded sparse PCM {peakVoices}/{voices}: {hash}");
        if (Environment.GetEnvironmentVariable("BMS_PCM_EXPANDED_TRACE_DIRECTORY") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, $"peak-{peakVoices}-voices-{voices}.f32"), bytes.ToArray());
        }
    }
}
