#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Testing;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Preview;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal;

[HeadlessTest]
public partial class BmsPreviewTrackTest : OsuTestScene
{
    private AudioManager audio = null!;
    private BmsRulesetConfigManager? previousConfigManager;
    private BmsRulesetConfigManager? testConfigManager;

    [TearDown]
    public void TearDown()
    {
        var configManager = testConfigManager;
        testConfigManager = null;
        configManager?.Dispose();

        if (configManager != null)
            BmsRulesetRuntime.ConfigManager = previousConfigManager;

        previousConfigManager = null;
    }

    [Test]
    public void TestEventAtTimeZeroPlaysOnStart()
    {
        BmsPreviewTrack track = null!;

        AddStep("create track and start from beginning", () =>
        {
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            writePcmWave(Path.Combine(directory, "test.wav"), TimeSpan.FromSeconds(1));

            track = new BmsEventPreviewTrack(
                _ => BmsEventPreviewTimeline.Create(
                    () => [new BmsPreviewSampleEvent(new BmsSampleEvent(0, 0, 1, 100), false)],
                    new Dictionary<ushort, string> { [1] = "test.wav" }),
                directory,
                audio);

            audio.AddItem(track);
            track.Start();
        });

        AddUntilStep("track playing from time zero", () => getActivePlaybackCount(track) > 0);
        AddStep("dispose track", () => track.Dispose());
    }

    [Test]
    public void TestEventAtTimeZeroPlaysWhenTimelineCompletesAfterStart()
    {
        BmsPreviewTrack track = null!;
        var timelineGate = new ManualResetEventSlim();

        AddStep("start before timeline is ready", () =>
        {
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-delayed-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            writePcmWave(Path.Combine(directory, "test.wav"), TimeSpan.FromSeconds(1));

            track = new BmsEventPreviewTrack(
                _ => BmsEventPreviewTimeline.Create(
                    () =>
                    {
                        timelineGate.Wait();
                        return [new BmsPreviewSampleEvent(new BmsSampleEvent(0, 0, 1, 100), false)];
                    },
                    new Dictionary<ushort, string> { [1] = "test.wav" }),
                directory,
                audio);

            audio.AddItem(track);
            track.Start();
        });

        AddStep("complete timeline", timelineGate.Set);
        AddUntilStep("track playing from original start position", () => getActivePlaybackCount(track) > 0);
        AddStep("dispose track", () =>
        {
            track.Dispose();
            timelineGate.Dispose();
        });
    }

    [Test]
    public void TestEventTimelineWaitsForFirstAudioBeforeStarting()
    {
        BmsPreviewTrack track = null!;
        var timelineGate = new ManualResetEventSlim();

        AddStep("start before timeline is ready", () =>
        {
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-delayed-sample-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            writePcmWave(Path.Combine(directory, "test.wav"), TimeSpan.FromSeconds(1));

            track = new BmsEventPreviewTrack(
                _ => BmsEventPreviewTimeline.Create(
                    () =>
                    {
                        timelineGate.Wait();
                        return [new BmsPreviewSampleEvent(new BmsSampleEvent(0, 0, 1, 100), false)];
                    },
                    new Dictionary<ushort, string> { [1] = "test.wav" }),
                directory,
                audio);

            audio.AddItem(track);
            track.Start();
        });

        AddWaitStep("wait while timeline is pending", 10);
        AddAssert("preview clock has not started", () => !track.IsRunning && track.CurrentTime == 0);
        AddStep("complete timeline", timelineGate.Set);
        AddUntilStep("preview clock starts", () => track.IsRunning);
        AddAssert("first audio is already playing", () => getActivePlaybackCount(track) > 0);
        AddStep("dispose track", () =>
        {
            track.Dispose();
            timelineGate.Dispose();
        });
    }

