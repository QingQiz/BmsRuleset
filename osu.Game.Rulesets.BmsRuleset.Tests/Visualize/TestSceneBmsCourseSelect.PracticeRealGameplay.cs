#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Tests.Audio;
using osu.Game.Rulesets.BmsRuleset.Tests.Normal;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens;
using osu.Game.Screens.Play;
using osu.Game.Tests.Beatmaps;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsCourseSelect
{
    [TestCase("Destr0yer (by 削除 feat. Nikki Simmons)", "7ANOTHER.bml", 0.75, true, 0)]
    [TestCase("Aleph-0 (by LeaF)", "_7ANOTHER.bms", 1.25, false, 0)]
    [TestCase("Destr0yer (by 削除 feat. Nikki Simmons)", "7ANOTHER.bml", 0.75, true, 60)]
    public void TestPracticeRealChartNaturalClockAndLiveInput(string directory, string filename, double rate, bool requireLongNote, int extendedMinutes)
    {
        WorkingBeatmap source = null!;
        BmsBeatmap full = null!;
        BmsPracticeSession session = null!;
        RealPracticeScreen practice = null!;
        RealPracticePlayer first = null!;
        RealPracticePlayer second = null!;
        RealPracticePlayer third = null!;
        RealPracticeRoot root = null!;
        var wait = new Stopwatch();
        var resumedCountdown = new Stopwatch();
        double remaining = 0;
        double frozenTime = 0;
        var judgedBeforePause = 0;
        var offsetsBefore = 0;
        var expectedCount = 0;
        var sourceOd = 0f;
        var sourceHp = 0f;
        PracticeTransitionMonitor monitor = null!;

        AddStep("decode real chart with its original difficulty", () =>
        {
            var path = Path.Combine(realPracticeSongsRoot(), directory, filename);
            var bytes = File.ReadAllBytes(path);
            var decoded = BmsBeatmapDecoder.DecodeBytes(bytes, randomValueSelector: _ => 1);
            full = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset()) { BranchRandomValueSelector = _ => 1 }.Convert();
            if (extendedMinutes > 0)
                full = BmsPracticeExtendedChartFixture.RepeatToMinutes(full, extendedMinutes);
            sourceOd = full.Difficulty.OverallDifficulty;
            sourceHp = full.Difficulty.DrainRate;
            BmsTestBeatmaps.SetupBeatmapInfo(full, Ruleset.Value, bpm: full.BeatmapInfo.BPM,
                overallDifficulty: sourceOd, drainRate: sourceHp);
            full.Metadata.Source = Path.GetDirectoryName(path)!;
            full.BeatmapInfo.Hash = Convert.ToHexString(SHA256.HashData(bytes));
            source = new BmsWorkingBeatmap(new TestWorkingBeatmap(full, audioManager: Audio), Audio);
            Beatmap.Value = source;
            SelectedMods.Value = [new BmsModMirror()];
            offsetsBefore = BmsRulesetRuntime.VisualOffsetSuggestions.History.Count;
            Mod speed = rate < 1
                ? new BmsModHalfTime { SpeedChange = { Value = rate } }
                : new BmsModDoubleTime { SpeedChange = { Value = rate } };
            var minimumStart = extendedMinutes * 60000d * 0.95;
            session = new BmsPracticeSession(source, selectRealPracticeRange(full, requireLongNote, minimumStart), [speed]);
            Assert.That(session.Settings.StartTime, Is.GreaterThanOrEqualTo(minimumStart - 750));
            expectedCount = BmsPracticeBeatmap.Create(full, session.Settings).CountScoringNotes();
            Assert.That(expectedCount, Is.GreaterThan(10));
            Stack.Push(root = new RealPracticeRoot());
        });
        AddUntilStep("test root entered", () => Stack.CurrentScreen == root && root.IsLoaded);
        AddStep("start the real practice screen", () => Stack.Push(practice = new RealPracticeScreen(session)));
        AddUntilStep("real samples and first count-in ready", () => practice.Countdown.IsReady && practice.Players.Count == 1);
        AddStep("pause actual count-in", () =>
        {
            practice.PauseCountdown();
            first = practice.Players[0];
            remaining = practice.Countdown.RemainingMilliseconds;
            frozenTime = first.NaturalClock.CurrentTime;
            Assert.That(remaining, Is.GreaterThan(1000));
            Assert.That(session.Attempts, Is.Zero);
            Assert.That(first.NaturalClock.IsPaused.Value, Is.True);
            Assert.That(first.PracticeRuleset.KeyBindingInputManager.UseParentInput, Is.False);
            Assert.That(first.PracticeRuleset.BackgroundAudioPaused.Value, Is.True);
            Assert.That(first.PreparedJudgements, Is.Zero);
            Assert.That(first.GameplayState.Beatmap.Difficulty.OverallDifficulty, Is.EqualTo(sourceOd));
            Assert.That(first.GameplayState.Beatmap.Difficulty.DrainRate, Is.EqualTo(sourceHp));
            Assert.That(first.PreparedAssets, Is.GreaterThan(0));
            if (requireLongNote)
                Assert.That(first.GameplayState.Beatmap.HitObjects.OfType<BmsLongNote>(), Is.Not.Empty);
            else
                Assert.That(full.TimingMap!.BpmEvents.Count(change => session.Settings.Contains(change.Time)), Is.GreaterThan(0));
            wait.Restart();
        });
        AddUntilStep("count-in remains paused for real elapsed time", () => wait.ElapsedMilliseconds >= 300);
        AddStep("resume count-in without skipping", () =>
        {
            Assert.That(practice.Countdown.RemainingMilliseconds, Is.EqualTo(remaining));
            Assert.That(first.NaturalClock.CurrentTime, Is.EqualTo(frozenTime));
            Assert.That(session.Attempts, Is.Zero);
            resumedCountdown.Restart();
            practice.ResumeCountdown();
        });
        AddUntilStep("count-in naturally starts first attempt", () => first.HasAttemptStarted);
        AddStep("verify real runtime rate and disabled replay", () =>
        {
            monitor = monitorPracticeTransitions();
            Assert.That(resumedCountdown.Elapsed.TotalMilliseconds, Is.GreaterThanOrEqualTo(remaining - 100));
            Assert.That(first.NaturalClock.Rate, Is.EqualTo(rate));
            Assert.That(((BmsPlayfield)first.PracticeRuleset.Playfield).ScrollController.PlaybackRate, Is.EqualTo(rate));
            Assert.That(BmsGameplayAudioController.GetPlaybackRate(session.Mods), Is.EqualTo(rate));
            Assert.That(first.PracticeRuleset.HasReplayLoaded.Value, Is.False);
            Assert.That(((BmsInputManager)first.PracticeRuleset.KeyBindingInputManager).Recorder, Is.Null);
            Assert.That(first.Score.Replay.Frames, Is.Empty);
            Assert.That(first.StartedJudgements, Is.Zero);
            Assert.That(first.StartedCombo, Is.Zero);
        });
        AddUntilStep("live notes judged before pause", () => first.NaturalClock.CurrentTime >= session.Settings.StartTime + 1200
            && first.Scores.ScoringJudgementEventCount > 0);
        AddStep("pause actual live gameplay", () => Assert.That(first.Pause(), Is.True));
        AddUntilStep("practice samples pause with actual player", () => first.PracticeRuleset.BackgroundAudioPaused.Value);
        AddStep("remember paused live state", () =>
        {
            frozenTime = first.NaturalClock.CurrentTime;
            judgedBeforePause = first.Scores.ScoringJudgementEventCount;
            wait.Restart();
        });
        AddUntilStep("live player remains paused for real elapsed time", () => wait.ElapsedMilliseconds >= 300);
        AddStep("resume actual player", () =>
        {
            Assert.That(first.NaturalClock.CurrentTime, Is.EqualTo(frozenTime));
            Assert.That(first.Scores.ScoringJudgementEventCount, Is.EqualTo(judgedBeforePause));
            first.Resume();
        });
        AddUntilStep("real delayed resume completes", () => !first.IsResuming && !first.NaturalClock.IsPaused.Value);
        AddUntilStep("first interval passes its midpoint naturally", () => first.NaturalClock.CurrentTime >= session.Settings.StartTime + 3500);
        AddUntilStep("reloaded playfield shows its opening countdown", () => practice.Results.Count == 1
            && practice.Players.Count >= 2 && Stack.CurrentScreen == practice.Players[1] && practice.Countdown.IsReady);
        AddStep("freeze countdown at the new interval start", () =>
        {
            practice.PauseCountdown();
            second = practice.Players[1];
            assertRealPracticeCompleted(first, practice.Results[0], expectedCount, requireLongNote);
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(Stack.CurrentScreen, Is.SameAs(second));
            Assert.That(second.NaturalClock.IsPaused.Value, Is.True);
            Assert.That(second.NaturalClock.CurrentTime, Is.LessThan(session.Settings.StartTime));
            frozenTime = second.NaturalClock.CurrentTime;
            remaining = practice.Countdown.RemainingMilliseconds;
            Assert.That(remaining, Is.GreaterThan(1500));
            Assert.That(second.HasAttemptStarted, Is.False);
            Assert.That(second.PreparedJudgements, Is.Zero);
            Assert.That(second.CountInOverlay.State.Value, Is.EqualTo(osu.Framework.Graphics.Containers.Visibility.Visible));
            Assert.That(second.PracticeRuleset.KeyBindingInputManager.UseParentInput, Is.False);
            Assert.That(practice.SelectedScore.Value, Is.Null);
            Assert.That(session.Attempts, Is.EqualTo(1));
            Assert.That(Realm.Run(realm => realm.All<ScoreInfo>().Count()), Is.EqualTo(scoreCountAtTestStart));
        });
        AddAssert("reloaded player is current and no longer pending", () => Stack.CurrentScreen == second && practice.PendingPlayer == null);
        AddAssert("opening countdown keeps the new interval clock frozen", () => second.NaturalClock.CurrentTime, () => Is.EqualTo(frozenTime));
        if (KeepPracticeCountdownOpen)
        {
            AddStep("leave the real playfield countdown running for capture", () => practice.ResumeCountdown());
            return;
        }
        AddStep("resume second natural count-in", () =>
        {
            resumedCountdown.Restart();
            practice.ResumeCountdown();
        });
        AddUntilStep("second attempt naturally starts", () => second.HasAttemptStarted);
        AddStep("verify fresh second attempt", () =>
        {
            Assert.That(resumedCountdown.Elapsed.TotalMilliseconds, Is.GreaterThanOrEqualTo(remaining - 100));
            Assert.That(second.PreparedJudgements, Is.Zero);
            Assert.That(second.StartedJudgements, Is.Zero);
            Assert.That(second.StartedCombo, Is.Zero);
            Assert.That(second.PreparedAssets, Is.GreaterThan(0));
            Assert.That(second.PreparedHealth, Is.EqualTo(first.PreparedHealth));
            Assert.That(second.PracticeRuleset.Playfield, Is.Not.SameAs(first.PracticeRuleset.Playfield));
            Assert.That(second.NaturalClock.AdjustmentsFromMods.AggregateFrequency.Value
                        * second.NaturalClock.AdjustmentsFromMods.AggregateTempo.Value, Is.EqualTo(rate));
            Assert.That(session.Attempts, Is.EqualTo(2));
            Assert.That(Realm.Run(realm => realm.All<ScoreInfo>().Count()), Is.EqualTo(scoreCountAtTestStart));
        });
        AddUntilStep("second interval passes first checkpoint", () => second.NaturalClock.CurrentTime >= session.Settings.StartTime + 1500);
        AddUntilStep("second interval passes midpoint", () => second.NaturalClock.CurrentTime >= session.Settings.StartTime + 3500);
        AddUntilStep("two complete real attempts and third opening count-in ready", () => practice.Results.Count == 2
            && practice.Players.Count >= 3 && Stack.CurrentScreen == practice.Players[2] && practice.Countdown.IsReady);
        AddStep("focus loss pauses the later repeat countdown", () =>
        {
            practice.WindowActive = false;
            practice.PauseCountdown();
            third = practice.Players[2];
            remaining = practice.Countdown.RemainingMilliseconds;
            frozenTime = third.NaturalClock.CurrentTime;
            wait.Restart();
        });
        AddUntilStep("later repeat remains paused in real time", () => wait.ElapsedMilliseconds >= 300);
        AddStep("focus return leaves the repeat paused", () =>
        {
            practice.WindowActive = true;
            Assert.That(practice.Countdown.IsPaused, Is.True);
            Assert.That(practice.Countdown.RemainingMilliseconds, Is.EqualTo(remaining));
            Assert.That(third.NaturalClock.CurrentTime, Is.EqualTo(frozenTime));
            Assert.That(Stack.CurrentScreen, Is.SameAs(third));
            InputManager.PressKey(Key.Enter);
        });
        AddStep("Enter restarts the selected countdown", () =>
        {
            InputManager.ReleaseKey(Key.Enter);
            Assert.That(practice.Countdown.IsPaused, Is.False);
            Assert.That(practice.Countdown.RemainingMilliseconds, Is.GreaterThan(1500));
            Assert.That(session.Attempts, Is.EqualTo(2));
        });
        AddStep("stop repeating through practice screen", () =>
        {
            practice.PauseCountdown();
            assertPracticeTransitions(monitor);
            assertRealPracticeCompleted(second, practice.Results[1], expectedCount, requireLongNote);
            Assert.That(session.Attempts, Is.EqualTo(2));
            practice.AdjustRange();
        });
        AddUntilStep("return to settings cancels the prepared attempt", () => Stack.CurrentScreen == root && !practice.Countdown.IsReady);
        AddStep("leave practice and write functional evidence", () =>
        {
            Assert.That(practice.Players.All(player => player.ImportCount == 0), Is.True);
            Assert.That(Realm.Run(realm => realm.All<ScoreInfo>().Count()), Is.EqualTo(scoreCountAtTestStart));
            Assert.That(BmsRulesetRuntime.VisualOffsetSuggestions.History.Count, Is.EqualTo(offsetsBefore));
            var report = JsonSerializer.Serialize(new
            {
                Chart = Path.Combine(directory, filename),
                ExtendedMinutes = extendedMinutes,
                session.Settings,
                Rate = rate,
                OriginalOD = sourceOd,
                OriginalHP = sourceHp,
                FullObjects = full.HitObjects.Count,
                SelectedJudgements = expectedCount,
                SelectedLongNotes = first.GameplayState.Beatmap.HitObjects.OfType<BmsLongNote>().Count(),
                SelectedBpmChanges = full.TimingMap!.BpmEvents.Count(change => session.Settings.Contains(change.Time)),
                Attempts = practice.Results.Select((result, index) => new
                {
                    result.Attempt,
                    result.ExScore,
                    result.Score.Accuracy,
                    result.Score.MaxCombo,
                    result.Score.Statistics,
                    Judgements = practice.Players[index].Scores.ScoringJudgementEventCount,
                    practice.Players[index].InjectedPresses,
                    practice.Players[index].MaxActiveVoices,
                    practice.Players[index].PreparedAssets,
                    practice.Players[index].UnexpectedSeekCount,
                }),
                ScoreImports = practice.Players.Sum(player => player.ImportCount),
            }, new JsonSerializerOptions { WriteIndented = true });
            var reportPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"practice-real-{Path.GetFileNameWithoutExtension(filename)}-{rate:0.00}-{extendedMinutes}min.json");
            File.WriteAllText(reportPath, report);
            TestContext.AddTestAttachment(reportPath, "Real chart, natural gameplay clock, live test inputs, two practice loops");
            TestContext.WriteLine(report);
            practice.Exit();
        });
        AddUntilStep("original screen restored", () => Stack.CurrentScreen == root);
        AddAssert("original beatmap restored", () => Beatmap.Value, () => Is.SameAs(source));
        AddAssert("original mods restored", () => SelectedMods.Value.Single() is BmsModMirror);
    }

    private static BmsPracticeSettings selectRealPracticeRange(BmsBeatmap full, bool requireLongNote, double minimumStart = 0)
    {
        var anchors = requireLongNote
            ? full.HitObjects.OfType<BmsLongNote>().Where(note => note.Duration >= 250 && note.Duration <= 3500).Select(note => note.StartTime)
            : full.TimingMap!.BpmEvents.Zip(full.TimingMap.BpmEvents.Skip(1))
                .Where(pair => pair.First.Bpm != pair.Second.Bpm && pair.Second.Time > 5000).Select(pair => pair.Second.Time);
        foreach (var anchor in anchors.Where(time => time >= minimumStart))
        {
            var settings = new BmsPracticeSettings { StartTime = Math.Max(0, anchor - 750), EndTime = anchor + 5250, CountIn = 2, SkipResults = true };
            var selected = full.HitObjects.Where(note => settings.Contains(note.StartTime)).ToArray();
            if (settings.IsValidFor(full) && selected.Length > 10 && selected.Max(note => note.GetEndTime()) < settings.StartTime + 7500)
                return settings;
        }
        throw new AssertionException("No suitable real chart interval includes the required LN or BPM change.");
    }

    private static string realPracticeSongsRoot([CallerFilePath] string sourceFile = "")
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "bms_test_songs"));
        return Directory.Exists(sourceRoot) ? sourceRoot : BmsEmbeddedSongDecoderTest.TestSongsRoot;
    }

    private static void assertRealPracticeCompleted(RealPracticePlayer player, BmsPracticeAttemptResult result, int expectedCount, bool requireLongNote)
    {
        Assert.That(player.UnexpectedSeekCount, Is.Zero, "Natural gameplay must not be advanced by test seeks.");
        Assert.That(player.Scores.ScoringJudgementEventCount, Is.EqualTo(expectedCount));
        Assert.That(player.InjectedPresses, Is.GreaterThan(10));
        Assert.That(result.Score.Statistics.GetValueOrDefault(HitResult.Perfect)
                    + result.Score.Statistics.GetValueOrDefault(HitResult.Great)
                    + result.Score.Statistics.GetValueOrDefault(HitResult.Good), Is.GreaterThan(expectedCount * 0.7));
        Assert.That(result.ExScore, Is.GreaterThan(0));
        Assert.That(result.Score.MaxCombo, Is.GreaterThan(3));
        Assert.That(player.MaxActiveVoices, Is.GreaterThan(0), "Real chart samples must reach the PCM mixer.");
        Assert.That(result.Gauges, Is.Not.Empty);
        Assert.That(result.GaugeHistory, Is.Not.Empty);
        Assert.That(player.ImportCount, Is.Zero);
        if (requireLongNote)
        {
            var observations = player.Scores.JudgementEvents.SelectMany(judgement => judgement.TimingObservations).ToArray();
            Assert.That(observations.Any(observation => observation.Kind == BmsTimingObservationKind.LongNoteHead), Is.True);
            Assert.That(observations.Any(observation => observation.Kind == BmsTimingObservationKind.LongNoteTail), Is.True);
        }
    }

    private partial class RealPracticeRoot : OsuScreen
    {
    }

    private partial class RealPracticeScreen : BmsPracticeScreen
    {
        private readonly BmsPracticeSession session;
        internal List<RealPracticePlayer> Players { get; } = [];
        internal List<BmsPracticeAttemptResult> Results { get; } = [];
        internal bool WindowActive { get; set; } = true;

        internal RealPracticeScreen(BmsPracticeSession session)
            : base(session)
        {
            this.session = session;
        }

        protected override bool IsWindowActive => WindowActive;

        protected override BmsPracticePlayer CreatePlayer()
        {
            var player = new RealPracticePlayer(session);
            player.AttemptCompleted += Results.Add;
            Players.Add(player);
            return player;
        }
    }

    private partial class RealPracticePlayer : BmsPracticePlayer
    {
        private BmsAutoGenerator.ActionPoint[] actions = [];
        private readonly HashSet<BmsAction> pressed = [];
        private int nextAction;
        internal int ImportCount { get; private set; }
        internal int InjectedPresses { get; private set; }
        internal int MaxActiveVoices { get; private set; }
        internal int PreparedJudgements { get; private set; }
        internal int StartedJudgements { get; private set; }
        internal int StartedCombo { get; private set; }
        internal int PreparedAssets { get; private set; }
        internal double PreparedHealth { get; private set; }
        internal int UnexpectedSeekCount { get; private set; }
        internal GameplayClockContainer NaturalClock => GameplayClockContainer;
        internal BmsScoreProcessor Scores => (BmsScoreProcessor)GameplayState.ScoreProcessor;
        protected override bool PauseOnFocusLost => false;

        internal RealPracticePlayer(BmsPracticeSession session)
            : base(session)
        {
            Prepared += () =>
            {
                PreparedJudgements = Scores.ScoringJudgementEventCount;
                PreparedHealth = GameplayState.HealthProcessor.Health.Value;
                PreparedAssets = BmsAudioTestAccess.GetCachedAssetCount(PracticeRuleset.SamplePlayback);
            };
            AttemptStarted += () =>
            {
                StartedJudgements = Scores.ScoringJudgementEventCount;
                StartedCombo = Scores.Combo.Value;
                GameplayClockContainer.OnSeek += () => UnexpectedSeekCount++;
            };
        }

        protected override void PrepareReplay()
        {
            base.PrepareReplay();
            actions = new BmsAutoGenerator((BmsBeatmap)GameplayState.Beatmap).GenerateActionPoints()
                .OrderBy(point => point.Time).ThenBy(point => point.Press).ToArray();
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();
            if (!HasAttemptStarted || GameplayClockContainer.IsPaused.Value || IsResuming)
                return;

            // Inputs follow the real clock; no replay handler or time-changing test helper participates.
            var bindings = ((BmsInputManager)PracticeRuleset.KeyBindingInputManager).KeyBindingContainer;
            while (nextAction < actions.Length && actions[nextAction].Time <= GameplayClockContainer.CurrentTime)
            {
                var point = actions[nextAction++];
                if (point.Press && pressed.Add(point.Action))
                {
                    bindings.TriggerPressed(point.Action);
                    InjectedPresses++;
                }
                else if (!point.Press && pressed.Remove(point.Action))
                    bindings.TriggerReleased(point.Action);
            }
            MaxActiveVoices = Math.Max(MaxActiveVoices, BmsAudioTestAccess.GetActiveVoiceCount(PracticeRuleset.SamplePlayback));
        }

        protected override Task ImportScore(Score score)
        {
            ImportCount++;
            return base.ImportScore(score);
        }
    }
}
