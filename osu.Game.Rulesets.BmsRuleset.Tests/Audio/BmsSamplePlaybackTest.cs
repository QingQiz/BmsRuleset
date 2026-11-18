using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Profiles;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Native;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;
using osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[HeadlessTest]
public partial class BmsSamplePlaybackTest : TestScene
{
    private const string silent_flac = "ZkxhQwAAACICQAJAAAAMAAAMAfQA8AAAAFDLQV4FuFvjFJSuG8IzvrWLhAAALAwAAABMYXZmNjEuNy4xMDABAAAAFAAAAGVuY29kZXI9TGF2ZjYxLjcuMTAw//hkCABPCQAAAHyn";

    private string tempDir = null!;
    private BmsSamplePlayback playback = null!;

    [TestCase(false)]
    [TestCase(true)]
    public void HoldHeadSampleStaysPreparedForRecoveryAndSeeking(bool useLazer)
    {
        var manualClock = new ManualClock { CurrentTime = 5000 };
        var mod = new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 }, UseLazer = { Value = useLazer } };
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8 };
        beatmap.HitObjects.Add(new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 9000, SampleKey = 1 });
        mod.ApplyToBeatmap(beatmap);
        var compiled = new BmsEngineChart(beatmap, GameplayProfile.FromMods([mod]));
        var output = new BmsRecordingJudgementOutput
        {
            Feedback = message =>
            {
                if (message == "keysound:1:100")
                    playback.QueueLivePlay(1);
            },
        };
        var engine = new BmsJudgementEngine(compiled, [new(1, output)]);
        AddStep("load in the middle of the hold", () =>
        {
            createWav("hold-head.wav", 1);
            playback = new BmsSamplePlayback(new Dictionary<int, BmsSampleDefinition> { { 1, new("hold-head.wav") } }, tempDir,
                sampleUsages: BmsGameplayAudioController.GetSampleUsages(beatmap, compiled))
            {
                Clock = new FramedClock(manualClock),
            };
            Add(playback);
        });
        AddUntilStep("wait for playback load", () => playback.IsLoaded);
        AddAssert("head sample is prepared before recovery", () => isSampleReady(1));
        AddStep("recover a missed head", () => input(true));
        assertRecoveryAudio("recovery");
        AddStep("release during the hold", () =>
        {
            manualClock.CurrentTime = 6000;
            input(false);
        });
        AddStep("regrab during the hold", () =>
        {
            manualClock.CurrentTime = 7000;
            Assert.That(isSampleReady(1), Is.True);
            input(true);
        });
        assertRecoveryAudio("regrab");
        AddStep("seek back into the hold", () =>
        {
            manualClock.CurrentTime = 3000;
            playback.StopAll();
            engine.Reset();
            output.Reset();
        });
        AddAssert("head sample is prepared after seeking", () => isSampleReady(1));
        AddStep("resume with a recovery press", () => input(true));
        assertRecoveryAudio("recovery after seek");
        AddStep("advance past all hold input windows", () => manualClock.CurrentTime = 60000);
        AddUntilStep("head lease expires after the hold", () => !isSampleReady(1));
        addCleanupSteps();

        void assertRecoveryAudio(string action)
        {
            if (useLazer)
                AddAssert($"{action} is silent during the body", () => BmsAudioTestAccess.GetActiveVoiceCount(playback) == 0);
            else
                AddUntilStep($"{action} plays the head sample", () => BmsAudioTestAccess.GetActiveVoiceCount(playback) > 0);
        }

        void input(bool pressed)
        {
            engine.QueueInput(manualClock.CurrentTime, 1, pressed, false);
            engine.ProcessFrame(new BmsFrameContext(manualClock.CurrentTime, 1), 0);
            playback.SubmitLivePlayBatch();
        }
    }

    [Test]
    public void NativeMixerPatchInstallsOnDesktopBassPlatforms()
    {
        if (!BmsAudioPlatform.SupportsNativeBass)
            Assert.Ignore("The native BMS mixer patch is only enabled on Windows and Linux.");

        AddStep("create sample + playback", () =>
        {
            createWav("native-mixer.wav", 1);
            Add(playback = new BmsSamplePlayback(new Dictionary<int, BmsSampleDefinition> { { 1, new("native-mixer.wav") } }, tempDir));
        });
        AddUntilStep("wait for playback load", () => playback.IsLoaded);
        AddAssert("PCM backend initialised", () => BmsAudioTestAccess.GetOutputMixer(playback) != null);
        AddAssert("PCM float mixer patch installed", () => BmsPcmMixerPatcher.IsInstalled);
        addCleanupSteps();
    }

    [Test]
    public void ScheduledSampleLoadsTenSecondsBeforeUse()
    {
        var manualClock = new ManualClock();

        AddStep("create scheduled sample + playback", () =>
        {
            createWav("scheduled.wav", 1);
            playback = new BmsSamplePlayback(
                new Dictionary<int, BmsSampleDefinition> { { 1, new("scheduled.wav") } },
                tempDir,
                sampleUsages: [new BmsSampleUsage(1, 10_001)])
            {
                Clock = new FramedClock(manualClock),
            };
            Add(playback);
        });
        AddUntilStep("wait for playback load", () => playback.IsLoaded);
        AddAssert("sample outside prefetch window", () => !isSampleReady(1));
        AddStep("enter prefetch window", () => manualClock.CurrentTime = 1);
        AddUntilStep("sample loads in prefetch window", () => isSampleReady(1));
        addCleanupSteps();
    }

    [Test]
    public void ScheduledSampleUsesEarliestCandidateTime()
    {
        AddStep("create early-candidate sample + playback", () =>
        {
            createWav("early-candidate.wav", 1);
            playback = new BmsSamplePlayback(
                new Dictionary<int, BmsSampleDefinition> { { 1, new("early-candidate.wav") } },
                tempDir,
                sampleUsages: [new BmsSampleUsage(1, 100_000, CandidateStartTime: 1_000, CandidateEndTime: 100_280)])
            {
                Clock = new FramedClock(new ManualClock()),
            };
            Add(playback);
        });
        AddUntilStep("wait for playback load", () => playback.IsLoaded);
        AddUntilStep("sample loaded for early candidate", () => isSampleReady(1));
        addCleanupSteps();
    }

    [Test]
    public void InitialisingPastUsageSkipsExpiredUnknownSample()
    {
        var manualClock = new ManualClock { CurrentTime = 60_000 };

        AddStep("create playback past sample lifetime", () =>
        {
            createWav("expired-at-load.wav", 1);
            playback = new BmsSamplePlayback(
                new Dictionary<int, BmsSampleDefinition> { { 1, new("expired-at-load.wav") } },
                tempDir,
                sampleUsages: [new BmsSampleUsage(1, 100)])
            {
                Clock = new FramedClock(manualClock),
            };
            Add(playback);
        });
        AddUntilStep("wait for playback load", () => playback.IsLoaded);
        AddAssert("expired sample was not loaded", () => !isSampleReady(1));
        addCleanupSteps();
    }

    [Test]
    public void InitialisingPastResumableUsageLoadsUnknownSample()
    {
        var manualClock = new ManualClock { CurrentTime = 500 };

        AddStep("create playback within unknown background sample", () =>
        {
            createWav("resumable-at-load.wav", 1);
            playback = new BmsSamplePlayback(
                new Dictionary<int, BmsSampleDefinition> { { 1, new("resumable-at-load.wav") } },
                tempDir,
                sampleUsages: [new BmsSampleUsage(1, 100, ResumeAfterSeek: true)])
            {
                Clock = new FramedClock(manualClock),
            };
            Add(playback);
        });
        AddUntilStep("wait for playback load", () => playback.IsLoaded);
        AddAssert("resumable sample was loaded", () => isSampleReady(1));
        addCleanupSteps();
    }

    [Test]
    public void ScheduledSampleRemainsLoadedAfterLastUse()
    {
        var manualClock = new ManualClock();

        AddStep("create expiring sample + playback", () =>
        {
            createWav("retained.wav", 1);
            playback = new BmsSamplePlayback(
                new Dictionary<int, BmsSampleDefinition> { { 1, new("retained.wav") } },
                tempDir,
                sampleUsages: [new BmsSampleUsage(1, 100)])
            {
                Clock = new FramedClock(manualClock),
            };
            Add(playback);
        });
        AddUntilStep("wait for playback load", () => playback.IsLoaded);
        AddAssert("scheduled sample loaded during playback load", () => isSampleReady(1));
        AddStep("advance past last use", () => manualClock.CurrentTime = 60_000);
        AddUntilStep("expired PCM lease is released", () => !playback.IsSampleReady(1));
        addCleanupSteps();
    }

    [Test]
    public void PreloadWaitsForSamples()
    {
        AddStep("create sample + playback", () =>
        {
            createWav("sine.wav", 1);
            Add(playback = new BmsSamplePlayback(new Dictionary<int, BmsSampleDefinition> { { 1, new("sine.wav") } }, tempDir));
        });
        AddUntilStep("wait for playback load", () => playback.IsLoaded);
        AddAssert("sample loaded during playback load", () => isSampleReady(1) && sampleLength(1) > 0);
        addCleanupSteps();
    }

    [Test]
    public void WavDefinitionFallsBackToFlac()
    {
        AddStep("create FLAC sample + playback", () =>
        {
            tempDir = Directory.CreateTempSubdirectory("bmstracks").FullName;
            File.WriteAllBytes(Path.Combine(tempDir, "sine.flac"), Convert.FromBase64String(silent_flac));
            Add(playback = new BmsSamplePlayback(new Dictionary<int, BmsSampleDefinition> { { 1, new("sine.wav") } }, tempDir));
        });
        AddUntilStep("wait for playback load", () => playback.IsLoaded);
        AddAssert("FLAC fallback sample loaded", () => isSampleReady(1) && sampleLength(1) > 0);
        addCleanupSteps();
    }

    [Test]
    public void PreloadCompletesAcrossBatches()
    {
        AddStep("create batched sample playback", () =>
        {
            createWav("batched.wav", 1);
            var definitions = new Dictionary<int, BmsSampleDefinition>();

            for (int sampleKey = 1; sampleKey <= 17; sampleKey++)
                definitions.Add(sampleKey, new BmsSampleDefinition("batched.wav"));

            Add(playback = new BmsSamplePlayback(definitions, tempDir));
        });
        AddUntilStep("wait for batched playback load", () => playback.IsLoaded);
        AddAssert("all batches loaded", () =>
        {
            for (int sampleKey = 1; sampleKey <= 17; sampleKey++)
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
        AddStep("create shared sample + playback", () =>
        {
            createWav("shared.wav", 6);
            Add(playback = new BmsSamplePlayback(new Dictionary<int, BmsSampleDefinition>
            {
                { 1, new("shared.wav") },
                { 2, new("shared.wav") },
            }, tempDir));
        });
        AddUntilStep("wait for playback load", () => playback.IsLoaded);
        AddAssert("shared PCM is decoded once", () => BmsAudioTestAccess.GetCachedAssetCount(playback) == 1);
        AddStep("play both keys", () =>
        {
            playback.Play(1);
            playback.Play(2);
        });
        AddUntilStep("different keys overlap", () =>
            BmsAudioTestAccess.GetActiveVoiceCount(playback) >= 2);
        addCleanupSteps();
    }

    [Test]
    public void LiveKeysoundsWaitForBatchFlush()
    {
        AddStep("create live sample + playback", () =>
        {
            createWav("live.wav", 6);
            Add(playback = new BmsSamplePlayback(new Dictionary<int, BmsSampleDefinition>
            {
                { 1, new("live.wav") },
                { 2, new("live.wav") },
            }, tempDir));
        });
        AddUntilStep("wait for playback load", () => playback.IsLoaded);
        AddStep("queue live chord", () =>
        {
            playback.QueueLivePlay(1);
            playback.QueueLivePlay(2);
        });
        AddAssert("samples wait for submit", () => BmsAudioTestAccess.GetActiveVoiceCount(playback) == 0);
        AddStep("submit live chord", () => playback.SubmitLivePlayBatch());
        AddUntilStep("samples start together", () =>
            BmsAudioTestAccess.GetActiveVoiceCount(playback) >= 2);
        addCleanupSteps();
    }

    [Test]
    public void ZeroKeyIsPlayableForLandmines()
    {
        AddStep("create landmine sample + playback", () =>
        {
            createWav("landmine.wav", 1);
            Add(playback = new BmsSamplePlayback(new Dictionary<int, BmsSampleDefinition> { { 0, new("landmine.wav") } }, tempDir));
        });
        AddUntilStep("wait for playback load", () => playback.IsLoaded);
        AddStep("play key zero", () => playback.Play(0));
        AddUntilStep("landmine sample is playing", () => BmsAudioTestAccess.GetActiveVoiceCount(playback) > 0);
        addCleanupSteps();
    }

    [Test]
    public void SameKeyRetriggersWithNewVoice()
    {
        AddStep("create sample + playback", () =>
        {
            createWav("retrigger.wav", 6);
            Add(playback = new BmsSamplePlayback(new Dictionary<int, BmsSampleDefinition> { { 1, new("retrigger.wav") } }, tempDir));
        });
        AddUntilStep("wait for playback load", () => playback.IsLoaded);
        AddStep("start from offset", () => playback.Play(1, offset: 1000));
        AddUntilStep("offset voice started", () => BmsAudioTestAccess.GetActiveVoiceCount(playback) == 1);
        AddStep("retrigger same key", () => playback.Play(1));
        AddUntilStep("same key remains active after retrigger", () => BmsAudioTestAccess.GetActiveVoiceCount(playback) == 1);
        addCleanupSteps();
    }

    [Test]
    public void RateUsesFixedRateTempo()
    {
        AddStep("create sample + rate playback", () =>
        {
            createWav("rate.wav", 1);
            Add(playback = new BmsSamplePlayback(new Dictionary<int, BmsSampleDefinition> { { 1, new("rate.wav") } }, tempDir, rate: 2));
        });
        AddUntilStep("wait for playback load", () => playback.IsLoaded);
        AddAssert("fixed-rate backend selected", () => BmsAudioTestAccess.GetOutputMixer(playback) != null);
        AddAssert("sample keeps source length", () => sampleLength(1) is >= 900 and <= 1100);
        addCleanupSteps();
    }

    [Test]
    public void InvalidAudioDoesNotWaitForTimeout()
    {
        AddStep("create invalid sample + playback", () =>
        {
            tempDir = Directory.CreateTempSubdirectory("bmstracks").FullName;
            File.WriteAllBytes(Path.Combine(tempDir, "invalid.wav"), [0, 1, 2, 3]);
            Add(playback = new BmsSamplePlayback(new Dictionary<int, BmsSampleDefinition> { { 1, new("invalid.wav") } }, tempDir));
        });
        AddUntilStep("playback loads without timeout", () => playback.IsLoaded);
        AddAssert("invalid sample is unavailable", () => !playback.HasSampleDefinition(1));
        addCleanupSteps();
    }

    [Test]
    public void EmptySampleDefinitionIsUnavailable()
    {
        AddStep("create playback with empty definition", () =>
            Add(playback = new BmsSamplePlayback(new Dictionary<int, BmsSampleDefinition> { { 1, new(string.Empty) } }, tempDir)));
        AddUntilStep("wait for playback load", () => playback.IsLoaded);
        AddAssert("empty definition is unavailable", () => !playback.HasSampleDefinition(1));
        addCleanupSteps();
    }

    [Test]
    public void DisposalReleasesOwnedPcmAssets()
    {
        AddStep("create sample + playback", () =>
        {
            createWav("dispose.wav", 1);
            Add(playback = new BmsSamplePlayback(new Dictionary<int, BmsSampleDefinition> { { 1, new("dispose.wav") } }, tempDir));
        });
        AddUntilStep("wait for playback load", () => playback.IsLoaded);
        AddStep("expire playback", () => playback.Expire());
        AddUntilStep("owned audio is disposed", () => BmsAudioTestAccess.IsSessionDisposed(playback));
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

    private bool isSampleReady(int sampleKey) => playback.IsSampleReady(sampleKey);

    private double sampleLength(int sampleKey) => playback.GetSampleLength(sampleKey);

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
        AddStep("expire playback", () => playback.Expire());
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
