using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Textures;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Models;
using osu.Game.Rulesets.BmsRuleset.Audio.Preview;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.IO.Resources;
using osu.Game.Rulesets.BmsRuleset.Tests.Normal;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Screens.Play;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[HeadlessTest]
public partial class BmsWorkingBeatmapCacheTest : OsuTestScene
{

    private readonly List<string> createdDirectories = new();
    private GameHost host = null!;
    private AudioManager audio = null!;
    private BeatmapManager iconBeatmapManager = null!;

    [Test]
    public void TestInstallWrapsEachBeatmapManagerResultWithoutReplacingManagerCache()
    {
        AddAssert("first manager returns wrapped BMS beatmap without cache replacement", installAndCheckNewManager);
        AddAssert("second manager returns wrapped BMS beatmap without cache replacement", installAndCheckNewManager);
    }

    [Test]
    public void TestRulesetInitialisationInstallsSongSelectPreviewHook()
    {
        AddStep("initialise ruleset with beatmap manager", () =>
        {
            iconBeatmapManager = createBeatmapManager();

            Child = new DependencyProvidingContainer
            {
                RelativeSizeAxes = Axes.Both,
                CachedDependencies =
                [
                    (typeof(BeatmapManager), iconBeatmapManager),
                ],
                Child = new BmsRuleset().CreateIcon(),
            };
        });

        AddUntilStep("preview hook installed", () =>
        {
            var beatmapSet = new BeatmapSetInfo();
            var beatmapInfo = createBeatmapInfo(beatmapSet, string.Empty, "icon-test.bms");

            return iconBeatmapManager.GetWorkingBeatmap(beatmapInfo) is BmsWorkingBeatmap
                   && getWorkingBeatmapCache(iconBeatmapManager).GetType() == typeof(WorkingBeatmapCache);
        });
    }

    [Test]
    public void TestBmsWorkingBeatmapDoesNotTransferTrack()
    {
        BmsWorkingBeatmap source = null!;
        WorkingBeatmap target = null!;

        AddStep("create working beatmaps", () =>
        {
            source = new BmsWorkingBeatmap(new StubWorkingBeatmap(audio), audio);
            target = new StubWorkingBeatmap(audio);
        });

        AddAssert("transfer refused", () => !source.TryTransferTrack(target));
    }

    [Test]
    public void TestBmsWorkingBeatmapTransfersTrackForSameBeatmapRefetch()
    {
        var beatmapId = Guid.NewGuid();
        BmsWorkingBeatmap source = null!;
        BmsWorkingBeatmap target = null!;
        Track previewTrack = null!;

        AddStep("create refetched working beatmaps", () =>
        {
            var sourceInfo = createBeatmapInfoWithAudio(beatmapId, "same-source.bms");
            var targetInfo = createBeatmapInfoWithAudio(beatmapId, "same-target.bms");

            source = new BmsWorkingBeatmap(new StubWorkingBeatmap(audio, new BmsBeatmap(), sourceInfo), audio);
            target = new BmsWorkingBeatmap(new StubWorkingBeatmap(audio, new BmsBeatmap(), targetInfo), audio);
        });

        AddStep("load source preview", () =>
        {
            previewTrack = source.LoadTrack();
            previewTrack.Start();
        });

        AddAssert("same beatmap transfer accepted", () => source.TryTransferTrack(target));
        AddAssert("target uses active preview", () => ReferenceEquals(target.Track, previewTrack));
        AddAssert("preview still running", () => previewTrack.IsRunning);
    }

