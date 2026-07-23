using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using osu.Framework.Audio.Track;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[HeadlessTest]
public partial class TestBmsSampleStoreRate : TestScene
{
    private const string silent_flac = "ZkxhQwAAACICQAJAAAAMAAAMAfQA8AAAAFDLQV4FuFvjFJSuG8IzvrWLhAAALAwAAABMYXZmNjEuNy4xMDABAAAAFAAAAGVuY29kZXI9TGF2ZjYxLjcuMTAw//hkCABPCQAAAHyn";

    private string tempDir = null!;
    private BmsSampleStore store = null!;

    [Test]
    public void ScheduledTrackLoadsTenSecondsBeforeUse()
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
        AddAssert("track outside prefetch window", () => store.GetTrack(1) == null);
        AddStep("enter prefetch window", () => manualClock.CurrentTime = 1);
        AddUntilStep("track loads in prefetch window", () => store.GetTrack(1) is { IsLoaded: true });
        addCleanupSteps();
    }

    [Test]
    public void ScheduledTrackRemainsLoadedAfterLastUse()
    {
        Track ownedTrack = null!;
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
        AddAssert("scheduled track loaded during store load", () => store.GetTrack(1) is { IsLoaded: true });
        AddStep("retain scheduled track", () => ownedTrack = store.GetTrack(1));
        AddStep("advance past last use", () => manualClock.CurrentTime = 60_000);
        AddAssert("track remains loaded", () => ReferenceEquals(store.GetTrack(1), ownedTrack));
        AddAssert("track remains undisposed", () => !ownedTrack.IsDisposed);
        addCleanupSteps();
    }

    [Test]
    public void PreloadWaitsForTracks()
    {
        AddStep("create sample + store", () =>
        {
            createWav("sine.wav", 1);
            Add(store = new BmsSampleStore(new Dictionary<ushort, string> { { 1, "sine.wav" } }, tempDir));
        });
        AddUntilStep("wait for store load", () => store.IsLoaded);
        AddAssert("track loaded during store load", () => store.GetTrack(1) is { IsLoaded: true, Length: > 0 });
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
        AddAssert("FLAC fallback track loaded", () => store.GetTrack(1) is { IsLoaded: true, Length: > 0 });
        addCleanupSteps();
    }

    [Test]
    public void PreloadCompletesAcrossBatches()
    {
        AddStep("create batched sample store", () =>
        {
            createWav("batched.wav", 1);
            Dictionary<ushort, string> definitions = [];

            for (ushort sampleKey = 1; sampleKey <= 17; sampleKey++)
                definitions.Add(sampleKey, "batched.wav");

            Add(store = new BmsSampleStore(definitions, tempDir));
        });
        AddUntilStep("wait for batched store load", () => store.IsLoaded);
        AddAssert("all batches loaded", () =>
        {
            for (ushort sampleKey = 1; sampleKey <= 17; sampleKey++)
            {
                if (store.GetTrack(sampleKey) is not { IsLoaded: true, Length: > 0 })
                    return false;
            }

            return true;
        });
        addCleanupSteps();
    }

    [Test]
    public void SameFileWithDifferentKeysCreatesIndependentTracks()
    {
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
        AddAssert("different keys have different tracks", () =>
            store.GetTrack(1) is { } first
            && store.GetTrack(2) is { } second
            && !ReferenceEquals(first, second));
        AddStep("play both keys", () =>
        {
            store.Play(1);
            store.Play(2);
        });
        AddUntilStep("different keys overlap", () => store.GetTrack(1)?.IsRunning == true && store.GetTrack(2)?.IsRunning == true);
        addCleanupSteps();
    }

    [Test]
    public void ZeroKeyIsPlayableForLandmines()
    {
        AddStep("create landmine sample + store", () =>
        {
            createWav("landmine.wav", 1);
            Add(store = new BmsSampleStore(new Dictionary<ushort, string> { { 0, "landmine.wav" } }, tempDir));
        });
        AddUntilStep("wait for store load", () => store.IsLoaded);
        AddStep("play key zero", () => store.Play(0));
        AddUntilStep("landmine sample is playing", () => store.GetTrack(0)?.IsRunning == true);
        addCleanupSteps();
    }

    [Test]
    public void SameKeyRetriggersExistingTrack()
    {
        AddStep("create sample + store", () =>
        {
            createWav("retrigger.wav", 6);
            Add(store = new BmsSampleStore(new Dictionary<ushort, string> { { 1, "retrigger.wav" } }, tempDir));
        });
        AddUntilStep("wait for store load", () => store.IsLoaded);
        AddStep("start from offset", () => store.Play(1, offset: 1000));
        AddUntilStep("started near requested offset", () => store.GetTrack(1) is { CurrentTime: >= 900 });
        AddStep("retrigger same key", () => store.Play(1));
        AddUntilStep("same track restarted from beginning", () => store.GetTrack(1) is { IsRunning: true, CurrentTime: < 200 });
        addCleanupSteps();
    }

    [Test]
    public void RateUsesTrackTempo()
    {
        AddStep("create sample + rate store", () =>
        {
            createWav("rate.wav", 1);
            Add(store = new BmsSampleStore(new Dictionary<ushort, string> { { 1, "rate.wav" } }, tempDir, rate: 2));
        });
        AddUntilStep("wait for store load", () => store.IsLoaded);
        AddUntilStep("track tempo follows rate", () => store.GetTrack(1)?.AggregateTempo.Value == 2);
        AddAssert("track keeps source length", () => store.GetTrack(1) is { Length: >= 900 and <= 1100 });
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
        AddAssert("invalid track is unavailable", () => store.GetTrack(1) == null);
        addCleanupSteps();
    }

    [Test]
    public void DisposalReleasesOwnedTracks()
    {
        Track ownedTrack = null!;

        AddStep("create sample + store", () =>
        {
            createWav("dispose.wav", 1);
            Add(store = new BmsSampleStore(new Dictionary<ushort, string> { { 1, "dispose.wav" } }, tempDir));
        });
        AddUntilStep("wait for store load", () => store.IsLoaded);
        AddStep("retain track for disposal check", () => ownedTrack = store.GetTrack(1));
        AddStep("expire store", () => store.Expire());
        AddUntilStep("owned track disposed", () => ownedTrack.IsDisposed);
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