    [Test]
    public void TestRestoreFadeWaitsForAudioLoadWithRunningClock()
    {
        BmsPreviewTrack track = null!;
        var audioLoadStarted = new ManualResetEventSlim();
        var audioLoadCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waitForFadeDuration = new Stopwatch();

        AddStep("create timeline with gated audio load", () =>
        {
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-delayed-restore-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            writePcmWave(Path.Combine(directory, "test.wav"), TimeSpan.FromSeconds(1));

            track = new BmsEventPreviewTrack(
                _ => BmsEventPreviewTimeline.CreateSingleFile("test.wav"),
                directory,
                audio,
                _ =>
                {
                    audioLoadStarted.Set();
                    return audioLoadCompletion.Task;
                })
            {
                PlaybackMode = BmsPreviewTrackPlaybackMode.GameplayClockOnly,
            };

            track.Frequency.Value = 0;
            audio.AddItem(track);
        });

        AddUntilStep("timeline is ready", () => ((BmsEventPreviewTrack)track).Playback != null);
        AddStep("restore with gameplay clock running at zero", () =>
        {
            track.Start();
            Assert.That(track.IsRunning && track.CurrentTime == 0, Is.True);
            track.RestorePreview(0);
            waitForFadeDuration.Start();
        });

        AddUntilStep("audio load is pending", () => audioLoadStarted.IsSet);
        AddAssert("restore fade duration is 2.5 seconds", () => BmsPreviewTrack.RESTORE_FADE_DURATION == 2_500);
        AddUntilStep("wait beyond fade duration", () => waitForFadeDuration.ElapsedMilliseconds >= 2_550);
        AddAssert("fade remains held while audio is pending", () => getRestoreFadeVolume(track) == 0);
        AddStep("complete audio load", audioLoadCompletion.SetResult);
        AddUntilStep("restored audio starts", () => track.IsRunning && getActivePlaybackCount(track) == 1);
        AddUntilStep("restore fade completes", () => getRestoreFadeVolume(track) == 1);
        AddStep("dispose track", () =>
        {
            track.Dispose();
            audioLoadStarted.Dispose();
        });
    }

    [Test]
    public void TestEventTimelineWaitsForAllInitialAudioBeforeStarting()
    {
        BmsPreviewTrack track = null!;

        AddStep("create initial events beyond one prefetch batch", () =>
        {
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-initial-batch-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            writePcmWave(Path.Combine(directory, "test.wav"), TimeSpan.FromSeconds(1));

            var events = Enumerable.Range(1, 18)
                                   .Select(key => new BmsSampleEvent(0, 0, (ushort)key, 100))
                                   .ToArray();
            var definitions = Enumerable.Range(1, 18).ToDictionary(key => (ushort)key, _ => "test.wav");

            track = createTrack(events, definitions, directory);
            audio.AddItem(track);
            track.Start();
        });

        AddUntilStep("preview clock starts", () => track.IsRunning);
        AddAssert("all initial audio is already playing", () => getActivePlaybackCount(track) == 18);
        AddStep("dispose track", () => track.Dispose());
    }

    [Test]
    public void TestEventTimelinePrefetchesOneSecondAhead()
    {
        BmsPreviewTrack track = null!;
        var audioLoadCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var audioLoadCount = 0;

        AddStep("create events around prefetch boundary", () =>
        {
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-prefetch-boundary-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            writePcmWave(Path.Combine(directory, "test.wav"), TimeSpan.FromSeconds(1));

            track = new BmsEventPreviewTrack(
                _ => BmsEventPreviewTimeline.Create(
                    () =>
                    [
                        new BmsPreviewSampleEvent(new BmsSampleEvent(0, 0, 1, 100), false),
                        new BmsPreviewSampleEvent(new BmsSampleEvent(1000, 0, 2, 100), false),
                        new BmsPreviewSampleEvent(new BmsSampleEvent(1001, 0, 3, 100), false),
                    ],
                    new Dictionary<ushort, string>
                    {
                        [1] = "test.wav",
                        [2] = "test.wav",
                        [3] = "test.wav",
                    }),
                directory,
                audio,
                _ =>
                {
                    Interlocked.Increment(ref audioLoadCount);
                    return audioLoadCompletion.Task;
                });

            audio.AddItem(track);
            track.Start();
        });

        AddUntilStep("events through one second start loading", () => Volatile.Read(ref audioLoadCount) >= 2);
        AddWaitStep("allow additional prefetch updates", 5);
        AddAssert("event after one second is not prefetched", () => Volatile.Read(ref audioLoadCount) == 2);
        AddStep("dispose track", () =>
        {
            track.Dispose();
            audioLoadCompletion.TrySetResult();
        });
    }

