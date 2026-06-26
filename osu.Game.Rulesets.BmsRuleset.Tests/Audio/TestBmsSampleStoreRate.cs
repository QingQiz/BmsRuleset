using System;
using System.IO;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Audio;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[HeadlessTest]
public partial class TestBmsSampleStoreRate : TestScene
{
    private string tempDir = null!;
    private BmsSampleStore store = null!;

    [Resolved]
    private AudioManager audioManager { get; set; } = null!;

    [Test]
    public void PreStretch_HalvesCachedSampleLength()
    {
        // The whole lifecycle runs as AddStep steps on the update thread. osu-framework pumps a
        // TestScene's step sequence in AfterTest (RunTestBlocking), which runs AFTER NUnit's
        // [SetUp]/[TearDown]: doing file I/O + Add() in [SetUp] either throws
        // InvalidThreadForMutationException (Add mutates a Loaded scene from the NUnit thread) or
        // has its temp dir deleted by [TearDown] before the steps pump. Keeping setup, assertions
        // and cleanup all inside the step sequence sidesteps both.
        AddStep("create temp dir + store", () =>
        {
            tempDir = Directory.CreateTempSubdirectory("bmsrate").FullName;
            // 1 second of 44100 Hz mono float silence.
            File.WriteAllBytes(Path.Combine(tempDir, "sine.wav"), BmsWavEncoder.Encode(new byte[44100 * 4], 44100, 1));
            Add(store = new BmsSampleStore(new[] { "sine.wav" }, tempDir, rate: 2.0));
        });
        AddUntilStep("wait for store load", () => store.IsLoaded);
        // The stretched ISample is created during load() but its BASS sample loads async on the
        // audio thread; wait for it before reading Length (racy-assertion lesson from Task 3).
        AddUntilStep("wait for stretched sample load", () => store.Get("sine.wav")?.IsLoaded == true);
        AddAssert("stretched sample is ~half length", () =>
        {
            var s = store.Get("sine.wav");
            return s != null && Math.Abs(s.Length - 500) < 80;
        });
        // The store stays parented; the framework disposes it (and its BmsSampleStretcher) on
        // scene teardown. Source files were read into memory during load(), so the dir is safe to
        // delete now.
        AddStep("cleanup temp dir", () =>
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        });
    }
}
