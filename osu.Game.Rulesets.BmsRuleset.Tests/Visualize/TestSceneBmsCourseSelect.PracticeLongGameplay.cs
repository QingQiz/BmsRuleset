#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Tests.Audio;
using osu.Game.Rulesets.Objects;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Tests.Beatmaps;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsCourseSelect
{
    [TestCase("Destr0yer (by 削除 feat. Nikki Simmons)", "7ANOTHER.bml")]
    [TestCase("Aleph-0 (by LeaF)", "_7ANOTHER.bms")]
    public void TestPracticeRealChartLongRangeSeekCompletionAndLoopCancellation(string directory, string filename)
    {
        WorkingBeatmap source = null!;
        BmsBeatmap full = null!;
        BmsPracticeSession session = null!;
        PracticeIsolationScreen practice = null!;
        PracticeIsolationPlayer first = null!;
        PracticeIsolationPlayer second = null!;
        RealPracticeRoot root = null!;
        GameplayClockContainer clock = null!;
        GameplayClockContainer secondClock = null!;
        BmsScoreProcessor scores = null!;
        BmsHitObject[] selected = [];
        (double Time, double End, int Column)[] originalObjects = [];
        var checkpoints = new (double Time, int Judgements)[3];
        var duration = 0d;
        var completeTime = 0d;
        var initialHealth = 0d;
        var expectedCount = 0;
        var offsetsBefore = 0;
        var firstPreparedAssets = 0;
        var secondPreparedAssets = 0;
        var explicitSeeks = 0;
        var wait = new Stopwatch();

        AddStep("decode real chart and select at least sixty seconds", () =>
        {
            var path = Path.Combine(realPracticeSongsRoot(), directory, filename);
            var bytes = File.ReadAllBytes(path);
            var decoded = BmsBeatmapDecoder.DecodeBytes(bytes, randomValueSelector: _ => 1);
            full = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset()) { BranchRandomValueSelector = _ => 1 }.Convert();
            BmsTestBeatmaps.SetupBeatmapInfo(full, Ruleset.Value, bpm: full.BeatmapInfo.BPM,
                overallDifficulty: full.Difficulty.OverallDifficulty, drainRate: full.Difficulty.DrainRate);
            full.Metadata.Source = Path.GetDirectoryName(path)!;
            full.BeatmapInfo.Hash = Convert.ToHexString(SHA256.HashData(bytes));
            source = new BmsWorkingBeatmap(new TestWorkingBeatmap(full, audioManager: Audio), Audio);
            originalObjects = full.HitObjects.Select(note => longPracticeObjectIdentity(note)).ToArray();
            Beatmap.Value = source;
            SelectedMods.Value = [new BmsModMirror()];
            offsetsBefore = BmsRulesetRuntime.VisualOffsetSuggestions.History.Count;
            var bounds = BmsPracticeBeatmap.GetTimeBounds(full);
            var fullDuration = bounds.EndTime - bounds.StartTime;
            var start = bounds.StartTime + fullDuration * 0.15;
            duration = Math.Min(90000, fullDuration * 0.7);
            Assert.That(duration, Is.GreaterThanOrEqualTo(60000));
            var settings = new BmsPracticeSettings { StartTime = start, EndTime = start + duration, CountIn = 5, SkipResults = true };
            session = new BmsPracticeSession(source, settings, []);
            full = session.GetFullBeatmap();
            selected = full.HitObjects.Where(note => settings.Contains(note.StartTime)).ToArray();
            expectedCount = BmsPracticeBeatmap.Create(full, settings).CountScoringNotes();
            completeTime = Math.Max(settings.EndTime, selected.Max(note => note.GetEndTime())) + 500;
            Assert.That(selected.Length, Is.GreaterThan(300));
            Assert.That(full.HitObjects.Count(note => note.StartTime < settings.StartTime), Is.GreaterThan(20));
            Assert.That(full.HitObjects.Count(note => note.StartTime >= settings.EndTime), Is.GreaterThan(20));
            Assert.That(selected.Count(note => note.StartTime < settings.StartTime + duration * 0.1), Is.GreaterThan(10));
            Assert.That(selected.Count(note => note.StartTime > settings.EndTime - duration * 0.1), Is.GreaterThan(10));
            Stack.Push(root = new RealPracticeRoot());
        });
        AddUntilStep("root screen entered", () => Stack.CurrentScreen == root && root.IsLoaded);
        AddStep("prepare the complete long practice interval", () => Stack.Push(practice = new PracticeIsolationScreen(session)));
        AddUntilStep("long interval player and samples ready", () => practice.Countdown.IsReady && practice.Players.Count == 1);
        AddStep("freeze count-in and verify every selected object", () =>
        {
            practice.PauseCountdown();
            first = practice.Players[0];
            clock = first.ChildrenOfType<GameplayClockContainer>().Single();
            scores = (BmsScoreProcessor)first.GameplayState.ScoreProcessor;
            initialHealth = first.GameplayState.HealthProcessor.Health.Value;
            firstPreparedAssets = BmsAudioTestAccess.GetCachedAssetCount(first.PracticeRuleset.SamplePlayback);
            Assert.That(firstPreparedAssets, Is.GreaterThan(0));
            Assert.That(clock.IsPaused.Value, Is.True);
            Assert.That(first.PracticeRuleset.KeyBindingInputManager.UseParentInput, Is.False);
            Assert.That(first.GameplayState.Beatmap.HitObjects.Select(longPracticeObjectIdentity),
                Is.EqualTo(selected.Select(note => longPracticeObjectIdentity(note))));
            Assert.That(((BmsBeatmap)first.GameplayState.Beatmap).PracticeFullNoteCount, Is.EqualTo(full.CountScoringNotes()));
            Assert.That(((BmsBeatmap)first.GameplayState.Beatmap).PracticeEndTime, Is.EqualTo(session.Settings.EndTime));
            Assert.That(scores.ScoringJudgementEventCount, Is.Zero);
            Assert.That(scores.HasCompleted.Value, Is.False);
            Assert.That(session.Attempts, Is.Zero);
            Assert.That(full.HitObjects.Count, Is.GreaterThan(selected.Length));
        });
        AddStep("start long interval through its count-in controls", () =>
        {
            practice.ResumeCountdown();
            practice.SkipCountdown();
        });
        AddUntilStep("long interval attempt started", () => session.Attempts == 1 && first.HasAttemptStarted);

        // This test targets long-range state transitions; the separate real-chart tests exercise natural clocks and live inputs.
        for (var i = 0; i < checkpoints.Length; i++)
        {
            var index = i;
            var fraction = index switch { 0 => 0.25, 1 => 0.5, _ => 0.9 };
            AddStep($"seek long interval to {fraction:P0} with normal gameplay clock", () =>
            {
                checkpoints[index].Time = session.Settings.StartTime + duration * fraction;
                clock.Seek(checkpoints[index].Time);
                explicitSeeks++;
            });
            AddUntilStep($"long interval catches up to {fraction:P0}", () => clock.CurrentTime >= checkpoints[index].Time
                && Math.Abs(first.PracticeRuleset.FrameStableClock.CurrentTime - clock.CurrentTime) < 100
                && scores.ScoringJudgementEventCount > (index == 0 ? 0 : checkpoints[index - 1].Judgements));
            AddStep($"long interval remains active at {fraction:P0}", () =>
            {
                checkpoints[index].Judgements = scores.ScoringJudgementEventCount;
                Assert.That(Stack.CurrentScreen, Is.SameAs(first));
                Assert.That(scores.HasCompleted.Value, Is.False);
                Assert.That(practice.LastResult, Is.Null);
                Assert.That(practice.Players.Count, Is.EqualTo(1));
                Assert.That(scores.ScoringJudgementEventCount, Is.LessThan(expectedCount));
                Assert.That(first.GameplayState.HasFailed, Is.False);
            });
        }

        AddStep("seek beyond the complete long interval and every LN tail", () =>
        {
            clock.Seek(completeTime);
            explicitSeeks++;
        });
        AddUntilStep("long interval concludes and next full player is ready", () => practice.LastResult?.Attempt == 1
            && practice.Players.Count == 2 && practice.Countdown.IsReady);
        AddStep("pause the repeat countdown at the reloaded interval start", () =>
        {
            practice.PauseCountdown();
            second = practice.Players[1];
            Assert.That(Stack.CurrentScreen, Is.SameAs(second));
            Assert.That(second.CountInOverlay.State.Value, Is.EqualTo(Visibility.Visible));
            var reloadedClock = second.ChildrenOfType<GameplayClockContainer>().Single();
            Assert.That(reloadedClock.IsPaused.Value, Is.True);
            Assert.That(reloadedClock.CurrentTime, Is.LessThan(session.Settings.StartTime));
            Assert.That(second.HasAttemptStarted, Is.False);
        });
        AddUntilStep("next long interval fully preloaded", () => second.LoadState >= LoadState.Ready);
        AddStep("start next long interval directly", () =>
        {
            practice.ResumeCountdown();
            practice.SkipCountdown();
        });
        AddUntilStep("next long interval starts with no extra countdown", () => second.HasAttemptStarted && session.Attempts == 2);
        AddStep("verify complete result and reset the next long interval", () =>
        {
            Assert.That(second.Pause(), Is.True);
            secondClock = second.ChildrenOfType<GameplayClockContainer>().Single();
            secondPreparedAssets = BmsAudioTestAccess.GetCachedAssetCount(second.PracticeRuleset.SamplePlayback);
            Assert.That(scores.ScoringJudgementEventCount, Is.EqualTo(expectedCount));
            Assert.That(practice.LastResult!.Score.Statistics.Values.Sum(), Is.EqualTo(expectedCount));
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.GameplayState.Beatmap.HitObjects.Select(longPracticeObjectIdentity),
                Is.EqualTo(selected.Select(note => longPracticeObjectIdentity(note))));
            Assert.That(second.GameplayState.ScoreProcessor.JudgedHits, Is.Zero);
            Assert.That(second.GameplayState.ScoreProcessor.Combo.Value, Is.Zero);
            Assert.That(((BmsScoreProcessor)second.GameplayState.ScoreProcessor).ScoringJudgementEventCount, Is.Zero);
            Assert.That(second.GameplayState.HealthProcessor.Health.Value, Is.EqualTo(initialHealth));
            Assert.That(((BmsHealthProcessor)second.GameplayState.HealthProcessor).HasEverFailed, Is.False);
            Assert.That(second.PracticeRuleset.Playfield, Is.Not.SameAs(first.PracticeRuleset.Playfield));
            Assert.That(secondPreparedAssets, Is.GreaterThan(0));
            Assert.That(secondClock.IsPaused.Value, Is.True);
            Assert.That(second.HasAttemptStarted, Is.True);
            Assert.That(session.Attempts, Is.EqualTo(2));
            Assert.That(practice.Players.All(player => player.ImportCount == 0), Is.True);
            Assert.That(Realm.Run(realm => realm.All<ScoreInfo>().Count()), Is.EqualTo(scoreCountAtTestStart));
        });
        AddStep("return to settings from the second long interval", () => practice.AdjustRange());
        AddUntilStep("return cancels practice and retains first summary", () => Stack.CurrentScreen == root
            && practice.PendingPlayer == null && !practice.Countdown.IsReady && practice.LastResult?.Attempt == 1);
        AddStep("cancelled count-in cannot be skipped into an attempt", () =>
        {
            practice.SkipCountdown();
            wait.Restart();
        });
        AddUntilStep("stopped loop stays inactive after real time passes", () => wait.ElapsedMilliseconds >= 300);
        AddStep("verify cancellation and write long-range evidence", () =>
        {
            Assert.That(session.Attempts, Is.EqualTo(2));
            Assert.That(practice.Players.Count, Is.EqualTo(2));
            Assert.That(second.HasAttemptStarted, Is.True);
            Assert.That(second.PracticeRuleset.KeyBindingInputManager.UseParentInput, Is.False);
            Assert.That(secondClock.IsPaused.Value, Is.True);
            Assert.That(BmsRulesetRuntime.VisualOffsetSuggestions.History.Count, Is.EqualTo(offsetsBefore));
            var report = JsonSerializer.Serialize(new
            {
                Chart = Path.Combine(directory, filename),
                ClockMode = "Normal GameplayClockContainer.Seek checkpoints; natural-clock gameplay is covered separately",
                ExplicitSeeks = explicitSeeks,
                session.Settings,
                DurationMilliseconds = duration,
                FullObjects = full.HitObjects.Count,
                SelectedObjects = selected.Length,
                SelectedLongNotes = selected.OfType<BmsLongNote>().Count(),
                ExpectedJudgements = expectedCount,
                CompletedJudgements = scores.ScoringJudgementEventCount,
                Checkpoints = checkpoints.Select(point => new { point.Time, point.Judgements }),
                FirstPreparedAssets = firstPreparedAssets,
                SecondPreparedAssets = secondPreparedAssets,
                CompletedAttempts = session.Attempts,
                PreparedPlayers = practice.Players.Count,
                LoopCancelled = !practice.Countdown.IsReady && practice.PendingPlayer == null,
                ScoreImports = practice.Players.Sum(player => player.ImportCount),
            }, new JsonSerializerOptions { WriteIndented = true });
            var reportPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"practice-long-{Path.GetFileNameWithoutExtension(filename)}.json");
            File.WriteAllText(reportPath, report);
            TestContext.AddTestAttachment(reportPath, "Real long-range practice: explicit seek checkpoints, complete scoring, next-loop reset and cancellation");
            TestContext.WriteLine(report);
            practice.Exit();
        });
        AddUntilStep("original root screen restored", () => Stack.CurrentScreen == root);
        AddAssert("long practice restores original beatmap", () => Beatmap.Value, () => Is.SameAs(source));
        AddAssert("long practice restores original mods", () => SelectedMods.Value.Single() is BmsModMirror);
        AddAssert("long practice saves no scores", () => Realm.Run(realm => realm.All<ScoreInfo>().Count()), () => Is.EqualTo(scoreCountAtTestStart));
        AddAssert("source still contains every original chart object", () => source.Beatmap.HitObjects.Select(longPracticeObjectIdentity),
            () => Is.EqualTo(originalObjects));
    }

    private static (double Time, double End, int Column) longPracticeObjectIdentity(HitObject note)
        => (note.StartTime, note.GetEndTime(), ((BmsHitObject)note).Column);
}