    [Test]
    public void TestTimelineWithoutAudioLoaderDoesNotBlockClock()
    {
        BmsPreviewTrack track = null!;

        AddStep("create timeline without audio loader", () =>
        {
            track = new BmsEventPreviewTrack(
                _ => new BmsEventPreviewTimeline(
                    [new BmsPreviewTimelineEntry(0, 1, "missing.wav", 100, false)],
                    1000),
                null,
                audio);

            audio.AddItem(track);
            track.Start();
        });

        AddUntilStep("preview clock starts", () => track.IsRunning);
        AddAssert("no audio track is created", () => getActivePlaybackCount(track) == 0);
        AddStep("dispose track", () => track.Dispose());
    }

    [Test]
    public void TestLeadingEmptyTimelineIsTrimmedForEventPreview()
    {
        BmsPreviewTrack track = null!;

        AddStep("create track with delayed first event", () =>
        {
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-trim-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            writePcmWave(Path.Combine(directory, "test.wav"), TimeSpan.FromSeconds(1));

            track = createTrack(
                [new BmsSampleEvent(2000, 0, 1, 100)],
                new Dictionary<ushort, string> { [1] = "test.wav" },
                directory);

            audio.AddItem(track);
            track.Start();
        });

        AddUntilStep("first audible event plays immediately", () => getActivePlaybackCount(track) > 0);
        AddStep("dispose track", () => track.Dispose());
    }

    [Test]
    public void TestSingleFileTimelineUsesLoadedTrackLength()
    {
        BmsPreviewTrack track = null!;
        Track loadedTrack = null!;

        AddStep("create single-file timeline", () =>
        {
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-single-timeline-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            writePcmWave(Path.Combine(directory, "preview.wav"), TimeSpan.FromSeconds(1));

            track = new BmsEventPreviewTrack(
                _ => BmsEventPreviewTimeline.CreateSingleFile("preview.wav"),
                directory,
                audio);

            audio.AddItem(track);
            track.Start();
        });

        AddUntilStep("single file is playing", () => getActivePlaybackCount(track) == 1);
        AddAssert("timeline uses audio length", () => track.Length, () => Is.EqualTo(1000).Within(1));
        AddStep("capture loaded track", () => loadedTrack = getActiveTrack(track)!);
        AddStep("seek single-file timeline", () => track.Seek(500));
        AddUntilStep("loaded track is reused after seek", () =>
            ReferenceEquals(getActiveTrack(track), loadedTrack)
            && loadedTrack is { IsRunning: true, CurrentTime: >= 400 });
        AddStep("dispose track", () => track.Dispose());
    }

    [Test]
    public void TestUnknownShortSingleFileRestoreRestartsFromBeginning()
    {
        BmsPreviewTrack track = null!;

        AddStep("restore unloaded short single file past its end", () =>
        {
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-short-restore-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            writePcmWave(Path.Combine(directory, "preview.wav"), TimeSpan.FromSeconds(1));

            track = new BmsEventPreviewTrack(
                _ => BmsEventPreviewTimeline.CreateSingleFile("preview.wav"),
                directory,
                audio)
            {
                PlaybackMode = BmsPreviewTrackPlaybackMode.GameplayClockOnly,
            };

            audio.AddItem(track);
            track.RestorePreview(10_000);
        });

        AddUntilStep("preview clock starts", () => track.IsRunning);
        AddAssert("single file started from beginning with clock", () =>
            getActiveTrack(track) is { IsRunning: true, CurrentTime: < 200 }
            && track.CurrentTime < 200);
        AddStep("dispose track", () => track.Dispose());
    }

