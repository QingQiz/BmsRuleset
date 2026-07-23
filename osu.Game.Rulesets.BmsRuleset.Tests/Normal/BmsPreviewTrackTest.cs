#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
                () => [new BmsPreviewSampleEvent(new BmsSampleEvent(0, 0, 1, 100), false)],
                new Dictionary<ushort, string> { [1] = "test.wav" },
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
                () =>
                {
                    timelineGate.Wait();
                    return [new BmsPreviewSampleEvent(new BmsSampleEvent(0, 0, 1, 100), false)];
                },
                new Dictionary<ushort, string> { [1] = "test.wav" },
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
    public void TestEventIsNotSkippedWhenTimelinePreparationExceedsSampleLength()
    {
        BmsPreviewTrack track = null!;
        var timelineGate = new ManualResetEventSlim();

        AddStep("start before timeline is ready", () =>
        {
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-delayed-sample-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            writePcmWave(Path.Combine(directory, "test.wav"), TimeSpan.FromSeconds(1));

            track = new BmsEventPreviewTrack(
                () =>
                {
                    timelineGate.Wait();
                    return [new BmsPreviewSampleEvent(new BmsSampleEvent(0, 0, 1, 100), false)];
                },
                new Dictionary<ushort, string> { [1] = "test.wav" },
                directory,
                audio);

            audio.AddItem(track);
            track.Start();
        });

        AddUntilStep("sample duration elapses", () => track.CurrentTime > 1000);
        AddStep("complete timeline", timelineGate.Set);
        AddUntilStep("delayed event still plays", () => getActivePlaybackCount(track) > 0);
        AddStep("dispose track", () =>
        {
            track.Dispose();
            timelineGate.Dispose();
        });
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

            track = new BmsDedicatedPreviewTrack(directory, "declared.wav", audio);
            audio.AddItem(track);
        });

        AddUntilStep("dedicated preview is used", () => usesDedicatedPreviewAudio(track));
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
                () =>
                {
                    eventFactoryInvoked = true;
                    return [new BmsPreviewSampleEvent(new BmsSampleEvent(0, 0, 1, 100), true)];
                },
                new Dictionary<ushort, string> { [1] = "event.wav" },
                directory,
                audio);

            audio.AddItem(track);
            track.Start();
        });

        AddAssert("dedicated preview is skipped", () => !usesDedicatedPreviewAudio(track));
        AddAssert("event factory is invoked", () => eventFactoryInvoked);
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

            track = new BmsDedicatedPreviewTrack(directory, "missing.wav", audio);
            audio.AddItem(track);
            track.Start();
        });

        AddUntilStep("folder preview is used", () => usesDedicatedPreviewAudio(track));
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

            track = new BmsDedicatedPreviewTrack(
                directory,
                "preview.wav",
                audio,
                _ =>
                {
                    fallbackStarted.Set();
                    fallbackGate.Wait();
                    return new BmsEventPreviewTimeline(
                        [new BmsPreviewTimelineEntry(0, 1, "event.wav", 100, false)],
                        1000);
                });

            audio.AddItem(track);
            track.Start();
        });

        AddUntilStep("fallback timeline requested", () => fallbackStarted.IsSet);
        AddStep("complete fallback timeline", fallbackGate.Set);
        AddUntilStep("fallback plays from original start position", () => getActivePlaybackCount(track) > 0);
        AddStep("dispose track", () =>
        {
            track.Dispose();
            fallbackStarted.Dispose();
            fallbackGate.Dispose();
        });
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
            setSeekOffset(track, 1500);
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

    private static int getActivePlaybackCount(BmsPreviewTrack track)
    {
        var activeTracks = getPlaybackTracks(track);
        var count = 0;

        for (var i = 0; i < activeTracks.Count; i++)
        {
            if (((Track)activeTracks[i]!).IsRunning)
                count++;
        }

        return count;
    }

    private static Track? getActiveTrack(BmsPreviewTrack track)
    {
        var activeTracks = getPlaybackTracks(track);

        if (activeTracks.Count == 0)
            return null;

        return (Track?)activeTracks[0];
    }

    private static IEnumerable<Track> getActiveTracks(BmsPreviewTrack track)
    {
        var activeTracks = getPlaybackTracks(track);

        for (var i = 0; i < activeTracks.Count; i++)
            yield return (Track)activeTracks[i]!;
    }

    private static void setSeekOffset(BmsPreviewTrack track, double seekOffset)
    {
        typeof(BmsPreviewTrack).GetField("seekOffset", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(track, seekOffset);
    }

    private static IList getPlaybackTracks(BmsPreviewTrack track)
    {
        var playback = track switch
        {
            BmsEventPreviewTrack => typeof(BmsEventPreviewTrack)
                .GetField("playback", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(track),
            BmsDedicatedPreviewTrack => typeof(BmsDedicatedPreviewTrack)
                .GetField("fallbackPlayback", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(track),
            _ => null,
        };

        if (playback == null)
            return Array.Empty<object>();

        return (IList)typeof(BmsEventPreviewPlayback)
            .GetField("activeTracks", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(playback)!;
    }

    private BmsPreviewTrack createTrack(
        IReadOnlyList<BmsSampleEvent> sampleEvents,
        IReadOnlyDictionary<ushort, string> sampleDefinitions,
        string? basePath)
        => new BmsEventPreviewTrack(
            () => sampleEvents.Select(evt => new BmsPreviewSampleEvent(evt, true)).ToArray(),
            sampleDefinitions,
            basePath,
            audio);

    private static bool usesDedicatedPreviewAudio(BmsPreviewTrack track) =>
        track is BmsDedicatedPreviewTrack
        && typeof(BmsDedicatedPreviewTrack).GetField("previewTrack", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(track) != null;

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
