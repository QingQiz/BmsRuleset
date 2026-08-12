using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using osu.Framework;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing.Pcm;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Native;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[HeadlessTest]
public partial class BmsSampleStoreTest : TestScene
{
    private const string silent_flac = "ZkxhQwAAACICQAJAAAAMAAAMAfQA8AAAAFDLQV4FuFvjFJSuG8IzvrWLhAAALAwAAABMYXZmNjEuNy4xMDABAAAAFAAAAGVuY29kZXI9TGF2ZjYxLjcuMTAw//hkCABPCQAAAHyn";

    private string tempDir = null!;
    private BmsSampleStore store = null!;

    [Test]
    public void NativeMixerPatchInstallsOnDesktopBassPlatforms()
    {
        if (!BmsAudioPlatform.SupportsNativeBass)
            Assert.Ignore("The native BMS mixer patch is only enabled on Windows and Linux.");

        AddStep("create sample + store", () =>
        {
            createWav("native-mixer.wav", 1);
            Add(store = new BmsSampleStore(new Dictionary<ushort, string> { { 1, "native-mixer.wav" } }, tempDir));
        });
        AddUntilStep("wait for store load", () => store.IsLoaded);
        AddAssert("PCM backend initialised", () => store.DiagnosticMixer != null);
        AddAssert("PCM float mixer patch installed", () => BmsPcmMixerPatcher.IsInstalled);
        addCleanupSteps();
    }

    [Test]
    public void ScheduledSampleLoadsTenSecondsBeforeUse()
    {
        var manualClock = new ManualClock();

        AddStep("create scheduled sample + store", () =>
        {
            createWav("scheduled.wav", 1);
            store = new BmsSampleStore(
                new Dictionary<ushort, string> { { 1, "scheduled.wav" } },
                tempDir,
                sampleUsages: [new BmsSampleUsage(1, 10_001)])
            {
                Clock = new FramedClock(manualClock),
            };
            Add(store);
        });
        AddUntilStep("wait for store load", () => store.IsLoaded);
        AddAssert("sample outside prefetch window", () => !isSampleReady(1));
        AddStep("enter prefetch window", () => manualClock.CurrentTime = 1);
        AddUntilStep("sample loads in prefetch window", () => isSampleReady(1));
        addCleanupSteps();
    }

    [Test]
    public void ScheduledSampleUsesEarliestCandidateTime()
    {
        AddStep("create early-candidate sample + store", () =>
        {
            createWav("early-candidate.wav", 1);
            store = new BmsSampleStore(
                new Dictionary<ushort, string> { { 1, "early-candidate.wav" } },
                tempDir,
                sampleUsages: [new BmsSampleUsage(1, 100_000, CandidateStartTime: 1_000, CandidateEndTime: 100_280)]);
            Add(store);
        });
        AddUntilStep("wait for store load", () => store.IsLoaded);
        AddUntilStep("sample loaded for early candidate", () => isSampleReady(1));
        addCleanupSteps();
    }

    [Test]
    public void ScheduledSampleRemainsLoadedAfterLastUse()
    {
        var manualClock = new ManualClock();

        AddStep("create expiring sample + store", () =>
        {
            createWav("retained.wav", 1);
            store = new BmsSampleStore(
                new Dictionary<ushort, string> { { 1, "retained.wav" } },
                tempDir,
                sampleUsages: [new BmsSampleUsage(1, 100)])
            {
                Clock = new FramedClock(manualClock),
            };
            Add(store);
        });
        AddUntilStep("wait for store load", () => store.IsLoaded);
        AddAssert("scheduled sample loaded during store load", () => isSampleReady(1));
        AddStep("advance past last use", () => manualClock.CurrentTime = 60_000);
        AddUntilStep("expired PCM lease is released", () => !store.IsSampleReady(1));
        addCleanupSteps();
    }

    [Test]
    public void PreloadWaitsForSamples()
    {
        AddStep("create sample + store", () =>
        {
            createWav("sine.wav", 1);
            Add(store = new BmsSampleStore(new Dictionary<ushort, string> { { 1, "sine.wav" } }, tempDir));
        });
        AddUntilStep("wait for store load", () => store.IsLoaded);
        AddAssert("sample loaded during store load", () => isSampleReady(1) && sampleLength(1) > 0);
        addCleanupSteps();
    }

