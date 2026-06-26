using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.IO.Stores;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Audio;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[HeadlessTest]
public partial class TestBmsSampleStretcher : TestScene
{
    [Resolved]
    private AudioManager audioManager { get; set; } = null!;

    [Test]
    public void Stretch_HalvesLengthAtDoubleRate()
    {
        // 1 second of 44100 Hz mono float silence -> 44100 samples * 4 bytes = 176400 bytes.
        var pcm = new byte[44100 * 4];
        var wav = BmsWavEncoder.Encode(pcm, 44100, 1);
        var dict = new Dictionary<string, byte[]> { ["silence.wav"] = wav };
        var source = new ResourceStore<byte[]>(new MemoryByteStore(dict));
        source.AddExtension("wav");

        using var stretcher = new BmsSampleStretcher(audioManager);
        var stretched = stretcher.Stretch(source, "silence.wav", 2.0);

        Assert.That(stretched, Is.Not.Null);

        // SampleBassFactory loads its BASS sample async on the audio thread (EnqueueAction), so
        // Length/IsLoaded stay 0/false until the next audio-thread pump. Poll for load before
        // reading length: the immediate read passed locally but is racy under CI scheduling.
        var deadline = Stopwatch.StartNew();
        while (!stretched!.IsLoaded && deadline.ElapsedMilliseconds < 2000)
            Thread.Sleep(10);

        Assert.That(stretched!.IsLoaded, Is.True);
        Assert.That(stretched!.Length, Is.EqualTo(500).Within(60));
    }
}