    [Test]
    public void TestUnknownLongSingleFileRestorePreservesPositionPastDefaultLength()
    {
        BmsPreviewTrack track = null!;

        AddStep("restore unloaded long single file past default length", () =>
        {
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-long-restore-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            writePcmWave(Path.Combine(directory, "preview.wav"), TimeSpan.FromSeconds(31));

            track = new BmsEventPreviewTrack(
                _ => BmsEventPreviewTimeline.CreateSingleFile("preview.wav"),
                directory,
                audio)
            {
                PlaybackMode = BmsPreviewTrackPlaybackMode.GameplayClockOnly,
            };

            audio.AddItem(track);
            track.Start();
            track.Seek(30_500);
            track.RestorePreview(30_500);
        });

        AddUntilStep("single file resumes past default length", () =>
            getActiveTrack(track) is { IsRunning: true, CurrentTime: >= 30_000 }
            && track.CurrentTime >= 30_000);
        AddStep("dispose track", () => track.Dispose());
    }

    [Test]
    public void TestBrokenSingleFileRestoreUsesShortFallbackLength()
    {
        BmsPreviewTrack track = null!;

        AddStep("restore broken single file to short fallback", () =>
        {
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-broken-short-restore-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "preview.wav"), "not audio");
            writePcmWave(Path.Combine(directory, "fallback.wav"), TimeSpan.FromSeconds(1));

            track = createTrack(
                [
                    _ => BmsEventPreviewTimeline.CreateSingleFile("preview.wav"),
                    _ => BmsEventPreviewTimeline.CreateSingleFile("fallback.wav"),
                ],
                directory);
            track.PlaybackMode = BmsPreviewTrackPlaybackMode.GameplayClockOnly;

            audio.AddItem(track);
            track.RestorePreview(10_000);
        });

        AddUntilStep("preview clock starts", () => track.IsRunning);
        AddAssert("short fallback started from beginning with clock", () =>
            getActiveTrack(track) is { IsRunning: true, CurrentTime: < 200 }
            && track.CurrentTime < 200);
        AddStep("dispose track", () => track.Dispose());
    }

    [Test]
    public void TestBrokenSingleFileRestorePreservesPositionInLongFallback()
    {
        BmsPreviewTrack track = null!;

        AddStep("restore broken single file to long fallback", () =>
        {
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-broken-long-restore-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "preview.wav"), "not audio");
            writePcmWave(Path.Combine(directory, "fallback.wav"), TimeSpan.FromSeconds(31));

            track = createTrack(
                [
                    _ => BmsEventPreviewTimeline.CreateSingleFile("preview.wav"),
                    _ => BmsEventPreviewTimeline.CreateSingleFile("fallback.wav"),
                ],
                directory);
            track.PlaybackMode = BmsPreviewTrackPlaybackMode.GameplayClockOnly;

            audio.AddItem(track);
            track.Start();
            track.Seek(30_500);
            track.RestorePreview(30_500);
        });

        AddUntilStep("long fallback preserves restore position", () =>
            getActiveTrack(track) is { IsRunning: true, CurrentTime: >= 30_000 }
            && track.CurrentTime >= 30_000);
        AddStep("dispose track", () => track.Dispose());
    }

    [Test]
    public void TestDeclaredWavResolvesOggFallbackFile()
    {
        BmsPreviewTrack track = null!;

        AddStep("create track with wav declaration and ogg file", () =>
        {
            var sourceDirectory = Path.Combine(BmsEmbeddedSongDecoderTest.TestSongsRoot, "103_outlaw_ogg");
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-ogg-fallback-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            File.Copy(Path.Combine(sourceDirectory, "Track_01_001.ogg"), Path.Combine(directory, "Track_01_001.ogg"));

            Assert.That(File.Exists(Path.Combine(directory, "Track_01_001.wav")), Is.False);

            track = createTrack(
                [new BmsSampleEvent(0, 0, 1, 100)],
                new Dictionary<ushort, string> { [1] = "Track_01_001.wav" },
                directory);
            audio.AddItem(track);
            track.Start();
        });

        AddUntilStep("fallback track resolved", () => getActivePlaybackCount(track) > 0);
        AddStep("dispose track", () => track.Dispose());
    }

    [Test]
    public void TestDeclaredPreviewHasPriorityOverFolderPreview()
    {
        BmsPreviewTrack track = null!;
        var eventFactoryInvoked = false;

        AddStep("create track with declared and folder preview", () =>
        {
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-priority-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            writePcmWave(Path.Combine(directory, "declared.wav"), TimeSpan.FromSeconds(1));
            writePcmWave(Path.Combine(directory, "preview.wav"), TimeSpan.FromSeconds(1));

            track = createTrack(
                [
                    _ => BmsEventPreviewTimeline.CreateSingleFile("declared.wav"),
                    _ => BmsEventPreviewTimeline.CreateSingleFile("preview.wav"),
                    _ =>
                    {
                        eventFactoryInvoked = true;
                        return new BmsEventPreviewTimeline([], BmsEventPreviewTimeline.DEFAULT_LENGTH);
                    },
                ],
                directory);
            audio.AddItem(track);
            track.Start();
        });

        AddUntilStep("declared preview is playing", () => getActivePlaybackCount(track) == 1);
        AddAssert("declared preview has priority", () => getFirstTimelineSamplePath(track) == "declared.wav");
        AddAssert("event factory is not invoked", () => !eventFactoryInvoked);
        AddStep("dispose track", () => track.Dispose());
    }

    [Test]
    public void TestDedicatedPreviewCanBeDisabled()
    {
        BmsPreviewTrack track = null!;
        var eventFactoryInvoked = false;

        AddStep("create sample-only preview", () =>
        {
            previousConfigManager = BmsRulesetRuntime.ConfigManager;
            testConfigManager = new BmsRulesetConfigManager(null, new BmsRuleset().RulesetInfo);
            testConfigManager.SetValue(BmsRulesetSetting.UseDedicatedPreviewAudio, false);
            BmsRulesetRuntime.ConfigManager = testConfigManager;

            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-disabled-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            writePcmWave(Path.Combine(directory, "declared.wav"), TimeSpan.FromSeconds(1));
            writePcmWave(Path.Combine(directory, "event.wav"), TimeSpan.FromSeconds(1));

            track = new BmsEventPreviewTrack(
                _ => BmsEventPreviewTimeline.Create(
                    () =>
                    {
                        eventFactoryInvoked = true;
                        return [new BmsPreviewSampleEvent(new BmsSampleEvent(0, 0, 1, 100), true)];
                    },
                    new Dictionary<ushort, string> { [1] = "event.wav" }),
                directory,
                audio);

            audio.AddItem(track);
            track.Start();
        });

        AddUntilStep("event factory is invoked", () => eventFactoryInvoked);
        AddUntilStep("chart track preview plays", () => getActivePlaybackCount(track) > 0);
        AddStep("dispose track", () => track.Dispose());
    }

    [Test]
    public void TestMissingDeclaredPreviewFallsBackToFolderPreview()
    {
        BmsPreviewTrack track = null!;

        AddStep("create track with missing declared preview and folder preview", () =>
        {
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-folder-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            writePcmWave(Path.Combine(directory, "preview.wav"), TimeSpan.FromSeconds(1));

            track = createTrack(
                [_ => BmsEventPreviewTimeline.CreateSingleFile("preview.wav")],
                directory);
            audio.AddItem(track);
            track.Start();
        });

        AddUntilStep("folder preview is used", () => getActivePlaybackCount(track) == 1);
        AddAssert("folder preview path is selected", () => getFirstTimelineSamplePath(track) == "preview.wav");
        AddStep("dispose track", () => track.Dispose());
    }

    [Test]
    public void TestMissingSingleFilePreviewKeepsEventPreview()
    {
        BmsPreviewTrack track = null!;

        AddStep("create track with event preview only", () =>
        {
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-events-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            writePcmWave(Path.Combine(directory, "event.wav"), TimeSpan.FromSeconds(1));

            track = createTrack(
                [new BmsSampleEvent(0, 0, 1, 100)],
                new Dictionary<ushort, string> { [1] = "event.wav" },
                directory);

            audio.AddItem(track);
            track.Start();
        });

        AddUntilStep("event preview still plays", () => getActivePlaybackCount(track) > 0);
        AddStep("dispose track", () => track.Dispose());
    }

    [Test]
    public void TestBrokenDedicatedPreviewPreservesFallbackStartPosition()
    {
        BmsPreviewTrack track = null!;
        var fallbackStarted = new ManualResetEventSlim();
        var fallbackGate = new ManualResetEventSlim();

        AddStep("start broken dedicated preview", () =>
        {
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-broken-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            File.WriteAllText(Path.Combine(directory, "preview.wav"), "not audio");
            writePcmWave(Path.Combine(directory, "event.wav"), TimeSpan.FromSeconds(1));

            track = createTrack(
                [
                    _ => BmsEventPreviewTimeline.CreateSingleFile("preview.wav"),
                    _ =>
                    {
                        fallbackStarted.Set();
                        fallbackGate.Wait();
                        return new BmsEventPreviewTimeline(
                            [new BmsPreviewTimelineEntry(0, 1, "event.wav", 100, false)],
                            1000);
                    },
                ],
                directory);

            audio.AddItem(track);
            track.Start();
        });

        AddUntilStep("fallback timeline requested", () => fallbackStarted.IsSet);
        AddAssert("preview clock waits for fallback audio", () => !track.IsRunning && track.CurrentTime == 0);
        AddStep("complete fallback timeline", fallbackGate.Set);
        AddUntilStep("preview clock starts", () => track.IsRunning);
        AddAssert("fallback plays from original start position", () =>
            getActiveTrack(track) is { IsRunning: true, CurrentTime: < 200 });
        AddStep("dispose track", () =>
        {
            track.Dispose();
            fallbackStarted.Dispose();
            fallbackGate.Dispose();
        });
    }

    [Test]
    public void TestBrokenSingleFileFallsBackAfterNonZeroSeek()
    {
        BmsPreviewTrack track = null!;

        AddStep("start broken single-file preview after seek", () =>
        {
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-broken-seek-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            File.WriteAllText(Path.Combine(directory, "preview.wav"), "not audio");
            writePcmWave(Path.Combine(directory, "event.wav"), TimeSpan.FromSeconds(2));

            track = createTrack(
                [
                    _ => BmsEventPreviewTimeline.CreateSingleFile("preview.wav"),
                    _ => new BmsEventPreviewTimeline(
                        [new BmsPreviewTimelineEntry(0, 1, "event.wav", 100, true)],
                        2000),
                ],
                directory);

            audio.AddItem(track);
            track.Seek(500);
            track.Start();
        });

        AddUntilStep("fallback resumes at requested position", () =>
            getActiveTrack(track) is { IsRunning: true, CurrentTime: >= 400 });
        AddStep("dispose track", () => track.Dispose());
    }

    [Test]
    public void TestRestoreAfterClockOnlyPlaybackDoesNotCatchUpPastEvents()
    {
        BmsPreviewTrack track = null!;

        AddStep("create clock-only track advanced past first event", () =>
        {
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-suppressed-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            writePcmWave(Path.Combine(directory, "event.wav"), TimeSpan.FromSeconds(1));

            track = createTrack(
                [
                    new BmsSampleEvent(1000, 0, 1, 100),
                    new BmsSampleEvent(3000, 0, 1, 100),
                ],
                new Dictionary<ushort, string> { [1] = "event.wav" },
                directory);

            audio.AddItem(track);
            track.PlaybackMode = BmsPreviewTrackPlaybackMode.GameplayClockOnly;
            track.Seek(1500);
            track.PlaybackMode = BmsPreviewTrackPlaybackMode.Preview;
            track.Start();
        });

        AddAssert("past event was skipped", () => getActivePlaybackCount(track) == 0);
        AddStep("dispose track", () => track.Dispose());
    }

    [Test]
    public void TestRestoreAfterClockOnlyPlaybackResumesActiveBackgroundTrack()
    {
        BmsPreviewTrack track = null!;

        AddStep("create clock-only track within long background event", () =>
        {
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-resume-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            writePcmWave(Path.Combine(directory, "bgm.wav"), TimeSpan.FromSeconds(3));

            track = createTrack(
                [new BmsSampleEvent(0, 0, 1, 100)],
                new Dictionary<ushort, string> { [1] = "bgm.wav" },
                directory);
            track.PlaybackMode = BmsPreviewTrackPlaybackMode.GameplayClockOnly;
            audio.AddItem(track);
            track.Seek(1000);
            track.Start();
            track.PlaybackMode = BmsPreviewTrackPlaybackMode.Preview;
            track.Start();
        });

        AddUntilStep("background track resumes from gameplay position", () =>
            getActiveTrack(track) is { IsRunning: true, CurrentTime: >= 900 });
        AddStep("dispose track", () => track.Dispose());
    }

    [Test]
    public void TestSeekWhileRunningReplacesActiveTracksAtTargetPosition()
    {
        BmsPreviewTrack track = null!;
        Task stopTask = null!;
        Task startTask = null!;

        AddStep("create running preview", () =>
        {
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-seek-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            writePcmWave(Path.Combine(directory, "first.wav"), TimeSpan.FromSeconds(3));
            writePcmWave(Path.Combine(directory, "second.wav"), TimeSpan.FromSeconds(3));

            track = createTrack(
                [
                    new BmsSampleEvent(0, 0, 1, 100),
                    new BmsSampleEvent(1000, 0, 2, 100),
                ],
                new Dictionary<ushort, string>
                {
                    [1] = "first.wav",
                    [2] = "second.wav",
                },
                directory);

            audio.AddItem(track);
            track.Start();
        });

        AddUntilStep("initial track is running", () => getActivePlaybackCount(track) == 1);
        AddStep("seek while running", () => track.Seek(1500));
        AddUntilStep("tracks resumed at seek target", () =>
            getActivePlaybackCount(track) == 2
            && getActiveTracks(track).All(active => active.CurrentTime >= 400));
        AddStep("stop asynchronously", () => stopTask = track.StopAsync());
        AddUntilStep("stop completes", () => stopTask.IsCompleted && !track.IsRunning && getActivePlaybackCount(track) == 0);
        AddStep("start asynchronously", () => startTask = track.StartAsync());
        AddUntilStep("start completes", () => startTask.IsCompleted && track.IsRunning && getActivePlaybackCount(track) == 2);
        AddStep("reset", () => track.Reset());
        AddAssert("reset completes at beginning", () => !track.IsRunning && track.CurrentTime == 0 && getActivePlaybackCount(track) == 0);
        AddStep("dispose track", () => track.Dispose());
    }

    [Test]
    public void TestEventTracksAreCreatedOnDemandAndFollowRateAdjustments()
    {
        BmsPreviewTrack track = null!;
        var tempo = new BindableDouble(1.5);

        AddStep("create event preview", () =>
        {
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-lazy-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            writePcmWave(Path.Combine(directory, "event.wav"), TimeSpan.FromSeconds(1));

            track = createTrack(
                [new BmsSampleEvent(0, 0, 1, 100)],
                new Dictionary<ushort, string> { [1] = "event.wav" },
                directory);
            audio.AddItem(track);
            track.AddAdjustment(AdjustableProperty.Tempo, tempo);
        });

        AddAssert("event tracks are not preloaded", () => getActivePlaybackCount(track) == 0);
        AddStep("start event preview", () => track.Start());
        AddUntilStep("event track starts", () => getActivePlaybackCount(track) > 0);
        AddAssert("event track inherits tempo", () => getActiveTrack(track)?.AggregateTempo.Value == tempo.Value);
        AddStep("dispose track", () => track.Dispose());
    }

    [Test]
    public void TestPreviewTrackIsNotDummyDevice()
    {
        BmsPreviewTrack track = null!;

        AddStep("create track", () =>
        {
            track = createTrack(
                [],
                new Dictionary<ushort, string>(),
                null);
            audio.AddItem(track);
        });

        AddAssert("not dummy device", () => !track.IsDummyDevice);
        AddStep("dispose track", () => track.Dispose());
    }

    [Test]
    public void TestOutlawChartLoadsWithoutError()
    {
        BmsPreviewTrack track = null!;

        AddStep("load outlaw chart and start", () =>
        {
            var directory = Path.Combine(BmsEmbeddedSongDecoderTest.TestSongsRoot, "103_outlaw_ogg");
            var chartPath = Path.Combine(directory, "01_outlaw_spn.bms");

            using var stream = File.OpenRead(chartPath);
            using var reader = new LineBufferedReader(stream);
            var beatmap = (IBmsBeatmap)new BmsBeatmapDecoder().Decode(reader);

            Assert.DoesNotThrow(() =>
            {
                track = createTrack(
                    beatmap.BackgroundSampleEvents,
                    beatmap.SampleDefinitions,
                    directory);
            });

            audio.AddItem(track);
            Assert.That(track.Length, Is.GreaterThan(0));
        });

        AddAssert("track length is positive", () => track.Length > 0);
        AddStep("dispose track", () => track.Dispose());
    }

    private static int getActivePlaybackCount(BmsPreviewTrack track) =>
        getPlaybackTracks(track).Count(activeTrack => activeTrack.IsRunning);

    private static Track? getActiveTrack(BmsPreviewTrack track) => getPlaybackTracks(track).FirstOrDefault();

    private static double getRestoreFadeVolume(BmsPreviewTrack track) => track.RestoreFadeVolume;

    private static IEnumerable<Track> getActiveTracks(BmsPreviewTrack track) => getPlaybackTracks(track);

    private static IReadOnlyList<Track> getPlaybackTracks(BmsPreviewTrack track)
    {
        return track is BmsEventPreviewTrack { Playback: { } playback }
            ? playback.ActiveTracks
            : [];
    }

    private BmsPreviewTrack createTrack(
        IReadOnlyList<BmsSampleEvent> sampleEvents,
        IReadOnlyDictionary<ushort, string> sampleDefinitions,
        string? basePath)
        => new BmsEventPreviewTrack(
            _ => BmsEventPreviewTimeline.Create(
                () => [.. sampleEvents.Select(evt => new BmsPreviewSampleEvent(evt, true))],
                sampleDefinitions),
            basePath,
            audio);

    private BmsPreviewTrack createTrack(
        IReadOnlyList<Func<CancellationToken, BmsEventPreviewTimeline>> timelineSources,
        string basePath)
        => new BmsEventPreviewTrack(timelineSources, basePath, audio);

    private static string getFirstTimelineSamplePath(BmsPreviewTrack track)
        => ((BmsEventPreviewTrack)track).Playback!.Events.First().SamplePath;

    private static void writePcmWave(string path, TimeSpan duration)
    {
        const int sample_rate = 44100;
        const short channels = 1;
        const short bits_per_sample = 16;

        var sampleCount = (int)(sample_rate * duration.TotalSeconds);
        var dataSize = sampleCount * channels * bits_per_sample / 8;

        using var stream = File.Create(path);
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

        for (var i = 0; i < sampleCount; i++)
        {
            var value = (short)(Math.Sin(i * Math.Tau * 440 / sample_rate) * short.MaxValue * 0.2);
            writer.Write(value);
        }
    }

    [BackgroundDependencyLoader]
    private void load(AudioManager audio)
    {
        this.audio = audio;
    }
}
