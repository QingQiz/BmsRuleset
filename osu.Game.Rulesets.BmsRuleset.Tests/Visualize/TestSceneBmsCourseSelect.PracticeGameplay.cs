using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Pooling;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.IO.Import;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Tests.Audio;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Course;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Practice;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsCourseSelect
{
    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void TestPracticeWaitsForSelectedLongNoteTail(BmsLongNoteMode mode)
    {
        var directory = string.Empty;
        Task import = null!;
        BmsPracticeSession session = null!;
        PracticeIsolationScreen practice = null!;
        PracticeIsolationPlayer player = null!;

        AddStep("import practice long note", () =>
        {
            directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"practice-tail-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "chart.bms"), $"""
                #PLAYER 1
                #TITLE Practice Tail
                #ARTIST Test
                #BPM 120
                #PLAYLEVEL 1
                #DIFFICULTY 1
                #LNMODE {(int)mode}
                #00051:0001
                #00251:01
                #03011:01
                """);
            import = new BmsFileImporter(Realm, LocalStorage).Import(directory);
        });
        AddUntilStep("chart imported", () => import.IsCompleted);
        AddStep("open song select", () =>
        {
            import.GetAwaiter().GetResult();
            Stack.Push(songSelect = new BmsSoloSongSelect());
        });
        AddUntilStep("song select loaded", () => songSelect.IsLoaded && Stack.CurrentScreen == songSelect);
        AddStep("practice only long-note head range", () =>
        {
            var info = Realm.Run(realm => realm.All<BeatmapInfo>().AsEnumerable().Single(chart => chart.Metadata.Source == directory).Detach());
            var source = beatmaps.GetWorkingBeatmap(info);
            Beatmap.Value = source;
            session = new BmsPracticeSession(source, new BmsPracticeSettings { StartTime = 1000, EndTime = 1500, CountIn = 0 }, []);
            Stack.Push(practice = new PracticeIsolationScreen(session));
        });
        AddUntilStep("practice starts without count-in", () => session.Attempts == 1 && Stack.CurrentScreen is PracticeIsolationPlayer);
        AddStep("seek beyond selection end", () =>
        {
            player = practice.Players.Single();
            var hold = player.GameplayState.Beatmap.HitObjects.OfType<BmsLongNote>().Single();
            Assert.That(hold.StartTime, Is.EqualTo(1000));
            Assert.That(hold.EndTime, Is.EqualTo(4000));
            Assert.That(player.GameplayState.Beatmap.HitObjects, Has.Count.EqualTo(1));
            Assert.That(player.ChildrenOfType<GameplayClockContainer>().Single().StartTime, Is.LessThan(session.Settings.StartTime - 100));
            Assert.That(player.GameplayState.ScoreProcessor.JudgedHits, Is.Zero);
            player.ChildrenOfType<GameplayClockContainer>().Single().Seek(2500);
        });
        AddUntilStep("clock beyond selected end", () => player.ChildrenOfType<GameplayClockContainer>().Single().CurrentTime >= 2500);
        AddAssert("tail still keeps attempt active", () => Stack.CurrentScreen == player && practice.LastResult == null);
        AddStep("finish complete tail and judgement window", () => player.ChildrenOfType<GameplayClockContainer>().Single().Seek(4500));
        AddUntilStep("practice result returns", () => Stack.CurrentScreen == practice && practice.LastResult != null);
        AddAssert("long note endpoint count complete", () => practice.LastResult!.Score.Statistics.Values.Sum(),
            () => Is.EqualTo(mode == BmsLongNoteMode.LongNote ? 1 : 2));
        AddAssert("practice imported no score", () => player.ImportCount == 0 && Realm.Run(realm => realm.All<ScoreInfo>().Count()) == scoreCountAtTestStart);
        AddStep("return from practice", () => practice.Exit());
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void TestPracticeCountdownLoopIsolationAndAbort(bool retryAndCancel, bool quickRetry)
    {
        var directory = string.Empty;
        Task import = null!;
        WorkingBeatmap source = null!;
        BmsPracticeSession session = null!;
        PracticeIsolationScreen practice = null!;
        PracticeIsolationPlayer first = null!;
        PracticeIsolationPlayer second = null!;
        BmsPlayfield firstPlayfield = null!;
        double countdownRemaining = 0;
        double preparedTime = 0;
        int offsetsBefore = 0;
        var adjustRequested = false;

        AddStep("import practice chart", () =>
        {
            directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"practice-runtime-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            writePracticeWave(Path.Combine(directory, "key.wav"));
            File.Copy(Path.Combine(directory, "key.wav"), Path.Combine(directory, "bgm.wav"));
            File.WriteAllText(Path.Combine(directory, "chart.bms"), $"""
                #PLAYER 1
                #TITLE Practice Runtime {Guid.NewGuid():N}
                #ARTIST Test
                #BPM 120
                #PLAYLEVEL 1
                #DIFFICULTY 1
                #WAV01 key.wav
                #WAV02 bgm.wav
                #01001:0002
                #01011:0001
                #01111:0001
                #03011:01
                """);
            import = new BmsFileImporter(Realm, LocalStorage).Import(directory);
        });
        AddUntilStep("chart imported", () => import.IsCompleted);
        AddStep("open song select", () =>
        {
            import.GetAwaiter().GetResult();
            Stack.Push(songSelect = new BmsSoloSongSelect());
        });
        AddUntilStep("song select loaded", () => songSelect.IsLoaded && Stack.CurrentScreen == songSelect);
        AddStep("start isolated practice preparation", () =>
        {
            var info = Realm.Run(realm => realm.All<BeatmapInfo>().AsEnumerable().Single(chart => chart.Metadata.Source == directory).Detach());
            source = beatmaps.GetWorkingBeatmap(info);
            Beatmap.Value = source;
            SelectedMods.Value = [new BmsModMirror()];
            offsetsBefore = BmsRulesetRuntime.VisualOffsetSuggestions.History.Count;
            session = new BmsPracticeSession(source, new BmsPracticeSettings { StartTime = 21000, EndTime = 27000, SkipResults = true, CountIn = 5 },
                [new BmsModHazardGauge(), new BmsModDoubleTime { SpeedChange = { Value = 1.25 } }]);
            Stack.Push(practice = new PracticeIsolationScreen(session));
            practice.AdjustRangeRequested += () => adjustRequested = true;
        });
        AddUntilStep("first player prepared", () => practice.Countdown.IsReady);
        AddStep("pause countdown on focus loss", () =>
        {
            practice.WindowActive = false;
            practice.PauseCountdown();
            countdownRemaining = practice.Countdown.RemainingMilliseconds;
            first = practice.Players[0];
            firstPlayfield = (BmsPlayfield)first.PracticeRuleset.Playfield;
            preparedTime = first.ChildrenOfType<GameplayClockContainer>().Single().CurrentTime;
            Assert.That(first.GameplayState.HealthProcessor.IsLoaded, Is.True);
            Assert.That(first.GameplayState.ScoreProcessor.IsLoaded, Is.True);
            Assert.That(((BmsHealthProcessor)first.GameplayState.HealthProcessor).GaugeType, Is.EqualTo(BmsGaugeType.Hazard));
            Assert.That(firstPlayfield.IsLoaded, Is.True);
            var notePool = firstPlayfield.ChildrenOfType<DrawablePool<DrawableBmsNote<Column1>>>().Single();
            Assert.That(notePool.CountAvailable, Is.GreaterThan(0));
            var firstDrawable = notePool.Get();
            try
            {
                // Preroll can precede the first lifetime; its pooled representation must already be ready.
                Assert.That(firstDrawable.LoadState, Is.GreaterThanOrEqualTo(LoadState.Ready));
            }
            finally
            {
                firstDrawable.Return();
            }
            Assert.That(first.PracticeRuleset.SamplePlayback.IsSampleReady(2), Is.True);
            Assert.That(first.PracticeRuleset.KeyBindingInputManager.UseParentInput, Is.False);
        });
        AddWaitStep("paused count in stays still", 3);
        AddStep("focus return needs explicit resume", () => practice.WindowActive = true);
        AddWaitStep("focus alone cannot resume", 3);
        AddUntilStep("first prepared BGM fully decoded", () => BmsAudioTestAccess.GetDecodedSampleDuration(first.PracticeRuleset.SamplePlayback, 2).HasValue);
        AddAssert("first prepared BGM already uses selected rate", () => BmsAudioTestAccess.GetDecodedSampleDuration(first.PracticeRuleset.SamplePlayback, 2),
            () => Is.EqualTo(800).Within(60));
        AddAssert("countdown remains frozen", () => practice.Countdown.RemainingMilliseconds, () => Is.EqualTo(countdownRemaining));
        AddAssert("prepared native time remains frozen", () => first.ChildrenOfType<GameplayClockContainer>().Single().CurrentTime, () => Is.EqualTo(preparedTime));
        AddAssert("no attempt started or settings saved", () => session.Attempts == 0
            && new BmsPracticeSettingsStore(Dependencies.Get<osu.Framework.Platform.Storage>()).Load(source.BeatmapInfo.Hash) == null);
        AddStep("skip cannot resume suspended countdown", () => practice.SkipCountdown());
        AddAssert("prepared player keeps gameplay clock stopped", () => Stack.CurrentScreen is PracticeIsolationPlayer { HasAttemptStarted: false }
            && practice.Players[0].ChildrenOfType<GameplayClockContainer>().Single().IsPaused.Value
            && practice.Players[0].PracticeRuleset.BackgroundAudioPaused.Value);
        AddStep("resume and skip countdown", () =>
        {
            practice.ResumeCountdown();
            practice.SkipCountdown();
        });
        AddUntilStep("first attempt entered", () => session.Attempts == 1 && Stack.CurrentScreen is PracticeIsolationPlayer);
        AddStep("verify interval and replay isolation", () =>
        {
            first = practice.Players[0];
            firstPlayfield = first.ChildrenOfType<BmsPlayfield>().Single();
            Assert.That(first.GameplayState.Beatmap.HitObjects, Has.Count.EqualTo(2));
            Assert.That(first.GameplayState.Beatmap.HitObjects.Max(hit => hit.StartTime), Is.EqualTo(23000));
            Assert.That(first.ChildrenOfType<BmsDrawableRuleset>().Single().KeyBindingInputManager is BmsInputManager { Recorder: null }, Is.True);
            Assert.That(first.Score.Replay.Frames, Is.Empty);
            Assert.That(first.Score.ScoreInfo.Mods.OfType<BmsModBranchReplay>(), Is.Empty);
            Assert.That(first.ChildrenOfType<osu.Game.Rulesets.UI.ModIcon>().Any(icon => icon.Mod is BmsModBranchReplay), Is.False);
            Assert.That(first.GameplayState.ScoreProcessor.JudgedHits, Is.Zero);
            Assert.That(((BmsHealthProcessor)first.GameplayState.HealthProcessor).GaugeType, Is.EqualTo(BmsGaugeType.Hazard));
            Assert.That(((BmsHealthProcessor)first.GameplayState.HealthProcessor).Health.Value, Is.EqualTo(1));
            Assert.That(first.ChildrenOfType<GameplayClockContainer>().Single().Rate, Is.EqualTo(1.25));
            Assert.That(firstPlayfield.ScrollController.PlaybackRate, Is.EqualTo(1.25));
            Assert.That(BmsGameplayAudioController.GetPlaybackRate(session.Mods), Is.EqualTo(1.25));
            Assert.That(new BmsPracticeSettingsStore(Dependencies.Get<osu.Framework.Platform.Storage>()).Load(source.BeatmapInfo.Hash), Is.EqualTo(session.Settings));
            first.ChildrenOfType<GameplayClockContainer>().Single().Seek(21300);
        });
        AddUntilStep("hazard exhausted by first missed note", () => ((BmsHealthProcessor)first.GameplayState.HealthProcessor).HasEverFailed);
        AddAssert("zero gauge keeps same attempt active", () => Stack.CurrentScreen == first && !first.GameplayState.HasFailed);
        AddStep("finish remaining selected note", () => first.ChildrenOfType<GameplayClockContainer>().Single().Seek(23400));
        AddUntilStep("clock beyond last selected note", () => first.ChildrenOfType<GameplayClockContainer>().Single().CurrentTime >= 23400);
        AddAssert("selected silent tail keeps attempt active", () => Stack.CurrentScreen == first && practice.LastResult == null);
        AddStep("finish the complete selected interval", () => first.ChildrenOfType<GameplayClockContainer>().Single().Seek(27400));
        AddUntilStep("countdown appears at the start of the reloaded playfield", () => practice.Players.Count == 2 && Stack.CurrentScreen == practice.Players[1]
            && practice.LastResult != null && practice.Countdown.IsReady);
        AddStep("freeze playfield countdown and verify results", () =>
        {
            practice.PauseCountdown();
            second = practice.Players[1];
            Assert.That(second.CountInOverlay.State.Value, Is.EqualTo(Visibility.Visible));
            var reloadedClock = second.ChildrenOfType<GameplayClockContainer>().Single();
            Assert.That(reloadedClock.IsPaused.Value, Is.True);
            Assert.That(reloadedClock.CurrentTime, Is.LessThan(session.Settings.StartTime));
            Assert.That(second.PracticeRuleset.KeyBindingInputManager.UseParentInput, Is.False);
            Assert.That(second.PracticeRuleset.BackgroundAudioPaused.Value, Is.True);
            Assert.That(practice.SelectedScore.Value, Is.Null);
            Assert.That(practice.LastResult!.Score.Statistics.Values.Sum(), Is.GreaterThan(0));
            Assert.That(practice.LastResult.Score.Mods.OfType<BmsModBranchReplay>(), Is.Empty);
            Assert.That(practice.LastResult.Gauges.Any(gauge => gauge.Failed), Is.True);
            Assert.That(practice.LastResult.GaugeHistory.Any(entry => entry.States.Any(gauge => gauge.Failed)), Is.True);
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.HasAttemptStarted, Is.False);
            Assert.That(second.GameplayState.ScoreProcessor.JudgedHits, Is.Zero);
            Assert.That(session.Attempts, Is.EqualTo(1));
        });
        AddAssert("next player is already current during count-in", () => Stack.CurrentScreen == second && practice.PendingPlayer == null);
        AddStep("start second attempt", () =>
        {
            practice.ResumeCountdown();
            practice.SkipCountdown();
        });
        AddUntilStep("second attempt entered", () => session.Attempts == 2 && Stack.CurrentScreen == second && second.HasAttemptStarted);
        AddStep("verify reset gameplay state", () =>
        {
            Assert.That(second.GameplayState.ScoreProcessor.JudgedHits, Is.Zero);
            Assert.That(second.GameplayState.ScoreProcessor.Combo.Value, Is.Zero);
            Assert.That(((BmsHealthProcessor)second.GameplayState.HealthProcessor).HasEverFailed, Is.False);
            Assert.That(((BmsHealthProcessor)second.GameplayState.HealthProcessor).GaugeType, Is.EqualTo(BmsGaugeType.Hazard));
            Assert.That(((BmsHealthProcessor)second.GameplayState.HealthProcessor).Health.Value, Is.EqualTo(1));
            var adjustments = second.ChildrenOfType<GameplayClockContainer>().Single().AdjustmentsFromMods;
            Assert.That(adjustments.AggregateFrequency.Value * adjustments.AggregateTempo.Value, Is.EqualTo(1.25));
            Assert.That(((BmsPlayfield)second.PracticeRuleset.Playfield).ScrollController.PlaybackRate, Is.EqualTo(1.25));
            Assert.That(second.PracticeRuleset.SamplePlayback.IsSampleReady(2), Is.True);
            Assert.That(second.GameplayState.HealthProcessor.IsLoaded, Is.True);
            Assert.That(second.ChildrenOfType<BmsDrawableRuleset>().Single().Playfield, Is.Not.SameAs(firstPlayfield));
            Assert.That(Realm.Run(realm => realm.All<ScoreInfo>().Count()), Is.EqualTo(scoreCountAtTestStart));
            Assert.That(BmsRulesetRuntime.VisualOffsetSuggestions.History.Count, Is.EqualTo(offsetsBefore));
        });
        AddUntilStep("second prepared BGM fully decoded", () => BmsAudioTestAccess.GetDecodedSampleDuration(second.PracticeRuleset.SamplePlayback, 2).HasValue);
        AddAssert("second prepared BGM already uses selected rate", () => BmsAudioTestAccess.GetDecodedSampleDuration(second.PracticeRuleset.SamplePlayback, 2),
            () => Is.EqualTo(800).Within(60));
        AddAssert("second running clock uses selected rate", () => second.ChildrenOfType<GameplayClockContainer>().Single().Rate, () => Is.EqualTo(1.25));
        if (retryAndCancel)
        {
            if (quickRetry)
                AddStep("hold native quick retry", () => InputManager.PressKey(Key.Tilde));
            else
            {
                AddStep("pause second attempt", () => Assert.That(second.Pause(), Is.True));
                AddStep("retry through real pause button", () => second.ChildrenOfType<PauseOverlay>().Single().Buttons
                    .Single(button => button.Text.Equals(BmsStrings.PracticeRetry)).TriggerClick());
            }
            AddUntilStep("third fresh player ready for count-in", () => practice.Players.Count == 3 && practice.Countdown.IsReady);
            if (quickRetry)
                AddStep("release native quick retry", () => InputManager.ReleaseKey(Key.Tilde));
            AddStep("pause third count-in", () => practice.PauseCountdown());
            AddAssert("retry discarded incomplete and old result", () => session.Attempts == 2 && practice.LastResult == null
                && practice.Players[2].GameplayState.ScoreProcessor.JudgedHits == 0);
            AddStep("adjust range cancels prepared player", () => practice.AdjustRange());
            AddUntilStep("range adjustment requested", () => adjustRequested);
            AddWaitStep("old countdown cannot start after cancellation", 3);
            AddAssert("cancelled preparation did not count attempt", () => session.Attempts == 2 && practice.Players.Count == 3);
        }
        else
        {
            AddStep("Escape pauses the active attempt immediately", () => InputManager.Key(Key.Escape));
            AddUntilStep("pause overlay appears without holding Escape", () => second.ChildrenOfType<PauseOverlay>().Single().State.Value == Visibility.Visible);
            AddStep("quit from the pause menu", () => second.ChildrenOfType<PauseOverlay>().Single().Buttons.Last().TriggerClick());
            AddUntilStep("aggregate practice results open", () => Stack.CurrentScreen is BmsPracticeResultsScreen);
            AddUntilStep("aggregate statistics fit the shared result layout", () =>
                TestSceneBmsResultScreenStatistics.ChartsFitViewport((Drawable)Stack.CurrentScreen!));
            AddAssert("aggregate uses one statistics panel", () => ((Drawable)Stack.CurrentScreen!).ChildrenOfType<BmsStatisticsPanel>().Count(), () => Is.EqualTo(1));
            AddAssert("aggregate does not display branch state", () => ((Drawable)Stack.CurrentScreen!).ChildrenOfType<osu.Game.Rulesets.UI.ModIcon>()
                .Any(icon => icon.Mod is BmsModBranchReplay), () => Is.False);
            AddAssert("aggregate does not compare against full-chart scores", () => ((Drawable)Stack.CurrentScreen!).ChildrenOfType<BmsResultComparisonButton>(), () => Is.Empty);
            AddAssert("completed rounds are retained in the session", () => session.AttemptResults.Count, () => Is.EqualTo(1));
            AddAssert("aggregate page shows one selectable attempt card", () => ((Drawable)Stack.CurrentScreen!).ChildrenOfType<BmsPracticeAttemptCard>().Count(), () => Is.EqualTo(1));
            AddAssert("aggregate page does not expose course stage selection", () => ((Drawable)Stack.CurrentScreen!).ChildrenOfType<BmsCourseStageCard>().Count(), () => Is.Zero);
            AddStep("return to practice settings from aggregate results", () => ((Drawable)Stack.CurrentScreen!).ChildrenOfType<BmsCourseResultButton>()
                .Single(button => button.Name == "Return to practice settings button").TriggerClick());
            AddUntilStep("song select restored", () => Stack.CurrentScreen == songSelect);
        }
        AddAssert("no score import attempted", () => practice.Players.All(player => player.ImportCount == 0));
        AddUntilStep("song select restored", () => Stack.CurrentScreen == songSelect);
        AddAssert("original mods restored", () => SelectedMods.Value.Single() is BmsModMirror);
        AddAssert("practice still saved no scores", () => Realm.Run(realm => realm.All<ScoreInfo>().Count()), () => Is.EqualTo(scoreCountAtTestStart));
    }

    [TestCase(0.75)]
    [TestCase(1.25)]
    public void TestPracticePreparesBackgroundSpanningPrerollBeforeCountdown(double rate)
    {
        var directory = string.Empty;
        Task import = null!;
        BmsPracticeSession session = null!;
        PracticeIsolationScreen practice = null!;
        AddStep("import long background chart", () =>
        {
            directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"practice-audio-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            writePracticeWave(Path.Combine(directory, "long.wav"), 30);
            writePracticeWave(Path.Combine(directory, "short.wav"));
            File.WriteAllText(Path.Combine(directory, "chart.bms"), """
                #PLAYER 1
                #TITLE Practice Background Resume
                #ARTIST Test
                #BPM 120
                #PLAYLEVEL 1
                #DIFFICULTY 1
                #WAV01 long.wav
                #WAV02 short.wav
                #00001:01
                #00001:02
                #01011:0002
                #01111:0002
                #02001:02
                #03011:02
                """);
            import = new BmsFileImporter(Realm, LocalStorage).Import(directory);
        });
        AddUntilStep("chart imported", () => import.IsCompleted);
        AddStep("open song select", () =>
        {
            import.GetAwaiter().GetResult();
            Stack.Push(songSelect = new BmsSoloSongSelect());
        });
        AddUntilStep("song select loaded", () => songSelect.IsLoaded && Stack.CurrentScreen == songSelect);
        AddStep("prepare middle of background", () =>
        {
            var info = Realm.Run(realm => realm.All<BeatmapInfo>().AsEnumerable().Single(chart => chart.Metadata.Source == directory).Detach());
            var source = beatmaps.GetWorkingBeatmap(info);
            Beatmap.Value = source;
            session = new BmsPracticeSession(source,
                new BmsPracticeSettings { StartTime = 21000, EndTime = 27000, SkipResults = true, CountIn = 5 },
                [rate < 1 ? new BmsModHalfTime { SpeedChange = { Value = rate } } : new BmsModDoubleTime { SpeedChange = { Value = rate } }]);
            Stack.Push(practice = new PracticeIsolationScreen(session));
        });
        AddUntilStep("countdown ready", () => practice.Countdown.IsReady);
        AddStep("verify resume is prepared before countdown", () =>
        {
            practice.PauseCountdown();
            var player = practice.Players.Single();
            var start = player.ChildrenOfType<GameplayClockContainer>().Single().CurrentTime;
            var playback = player.PracticeRuleset.SamplePlayback;
            var controller = BmsPcmMaintenanceTest.Field<BmsPcmPlaybackSession>(playback, "playbackSession").Controller!;
            Assert.That(start, Is.InRange(1000, 21000));
            Assert.That(controller.IsSampleReady(1, start), Is.True, "The actual preroll seek position must be buffered before count-in starts.");
            Assert.That(controller.GetSampleLength(1), Is.EqualTo(30000).Within(1));
            Assert.That(session.Attempts, Is.Zero);
            Assert.That(player.PracticeRuleset.BackgroundAudioPaused.Value, Is.True);
        });
        AddStep("start prepared practice", () =>
        {
            practice.ResumeCountdown();
            practice.SkipCountdown();
        });
        AddUntilStep("background spanning start reaches mixer", () => session.Attempts == 1
            && BmsAudioTestAccess.GetActiveVoiceCount(practice.Players.Single().PracticeRuleset.SamplePlayback) > 0);
        AddStep("exit prepared practice", () => practice.AdjustRange());
        AddUntilStep("song select restored", () => Stack.CurrentScreen == songSelect);
    }

    private static void writePracticeWave(string path, int seconds = 1)
    {
        const int sample_rate = 44100;
        var dataSize = sample_rate * sizeof(short) * seconds;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sample_rate);
        writer.Write(sample_rate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
    }

    private partial class PracticeIsolationScreen : BmsPracticeScreen
    {
        private readonly BmsPracticeSession session;
        internal bool WindowActive { get; set; } = true;
        internal List<PracticeIsolationPlayer> Players { get; } = [];

        internal PracticeIsolationScreen(BmsPracticeSession session)
            : base(session)
        {
            this.session = session;
        }

        protected override bool IsWindowActive => WindowActive;

        protected override BmsPracticePlayer CreatePlayer()
        {
            var player = new PracticeIsolationPlayer(session);
            Players.Add(player);
            return player;
        }
    }

    private partial class PracticeIsolationPlayer : BmsPracticePlayer
    {
        internal int ImportCount { get; private set; }

        internal PracticeIsolationPlayer(BmsPracticeSession session)
            : base(session)
        {
        }

        protected override bool PauseOnFocusLost => false;

        protected override Task ImportScore(Score score)
        {
            ImportCount++;
            return base.ImportScore(score);
        }
    }
}
