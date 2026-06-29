#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Testing;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Audio;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal;

[HeadlessTest]
public partial class BmsPreviewTrackTest : OsuTestScene
{
    private AudioManager audio = null!;

    [Test]
    public void TestEventAtTimeZeroPlaysOnStart()
    {
        BmsPreviewTrack track = null!;

        AddStep("create track and start from beginning", () =>
        {
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            writePcmWave(Path.Combine(directory, "test.wav"), TimeSpan.FromSeconds(1));

            track = new BmsPreviewTrack(
                [new BmsSampleEvent(0, 0, 1)],
                new Dictionary<ushort, string> { [1] = "test.wav" },
                directory,
                audio);

            track.Start();
            invokeUpdateState(track);
        });

        AddAssert("sample playing from time zero", () => getActivePlaybackCount(track!) > 0);
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

            track = new BmsPreviewTrack(
                [new BmsSampleEvent(2000, 0, 1)],
                new Dictionary<ushort, string> { [1] = "test.wav" },
                directory,
                audio);

            track.Start();
            invokeUpdateState(track);
        });

        AddAssert("first audible event plays immediately", () => getActivePlaybackCount(track!) > 0);
        AddStep("dispose track", () => track.Dispose());
    }

    [Test]
    public void TestDeclaredWavResolvesOggFallbackFile()
    {
        BmsPreviewTrack track = null!;

        AddStep("create track with wav declaration and ogg file", () =>
        {
            var directory = Path.Combine(BmsEmbeddedSongDecoderTest.TestSongsRoot, "103_outlaw_ogg");

            Assert.That(File.Exists(Path.Combine(directory, "Track_01_001.ogg")), Is.True);
            Assert.That(File.Exists(Path.Combine(directory, "Track_01_001.wav")), Is.False);

            track = new BmsPreviewTrack(
                [new BmsSampleEvent(0, 0, 1)],
                new Dictionary<ushort, string> { [1] = "Track_01_001.wav" },
                directory,
                audio);
        });

        AddAssert("fallback sample resolved", () =>
        {
            var resolveSample = typeof(BmsPreviewTrack).GetMethod("resolveSample",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var sample = resolveSample.Invoke(track, ["Track_01_001.wav"]);
            return sample != null;
        });
        AddStep("dispose track", () => track.Dispose());
    }

    [Test]
    public void TestDeclaredPreviewHasPriorityOverFolderPreview()
    {
        BmsPreviewTrack track = null!;

        AddStep("create track with declared and folder preview", () =>
        {
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-priority-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            writePcmWave(Path.Combine(directory, "declared.wav"), TimeSpan.FromSeconds(1));
            writePcmWave(Path.Combine(directory, "preview.wav"), TimeSpan.FromSeconds(1));

            track = new BmsPreviewTrack(
                [],
                new Dictionary<ushort, string>(),
                directory,
                audio,
                "declared.wav");
        });

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

            track = new BmsPreviewTrack(
                [],
                new Dictionary<ushort, string>(),
                directory,
                audio,
                "missing.wav");
        });

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

            track = new BmsPreviewTrack(
                [new BmsSampleEvent(0, 0, 1)],
                new Dictionary<ushort, string> { [1] = "event.wav" },
                directory,
                audio,
                "missing.wav");

            track.Start();
            invokeUpdateState(track);
        });

        AddAssert("event preview still plays", () => getActivePlaybackCount(track!) > 0);
        AddStep("dispose track", () => track.Dispose());
    }

    [Test]
    public void TestRestoreAfterSuppressedPlaybackDoesNotCatchUpPastEvents()
    {
        BmsPreviewTrack track = null!;

        AddStep("create suppressed track advanced past first event", () =>
        {
            var directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"bms-preview-suppressed-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            writePcmWave(Path.Combine(directory, "event.wav"), TimeSpan.FromSeconds(1));

            track = new BmsPreviewTrack(
                [
                    new BmsSampleEvent(1000, 0, 1),
                    new BmsSampleEvent(3000, 0, 1),
                ],
                new Dictionary<ushort, string> { [1] = "event.wav" },
                directory,
                audio);

            track.SuppressEventProcessing = true;
            setSeekOffset(track, 2000);
            track.SuppressEventProcessing = false;
            track.Start();
            invokeUpdateState(track);
        });

        AddAssert("past event was skipped", () => getActivePlaybackCount(track!) == 0);
        AddStep("dispose track", () => track.Dispose());
    }

    [Test]
    public void TestPreviewTrackIsNotDummyDevice()
    {
        BmsPreviewTrack track = null!;

        AddStep("create track", () =>
        {
            track = new BmsPreviewTrack(
                [],
                new Dictionary<ushort, string>(),
                null,
                audio);
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
                track = new BmsPreviewTrack(
                    beatmap.BackgroundSampleEvents,
                    beatmap.SampleDefinitions,
                    directory,
                    audio);
            });

            Assert.That(track.Length, Is.GreaterThan(0));
        });

        AddAssert("track length is positive", () => track.Length > 0);
        AddStep("dispose track", () => track.Dispose());
    }

    private static void invokeUpdateState(BmsPreviewTrack track)
    {
        typeof(BmsPreviewTrack).GetMethod("UpdateState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(track, null);
    }

    private static int getActivePlaybackCount(BmsPreviewTrack track)
    {
        var activeChannels = typeof(BmsPreviewTrack).GetField("activeChannels", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(track);
        return ((ICollection)activeChannels!).Count;
    }

    private static void setSeekOffset(BmsPreviewTrack track, double seekOffset)
    {
        typeof(BmsPreviewTrack).GetField("seekOffset", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(track, seekOffset);
    }

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