    [Test]
    public void WavDefinitionFallsBackToFlac()
    {
        AddStep("create FLAC sample + store", () =>
        {
            tempDir = Directory.CreateTempSubdirectory("bmstracks").FullName;
            File.WriteAllBytes(Path.Combine(tempDir, "sine.flac"), Convert.FromBase64String(silent_flac));
            Add(store = new BmsSampleStore(new Dictionary<ushort, string> { { 1, "sine.wav" } }, tempDir));
        });
        AddUntilStep("wait for store load", () => store.IsLoaded);
        AddAssert("FLAC fallback sample loaded", () => isSampleReady(1) && sampleLength(1) > 0);
        addCleanupSteps();
    }

    [Test]
    public void PreloadCompletesAcrossBatches()
    {
        AddStep("create batched sample store", () =>
        {
            createWav("batched.wav", 1);
            var definitions = new Dictionary<ushort, string>();

            for (ushort sampleKey = 1; sampleKey <= 17; sampleKey++)
                definitions.Add(sampleKey, "batched.wav");

            Add(store = new BmsSampleStore(definitions, tempDir));
        });
        AddUntilStep("wait for batched store load", () => store.IsLoaded);
        AddAssert("all batches loaded", () =>
        {
            for (ushort sampleKey = 1; sampleKey <= 17; sampleKey++)
            {
                if (!isSampleReady(sampleKey) || sampleLength(sampleKey) <= 0)
                    return false;
            }

            return true;
        });
        addCleanupSteps();
    }

    [Test]
    public void SameFileWithDifferentKeysCreatesIndependentVoices()
    {
        var startedVoices = 0L;

        AddStep("create shared sample + store", () =>
        {
            createWav("shared.wav", 6);
            Add(store = new BmsSampleStore(new Dictionary<ushort, string>
            {
                { 1, "shared.wav" },
                { 2, "shared.wav" },
            }, tempDir));
        });
        AddUntilStep("wait for store load", () => store.IsLoaded);
        AddAssert("shared PCM is decoded once", () => store.DiagnosticSnapshot.Cache.LoadedAssets == 1);
        AddStep("remember voice count", () => startedVoices = store.DiagnosticSnapshot.Audio.StartedVoices);
        AddStep("play both keys", () =>
        {
            store.Play(1);
            store.Play(2);
        });
        AddUntilStep("different keys overlap", () =>
            store.DiagnosticSnapshot.Audio.StartedVoices >= startedVoices + 2 && store.DiagnosticSnapshot.Audio.ActiveVoices >= 2);
        addCleanupSteps();
    }

    [Test]
    public void LiveKeysoundsWaitForBatchFlush()
    {
        var startedVoices = 0L;

        AddStep("create live sample + store", () =>
        {
            createWav("live.wav", 6);
            Add(store = new BmsSampleStore(new Dictionary<ushort, string>
            {
                { 1, "live.wav" },
                { 2, "live.wav" },
            }, tempDir));
        });
        AddUntilStep("wait for store load", () => store.IsLoaded);
        AddStep("remember voice count", () => startedVoices = store.DiagnosticSnapshot.Audio.StartedVoices);
        AddStep("queue live chord", () =>
        {
            store.QueueLivePlay(1);
            store.QueueLivePlay(2);
        });
        AddAssert("samples wait for submit", () => store.DiagnosticSnapshot.Audio.StartedVoices == startedVoices);
        AddStep("submit live chord", () => store.SubmitLivePlayBatch());
        AddUntilStep("samples start together", () =>
            store.DiagnosticSnapshot.Audio.StartedVoices >= startedVoices + 2 && store.DiagnosticSnapshot.Audio.ActiveVoices >= 2);
        addCleanupSteps();
    }

    [Test]
    public void ZeroKeyIsPlayableForLandmines()
    {
        var startedVoices = 0L;

        AddStep("create landmine sample + store", () =>
        {
            createWav("landmine.wav", 1);
            Add(store = new BmsSampleStore(new Dictionary<ushort, string> { { 0, "landmine.wav" } }, tempDir));
        });
        AddUntilStep("wait for store load", () => store.IsLoaded);
        AddStep("remember voice count", () => startedVoices = store.DiagnosticSnapshot.Audio.StartedVoices);
        AddStep("play key zero", () => store.Play(0));
        AddUntilStep("landmine sample is playing", () =>
            store.DiagnosticSnapshot.Audio.StartedVoices > startedVoices && store.DiagnosticSnapshot.Audio.ActiveVoices > 0);
        addCleanupSteps();
    }