    [Test]
    public void TestBmsWorkingBeatmapDisposesPreviousPreviewTrackWhenReplaced()
    {
        var firstDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"bms-preview-first-{Guid.NewGuid()}");
        var secondDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"bms-preview-second-{Guid.NewGuid()}");
        BmsPreviewTrack firstPreview = null!;

        AddStep("create preview directories", () =>
        {
            Directory.CreateDirectory(firstDirectory);
            Directory.CreateDirectory(secondDirectory);
            createdDirectories.Add(firstDirectory);
            createdDirectories.Add(secondDirectory);
        });

        AddStep("load first preview track", () =>
        {
            var first = new BmsWorkingBeatmap(new StubWorkingBeatmap(audio, new BmsBeatmap(), firstDirectory), audio);
            first.LoadTrack();
            firstPreview = BmsWorkingBeatmap.ActivePreviewTrack!;
        });

        AddStep("replace preview track", () =>
        {
            var second = new BmsWorkingBeatmap(new StubWorkingBeatmap(audio, new BmsBeatmap(), secondDirectory), audio);
            second.LoadTrack();
        });

        AddUntilStep("first preview disposed", () => firstPreview.IsDisposed);
    }

    [Test]
    public void TestDrawableRulesetKeepsPreviewUntilGameplayStarts()
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"bms-preview-gameplay-start-{Guid.NewGuid()}");
        BmsPreviewTrack previewTrack = null!;
        GameplayClockContainer gameplayClock = null!;
        BmsDrawableRuleset drawableRuleset = null!;

        AddStep("load active preview track", () =>
        {
            Directory.CreateDirectory(directory);
            createdDirectories.Add(directory);

            var working = new BmsWorkingBeatmap(new StubWorkingBeatmap(audio, new BmsBeatmap(), directory), audio);
            previewTrack = (BmsPreviewTrack)working.LoadTrack();
            Assert.That(previewTrack, Is.TypeOf<BmsEventPreviewTrack>());
            previewTrack.Start();
        });

        AddStep("load drawable ruleset under paused gameplay clock", () =>
        {
            gameplayClock = new GameplayClockContainer(previewTrack, false, false)
            {
                RelativeSizeAxes = Axes.Both,
            };

            Child = gameplayClock;
            gameplayClock.Add(drawableRuleset = new BmsDrawableRuleset(new BmsRuleset(), new BmsBeatmap()));
        });

        AddUntilStep("preview clock remains in preview mode during loading", () =>
            previewTrack.IsRunning
            && previewTrack.PlaybackMode == BmsPreviewTrackPlaybackMode.Preview
            && previewTrack.Volume.Value == 1);
        AddAssert("background audio remains paused during loading", () => getBackgroundAudioPaused(drawableRuleset));

        AddStep("start gameplay clock", () => gameplayClock.Start());
        AddUntilStep("preview remains stopped for gameplay", () =>
            previewTrack.PlaybackMode == BmsPreviewTrackPlaybackMode.GameplayClockOnly
            && previewTrack.Volume.Value == 1
            && getActivePreviewPlaybackCount(previewTrack) == 0);
        AddAssert("gameplay clock track is still running", () => previewTrack.IsRunning);

        AddStep("dispose drawable ruleset while gameplay is running", () => drawableRuleset.RemoveAndDisposeImmediately());
        AddAssert("preview is restored after gameplay disposal", () =>
            previewTrack.PlaybackMode == BmsPreviewTrackPlaybackMode.Preview
            && previewTrack.Volume.Value == 1);
    }

    [Test]
    public void TestDrawableRulesetStopsSingleFilePreviewWhenGameplayStarts()
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"bms-single-file-preview-gameplay-start-{Guid.NewGuid()}");
        BmsPreviewTrack previewTrack = null!;
        GameplayClockContainer gameplayClock = null!;
        BmsDrawableRuleset drawableRuleset = null!;

        AddStep("load active single-file preview track", () =>
        {
            Directory.CreateDirectory(directory);
            createdDirectories.Add(directory);
            writePcmWave(Path.Combine(directory, "preview.wav"), TimeSpan.FromSeconds(5));

            var working = new BmsWorkingBeatmap(new StubWorkingBeatmap(audio, new BmsBeatmap(), directory), audio);
            previewTrack = (BmsPreviewTrack)working.LoadTrack();
            Assert.That(previewTrack, Is.TypeOf<BmsDedicatedPreviewTrack>());
            previewTrack.Start();
        });

        AddUntilStep("single-file preview is playing before loading", () =>
            previewTrack.IsRunning
            && previewTrack.PlaybackMode == BmsPreviewTrackPlaybackMode.Preview
            && previewTrack.Volume.Value == 1
            && getActivePreviewPlaybackCount(previewTrack) == 1
            && getPreviewPlaybackAggregateVolume(previewTrack) > 0
            && getSingleFilePreviewTrack(previewTrack)?.IsRunning == true);

        AddStep("load drawable ruleset under paused gameplay clock", () =>
        {
            gameplayClock = new GameplayClockContainer(previewTrack, false, false)
            {
                RelativeSizeAxes = Axes.Both,
            };

            Child = gameplayClock;
            gameplayClock.Add(drawableRuleset = new BmsDrawableRuleset(new BmsRuleset(), new BmsBeatmap()));
        });

        AddUntilStep("single-file preview continues during loading", () =>
            previewTrack.PlaybackMode == BmsPreviewTrackPlaybackMode.Preview
            && getActivePreviewPlaybackCount(previewTrack) == 1
            && getSingleFilePreviewTrack(previewTrack)?.IsRunning == true);

        AddStep("start gameplay clock", () => gameplayClock.Start());
        AddUntilStep("single-file preview stopped for gameplay", () =>
            previewTrack.PlaybackMode == BmsPreviewTrackPlaybackMode.GameplayClockOnly
            && previewTrack.Volume.Value == 1
            && getActivePreviewPlaybackCount(previewTrack) == 0);
        AddAssert("gameplay clock track is still running", () => previewTrack.IsRunning);

        AddStep("dispose drawable ruleset while gameplay is running", () => drawableRuleset.RemoveAndDisposeImmediately());
        AddUntilStep("single-file preview resumes after gameplay disposal", () =>
            previewTrack.PlaybackMode == BmsPreviewTrackPlaybackMode.Preview
            && getSingleFilePreviewTrack(previewTrack)?.IsRunning == true);
    }

    [Test]
    public void TestDedicatedPreviewDoesNotLimitGameplayClock()
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"bms-short-preview-clock-{Guid.NewGuid()}");
        BmsPreviewTrack previewTrack = null!;

        AddStep("load short dedicated preview as gameplay clock", () =>
        {
            Directory.CreateDirectory(directory);
            createdDirectories.Add(directory);
            writePcmWave(Path.Combine(directory, "preview.wav"), TimeSpan.FromSeconds(1));

            var working = new BmsWorkingBeatmap(new StubWorkingBeatmap(audio, new BmsBeatmap(), directory), audio);
            previewTrack = (BmsPreviewTrack)working.LoadTrack();
            previewTrack.PlaybackMode = BmsPreviewTrackPlaybackMode.GameplayClockOnly;
            previewTrack.Seek(60_000);
            previewTrack.Start();
        });

        AddUntilStep("gameplay clock passes preview length", () => previewTrack.CurrentTime > 60_100);
        AddAssert("gameplay clock remains running", () => previewTrack.IsRunning);
        AddStep("restart gameplay clock past preview length", () =>
        {
            previewTrack.Stop();
            previewTrack.Start();
        });
        AddUntilStep("restarted clock keeps advancing", () => previewTrack.CurrentTime > 60_200);
    }

    [Test]
    public void TestBrokenDedicatedPreviewFallsBackToEvents()
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"bms-broken-preview-fallback-{Guid.NewGuid()}");
        BmsPreviewTrack previewTrack = null!;

        AddStep("load broken dedicated preview with event fallback", () =>
        {
            Directory.CreateDirectory(directory);
            createdDirectories.Add(directory);
            File.WriteAllBytes(Path.Combine(directory, "preview.wav"), [1, 2, 3, 4]);
            writePcmWave(Path.Combine(directory, "bgm.wav"), TimeSpan.FromSeconds(1));

            var beatmap = new BmsBeatmap
            {
                SampleDefinitions = new Dictionary<ushort, string> { [1] = "bgm.wav" },
                BackgroundSampleEvents = [new BmsSampleEvent(0, 0, 1, 100)],
            };

            var working = new BmsWorkingBeatmap(new StubWorkingBeatmap(audio, beatmap, directory), audio);
            previewTrack = (BmsPreviewTrack)working.LoadTrack();
            Assert.That(previewTrack, Is.TypeOf<BmsDedicatedPreviewTrack>());
            previewTrack.Start();
        });

        AddUntilStep("event fallback becomes audible", () => getActivePreviewPlaybackCount(previewTrack) > 0);
    }

    [Test]
    public void TestRestoreCompletedPreviewRestartsFromBeginning()
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"bms-completed-preview-restore-{Guid.NewGuid()}");
        BmsPreviewTrack previewTrack = null!;

        AddStep("load completed single-file preview", () =>
        {
            Directory.CreateDirectory(directory);
            createdDirectories.Add(directory);
            writePcmWave(Path.Combine(directory, "preview.wav"), TimeSpan.FromSeconds(1));

            var working = new BmsWorkingBeatmap(new StubWorkingBeatmap(audio, new BmsBeatmap(), directory), audio);
            previewTrack = (BmsPreviewTrack)working.LoadTrack();
            previewTrack.Seek(previewTrack.Length);
            previewTrack.PlaybackMode = BmsPreviewTrackPlaybackMode.GameplayClockOnly;
        });

        AddStep("restore preview", () => BmsWorkingBeatmap.RestoreActivePreview(null));
        AddUntilStep("preview restarts from beginning", () =>
            previewTrack.PlaybackMode == BmsPreviewTrackPlaybackMode.Preview
            && previewTrack.CurrentTime < previewTrack.Length
            && getSingleFilePreviewTrack(previewTrack)?.IsRunning == true);
    }

    [Test]
    public void TestRestorePreviewSeeksToGameplayTime()
    {
        const double preview_time = 500;
        const double gameplay_time = 2000;

        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"bms-preview-restore-time-{Guid.NewGuid()}");
        BmsPreviewTrack previewTrack = null!;

        AddStep("load preview at initial time", () =>
        {
            Directory.CreateDirectory(directory);
            createdDirectories.Add(directory);
            writePcmWave(Path.Combine(directory, "preview.wav"), TimeSpan.FromSeconds(5));

            var working = new BmsWorkingBeatmap(new StubWorkingBeatmap(audio, new BmsBeatmap(), directory), audio);
            previewTrack = (BmsPreviewTrack)working.LoadTrack();
            previewTrack.Seek(preview_time);
            previewTrack.PlaybackMode = BmsPreviewTrackPlaybackMode.GameplayClockOnly;
        });

        AddStep("restore at gameplay time", () =>
        {
            BmsWorkingBeatmap.RestoreActivePreview(gameplay_time);
            Assert.That(getRestoreFadeStart(previewTrack), Is.GreaterThan(0));
        });
        AddUntilStep("preview resumes from gameplay time", () =>
            previewTrack.PlaybackMode == BmsPreviewTrackPlaybackMode.Preview
            && previewTrack.CurrentTime >= gameplay_time
            && getSingleFilePreviewTrack(previewTrack) is { IsRunning: true, CurrentTime: >= gameplay_time });
        AddUntilStep("restore fades to full volume", () => getRestoreFadeVolume(previewTrack) == 1);
    }

    [Test]
    public void TestSwitchActivePreviewToGameplayClockOnly()
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"bms-clock-only-preview-{Guid.NewGuid()}");
        BmsPreviewTrack previewTrack = null!;

        AddStep("load active event preview track", () =>
        {
            Directory.CreateDirectory(directory);
            createdDirectories.Add(directory);
            writePcmWave(Path.Combine(directory, "bgm.wav"), TimeSpan.FromSeconds(1));

            var beatmap = new BmsBeatmap
            {
                SampleDefinitions = new Dictionary<ushort, string>
                {
                    [1] = "bgm.wav",
                },
                BackgroundSampleEvents =
                [
                    new BmsSampleEvent(0, 0, 1, 100),
                ],
            };

            var working = new BmsWorkingBeatmap(new StubWorkingBeatmap(audio, beatmap, directory), audio);
            previewTrack = (BmsPreviewTrack)working.LoadTrack();
            previewTrack.Start();
        });

        AddUntilStep("event preview is audible", () => getActivePreviewPlaybackCount(previewTrack) > 0);
        AddStep("switch to gameplay clock only", BmsWorkingBeatmap.SwitchActivePreviewToGameplayClockOnly);
        AddAssert("preview track exposes clock-only mode", () => previewTrack.PlaybackMode == BmsPreviewTrackPlaybackMode.GameplayClockOnly);

        AddStep("simulate gameplay reset and start", () =>
        {
            previewTrack.Seek(0);
            previewTrack.Start();
        });

        AddAssert("clock remains running without preview output", () =>
            previewTrack.IsRunning
            && getActivePreviewPlaybackCount(previewTrack) == 0);
    }

    [Test]
    public void TestExternalTextureStoreCacheDoesNotKeepUnusedStoresAlive()
    {
        BmsWorkingBeatmapCache cache = null!;
        WeakReference storeReference = null!;

        AddStep("create external texture store", () =>
        {
            var manager = createBeatmapManager();
            cache = new BmsWorkingBeatmapCache(getWorkingBeatmapCache(manager));
            var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"bms-texture-store-{Guid.NewGuid()}");

            Directory.CreateDirectory(directory);
            createdDirectories.Add(directory);
            storeReference = createExternalTextureStoreReference(cache, directory);
        });

        AddUntilStep("store can be collected", () =>
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.KeepAlive(cache);
            return !storeReference.IsAlive;
        });
    }

    [Test]
    public void TestBmsWorkingBeatmapLoadsSongSelectBackgroundFromExternalDirectory()
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"bms-background-{Guid.NewGuid()}");
        var backgroundPath = Path.Combine(directory, "back.jpg");
        BmsWorkingBeatmap working = null!;
        Texture texture = null!;

        AddStep("create external background file", () =>
        {
            Directory.CreateDirectory(directory);
            createdDirectories.Add(directory);
            File.Copy(Path.Combine(BmsEmbeddedSongDecoderTest.TestSongsRoot, "103_outlaw_ogg", "banner.jpg"), backgroundPath);
        });

        AddStep("create working beatmap", () =>
        {
            var beatmap = new BmsBeatmap
            {
                StageFile = "missing.png",
                BackBmp = "back.jpg",
                Banner = "banner.png",
            };

            var inner = new StubWorkingBeatmap(audio, beatmap, directory);
            var externalTextures = new LargeTextureStore(host.Renderer, host.CreateTextureLoaderStore(new BmsFileResourceStore(directory)));
            working = new BmsWorkingBeatmap(inner, audio, externalTextures);
        });

        AddStep("get background", () => texture = working.GetBackground());
        AddAssert("background loaded", () => texture != null);
        AddAssert("loaded back bmp dimensions", () => texture.Width > 0);
    }

    [Test]
    public void TestBmsWorkingBeatmapConcurrentBackgroundRequestsDoNotObservePartialState()
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"bms-background-concurrent-{Guid.NewGuid()}");
        BmsWorkingBeatmap working = null!;

        AddStep("create working beatmap", () =>
        {
            Directory.CreateDirectory(directory);
            createdDirectories.Add(directory);

            var beatmap = new BmsBeatmap
            {
                StageFile = "missing-stage.png",
                BackBmp = "missing-background.png",
                Banner = "missing-banner.png",
            };

            working = new BmsWorkingBeatmap(new StubWorkingBeatmap(audio, beatmap, directory), audio);
        });

        AddStep("request background concurrently", () =>
        {
            const int request_count = 16;

            using var start = new ManualResetEventSlim();
            using var ready = new CountdownEvent(request_count);
            var requests = Enumerable.Range(0, request_count)
                .Select(_ => Task.Factory.StartNew(() =>
                {
                    ready.Signal();
                    start.Wait();
                    working.GetBackground();
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default))
                .ToArray();

            ready.Wait();
            start.Set();
            Task.WaitAll(requests);
        });
    }

    [Test]
    public void TestBmsWorkingBeatmapPanelBackgroundPrefersBanner()
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"bms-panel-background-{Guid.NewGuid()}");
        BmsWorkingBeatmap working = null!;
        Texture background = null!;
        Texture panelBackground = null!;

        AddStep("create external background files", () =>
        {
            Directory.CreateDirectory(directory);
            createdDirectories.Add(directory);
            createTestTexture(Path.Combine(directory, "stage.png"), 4, 2);
            createTestTexture(Path.Combine(directory, "banner.png"), 2, 4);
        });

        AddStep("create working beatmap", () =>
        {
            var beatmap = new BmsBeatmap
            {
                StageFile = "stage.png",
                Banner = "banner.png",
            };

            var inner = new StubWorkingBeatmap(audio, beatmap, directory);
            var externalTextures = new LargeTextureStore(host.Renderer, host.CreateTextureLoaderStore(new BmsFileResourceStore(directory)));
            working = new BmsWorkingBeatmap(inner, audio, externalTextures);
        });

        AddStep("get backgrounds", () =>
        {
            background = working.GetBackground();
            panelBackground = working.GetPanelBackground();
        });

        AddAssert("main background uses stagefile", () => background is { Width: 4, Height: 2 });
        AddAssert("panel background uses banner", () => panelBackground is { Width: 2, Height: 4 });
        AddAssert("panel marker uses banner", () => Path.GetFileName(working.Metadata.BackgroundFile) == "banner.png");
    }

    [Test]
    public void TestBmsWorkingBeatmapInitialisesExternalBackgroundMetadataBeforeTextureLoad()
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"bms-background-metadata-{Guid.NewGuid()}");
        BmsWorkingBeatmap working = null!;

        AddStep("create external background files", () =>
        {
            Directory.CreateDirectory(directory);
            createdDirectories.Add(directory);
            File.WriteAllBytes(Path.Combine(directory, "stage.png"), []);
            File.WriteAllBytes(Path.Combine(directory, "banner.png"), []);
        });

        AddStep("create working beatmap", () =>
        {
            var beatmapSet = new BeatmapSetInfo();
            var beatmapInfo = createBeatmapInfo(beatmapSet, directory, "metadata.bms");
            var beatmap = new BmsBeatmap
            {
                StageFile = "stage.png",
                Banner = "banner.png",
            };

            working = new BmsWorkingBeatmap(new StubWorkingBeatmap(audio, beatmap, beatmapInfo), audio);
        });

        AddAssert("panel marker is ready before texture load", () => Path.GetFileName(working.Metadata.BackgroundFile) == "banner.png");
        AddAssert("panel marker has file hash", () => working.BeatmapSetInfo.GetFile(working.Metadata.BackgroundFile) != null);
    }

    [Test]
    public void TestBmsWorkingBeatmapDecodesExternalShiftJisChartFromOriginalBytes()
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"bms-shiftjis-{Guid.NewGuid()}");
        const string chart_name = "shiftjis.bms";
        BmsWorkingBeatmap working = null!;
        Track previewTrack = null!;

        AddStep("create shift-jis chart", () =>
        {
            Directory.CreateDirectory(directory);
            createdDirectories.Add(directory);

            const string chart = """
                                 #PLAYER 1
                                 #TITLE Shift JIS
                                 #ARTIST Test
                                 #WAV01 b_accordion (切る)_v100l8o5c.wav
                                 #00111:01
                                 """;

            var shiftJis = CodePagesEncodingProvider.Instance.GetEncoding(932)!;
            File.WriteAllBytes(Path.Combine(directory, chart_name), shiftJis.GetBytes(chart));
        });

        AddStep("create wrapped working beatmap", () =>
        {
            var beatmapSet = new BeatmapSetInfo();
            var beatmapInfo = createBeatmapInfo(beatmapSet, directory, chart_name);

            var innerBeatmap = new BmsBeatmap
            {
                SampleDefinitions = new Dictionary<ushort, string>
                {
                    [1] = "b_accordion (�؂�)_v100l8o5c.wav",
                },
            };

            working = new BmsWorkingBeatmap(new StubWorkingBeatmap(audio, innerBeatmap, beatmapInfo), audio);
        });

        AddAssert("sample path decoded from original bytes", () =>
            ((IBmsBeatmap)working.Beatmap).SampleDefinitions[1] == "b_accordion (切る)_v100l8o5c.wav");

        AddStep("load preview track", () => previewTrack = working.LoadTrack());
        AddAssert("preview uses decoded sample path", () => getFirstPreviewEventSamplePath(previewTrack) == "b_accordion (切る)_v100l8o5c.wav");
    }

    [Test]
    public void TestBmsWorkingBeatmapBackgroundComparisonChangesBetweenExternalBackgrounds()
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"bms-background-compare-{Guid.NewGuid()}");
        BmsWorkingBeatmap first = null!;
        BmsWorkingBeatmap second = null!;
        BmsWorkingBeatmap third = null!;
        BmsWorkingBeatmap fourth = null!;
        BmsWorkingBeatmap fifth = null!;

        AddStep("create working beatmaps", () =>
        {
            Directory.CreateDirectory(directory);
            createdDirectories.Add(directory);
            File.WriteAllBytes(Path.Combine(directory, "first.png"), []);
            File.WriteAllBytes(Path.Combine(directory, "second.png"), []);
            File.WriteAllBytes(Path.Combine(directory, "banner.png"), []);
            File.WriteAllBytes(Path.Combine(directory, "other-banner.png"), []);

            var beatmapSet = new BeatmapSetInfo();
            var firstInfo = createBeatmapInfo(beatmapSet, directory, "first.bms");
            var secondInfo = createBeatmapInfo(beatmapSet, directory, "second.bms");
            var thirdInfo = createBeatmapInfo(beatmapSet, directory, "third.bms");
            var fourthInfo = createBeatmapInfo(beatmapSet, directory, "fourth.bms");
            var fifthInfo = createBeatmapInfo(beatmapSet, directory, "fifth.bms");

            first = new BmsWorkingBeatmap(new StubWorkingBeatmap(audio, new BmsBeatmap { BackBmp = "first.png", Banner = "banner.png" }, firstInfo), audio);
            second = new BmsWorkingBeatmap(new StubWorkingBeatmap(audio, new BmsBeatmap { BackBmp = "second.png", Banner = "banner.png" }, secondInfo), audio);
            third = new BmsWorkingBeatmap(new StubWorkingBeatmap(audio, new BmsBeatmap { BackBmp = "first.png", Banner = "banner.png" }, thirdInfo), audio);
            fourth = new BmsWorkingBeatmap(new StubWorkingBeatmap(audio, new BmsBeatmap { BackBmp = "first.png", Banner = "other-banner.png" }, fourthInfo), audio);
            fifth = new BmsWorkingBeatmap(new StubWorkingBeatmap(audio, new BmsBeatmap { BackBmp = "second.png", Banner = "other-banner.png" }, fifthInfo), audio);
        });

        AddAssert("main backgrounds compare different", () => !first.BeatmapInfo.BackgroundEquals(second.BeatmapInfo));
        AddAssert("panel backgrounds compare different", () => !first.BeatmapInfo.BackgroundEquals(fourth.BeatmapInfo));
        AddAssert("both backgrounds compare different", () => !first.BeatmapInfo.BackgroundEquals(fifth.BeatmapInfo));
        AddAssert("same background compares equal", () => first.BeatmapInfo.BackgroundEquals(third.BeatmapInfo));
    }

    [TearDown]
    public void TearDownExternalBackgroundDirectories()
    {
        // The background tests create Guid-named directories under the NUnit work dir to
        // host external image assets; remove them so they don't accumulate across runs.
        foreach (var directory in createdDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup — a leftover directory should not fail the test run.
            }
        }

        createdDirectories.Clear();
    }

    private static WorkingBeatmapCache getWorkingBeatmapCache(BeatmapManager manager) =>
        (WorkingBeatmapCache)typeof(BeatmapManager)
            .GetField("workingBeatmapCache", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(manager)!;

    private static void createTestTexture(string path, int width, int height)
    {
        var image = new Image<Rgba32>(width, height);
        image.SaveAsPng(path);
        image.Dispose();
    }

    private static BeatmapInfo createBeatmapInfo(BeatmapSetInfo beatmapSet, string sourceDirectory, string filename)
    {
        var beatmap = new BeatmapInfo(new BmsRuleset().RulesetInfo)
        {
            BeatmapSet = beatmapSet,
            Hash = filename,
            Metadata =
            {
                Source = sourceDirectory,
            },
        };

        beatmapSet.Beatmaps.Add(beatmap);
        beatmapSet.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = beatmap.Hash }, filename));

        return beatmap;
    }

    private static BeatmapInfo createBeatmapInfoWithAudio(Guid id, string filename)
    {
        var beatmapSet = new BeatmapSetInfo();
        var beatmap = createBeatmapInfo(beatmapSet, string.Empty, filename);
        beatmap.ID = id;
        beatmap.Metadata.AudioFile = "audio.mp3";
        beatmapSet.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = "audio-hash" }, "audio.mp3"));
        return beatmap;
    }

    private static WeakReference createExternalTextureStoreReference(BmsWorkingBeatmapCache cache, string directory)
    {
        var store = typeof(BmsWorkingBeatmapCache)
            .GetMethod("getOrCreateExternalTextureStore", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(cache, [directory]);

        return new WeakReference(store);
    }

    private static string getFirstPreviewEventSamplePath(Track track)
    {
        var playback = getEventPlayback((BmsPreviewTrack)track)!;
        var events = (IEnumerable)typeof(BmsEventPreviewPlayback)
            .GetField("sortedEvents", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(playback)!;

        var first = events.Cast<object>().FirstOrDefault();
        return first?.GetType().GetProperty("SamplePath")?.GetValue(first) as string ?? string.Empty;
    }

    private static int getActivePreviewPlaybackCount(BmsPreviewTrack track)
    {
        var eventPlayback = getEventPlayback(track);
        var activeTracks = eventPlayback != null
            ? (IEnumerable)typeof(BmsEventPreviewPlayback)
                .GetField("activeTracks", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(eventPlayback)!
            : Array.Empty<Track>();

        var previewTrack = getSingleFilePreviewTrack(track);

        return activeTracks.Cast<Track>().Count(active => active.IsRunning)
               + (previewTrack?.IsRunning == true ? 1 : 0);
    }

    private static double getPreviewPlaybackAggregateVolume(BmsPreviewTrack track) =>
        getSingleFilePreviewTrack(track)?.AggregateVolume.Value ?? 0;

    private static double getRestoreFadeVolume(BmsPreviewTrack track) =>
        ((BindableDouble)typeof(BmsPreviewTrack)
            .GetField("restoreFadeVolume", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(track)!).Value;

    private static long getRestoreFadeStart(BmsPreviewTrack track) =>
        (long)typeof(BmsPreviewTrack)
            .GetField("restoreFadeStart", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(track)!;

    private static Track getSingleFilePreviewTrack(BmsPreviewTrack track) =>
        track is BmsDedicatedPreviewTrack
            ? typeof(BmsDedicatedPreviewTrack)
                .GetField("previewTrack", BindingFlags.Instance | BindingFlags.NonPublic)?
                .GetValue(track) as Track
            : null!;

    private static BmsEventPreviewPlayback getEventPlayback(BmsPreviewTrack track)
    {
        if (track is BmsEventPreviewTrack)
        {
            return (BmsEventPreviewPlayback)typeof(BmsEventPreviewTrack)
                .GetField("playback", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(track)!;
        }

        return typeof(BmsDedicatedPreviewTrack)
            .GetField("fallbackPlayback", BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(track) as BmsEventPreviewPlayback;
    }

    private static bool getBackgroundAudioPaused(BmsDrawableRuleset drawableRuleset) =>
        ((BindableBool)typeof(BmsDrawableRuleset)
            .GetField("backgroundAudioPaused", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(drawableRuleset)!).Value;

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
    private void load(GameHost host, AudioManager audio)
    {
        this.host = host;
        this.audio = audio;
    }

    private bool installAndCheckNewManager()
    {
        var manager = createBeatmapManager();
        var beatmapSet = new BeatmapSetInfo();
        var beatmapInfo = createBeatmapInfo(beatmapSet, string.Empty, "install-test.bms");

        return BmsWorkingBeatmapPatcher.InstallOnce()
               && manager.GetWorkingBeatmap(beatmapInfo) is BmsWorkingBeatmap
               && getWorkingBeatmapCache(manager).GetType() == typeof(WorkingBeatmapCache);
    }

    private BeatmapManager createBeatmapManager() => new(LocalStorage, Realm, null, audio, Resources, host, Beatmap.Default);

    private class StubWorkingBeatmap : WorkingBeatmap
    {
        private readonly IBeatmap beatmap = null!;

        public StubWorkingBeatmap(AudioManager audioManager)
            : base(new BeatmapInfo(), audioManager)
        {
        }

        public StubWorkingBeatmap(AudioManager audioManager, IBeatmap beatmap, string sourceDirectory)
            : base(new BeatmapInfo
            {
                Metadata =
                {
                    Source = sourceDirectory,
                },
            }, audioManager)
        {
            this.beatmap = beatmap;
        }

        public StubWorkingBeatmap(AudioManager audioManager, IBeatmap beatmap, BeatmapInfo beatmapInfo)
            : base(beatmapInfo, audioManager)
        {
            this.beatmap = beatmap;
        }

        // The wrapper serves backgrounds from the external texture store; this is only hit as a
        // fallback (e.g. when a test has no external store), so return null rather than throw.
        public override Texture GetBackground() => null!;

        public override Stream GetStream(string storagePath) => throw new NotSupportedException();

        protected override IBeatmap GetBeatmap() => beatmap ?? throw new NotSupportedException();

        protected override Track GetBeatmapTrack() => throw new NotSupportedException();

        protected override ISkin GetSkin() => throw new NotSupportedException();
    }
}