    [Test]
    public void SameKeyRetriggersWithNewVoice()
    {
        var startedVoices = 0L;

        AddStep("create sample + store", () =>
        {
            createWav("retrigger.wav", 6);
            Add(store = new BmsSampleStore(new Dictionary<ushort, string> { { 1, "retrigger.wav" } }, tempDir));
        });
        AddUntilStep("wait for store load", () => store.IsLoaded);
        AddStep("remember voice count", () => startedVoices = store.DiagnosticSnapshot.Audio.StartedVoices);
        AddStep("start from offset", () => store.Play(1, offset: 1000));
        AddUntilStep("offset voice started", () => store.DiagnosticSnapshot.Audio.StartedVoices >= startedVoices + 1);
        AddStep("retrigger same key", () => store.Play(1));
        AddUntilStep("same key starts another logical voice", () => store.DiagnosticSnapshot.Audio.StartedVoices >= startedVoices + 2);
        addCleanupSteps();
    }

    [Test]
    public void RateUsesFixedRateTempo()
    {
        AddStep("create sample + rate store", () =>
        {
            createWav("rate.wav", 1);
            Add(store = new BmsSampleStore(new Dictionary<ushort, string> { { 1, "rate.wav" } }, tempDir, rate: 2));
        });
        AddUntilStep("wait for store load", () => store.IsLoaded);
        AddAssert("fixed-rate backend selected", () => store.DiagnosticMixer != null);
        AddAssert("sample keeps source length", () => sampleLength(1) is >= 900 and <= 1100);
        addCleanupSteps();
    }

    [Test]
    public void InvalidAudioDoesNotWaitForTimeout()
    {
        AddStep("create invalid sample + store", () =>
        {
            tempDir = Directory.CreateTempSubdirectory("bmstracks").FullName;
            File.WriteAllBytes(Path.Combine(tempDir, "invalid.wav"), [0, 1, 2, 3]);
            Add(store = new BmsSampleStore(new Dictionary<ushort, string> { { 1, "invalid.wav" } }, tempDir));
        });
        AddUntilStep("store loads without timeout", () => store.IsLoaded);
        AddAssert("invalid sample is unavailable", () => !store.HasSampleDefinition(1));
        addCleanupSteps();
    }

    [Test]
    public void EmptySampleDefinitionIsUnavailable()
    {
        AddStep("create store with empty definition", () =>
            Add(store = new BmsSampleStore(new Dictionary<ushort, string> { { 1, string.Empty } }, tempDir)));
        AddUntilStep("wait for store load", () => store.IsLoaded);
        AddAssert("empty definition is unavailable", () => !store.HasSampleDefinition(1));
        addCleanupSteps();
    }

    [Test]
    public void DisposalReleasesOwnedPcmAssets()
    {
        AddStep("create sample + store", () =>
        {
            createWav("dispose.wav", 1);
            Add(store = new BmsSampleStore(new Dictionary<ushort, string> { { 1, "dispose.wav" } }, tempDir));
        });
        AddUntilStep("wait for store load", () => store.IsLoaded);
        AddStep("expire store", () => store.Expire());
        AddUntilStep("owned audio is disposed", () => store.DiagnosticSnapshot.Cache.ResidentPcmBytes == 0);
        AddUntilStep("cleanup temp dir", () =>
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);

                return true;
            }
            catch (IOException)
            {
                return false;
            }
        });
    }

    private bool isSampleReady(ushort sampleKey) => store.IsSampleReady(sampleKey);

    private double sampleLength(ushort sampleKey) => store.GetSampleLength(sampleKey);

    private void createWav(string filename, int seconds)
    {
        const int sample_rate = 44100;
        const short channels = 1;
        const short bits_per_sample = 16;

        tempDir = Directory.CreateTempSubdirectory("bmstracks").FullName;
        var dataSize = sample_rate * channels * bits_per_sample / 8 * seconds;

        using var stream = File.Create(Path.Combine(tempDir, filename));
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sample_rate);
        writer.Write(sample_rate * channels * bits_per_sample / 8);
        writer.Write((short)(channels * bits_per_sample / 8));
        writer.Write(bits_per_sample);
        writer.Write("data"u8);
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
    }

    private void addCleanupSteps()
    {
        AddStep("expire store", () => store.Expire());
        AddUntilStep("cleanup temp dir", () =>
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);

                return true;
            }
            catch (IOException)
            {
                return false;
            }
        });
    }
}
